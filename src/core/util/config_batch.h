#pragma once
// Small explicit-file transaction for configuration, not a filesystem-wide API.
// Stages verified bytes before replacing anything, restores on ordinary failure,
// and retains recovery files if an external writer prevents exact rollback.
// Multi-file crash atomicity is NOT promised; no Windows TxF dependency.
#include "core/util/log.h"
#include <windows.h>
#include <atomic>
#include <mutex>
#include <string>
#include <vector>

namespace bvr::config_batch {
inline bool read(const std::wstring& path, std::string& bytes, bool& exists) {
    bytes.clear(); exists = false;
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) return GetLastError() == ERROR_FILE_NOT_FOUND;
    if (attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) return false;
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{}; DWORD got = 0;
    bool ok = GetFileSizeEx(file, &size) && size.QuadPart >= 0 && size.QuadPart <= (8 << 20);
    if (ok) {
        bytes.resize(static_cast<size_t>(size.QuadPart));
        ok = ReadFile(file, bytes.data(), DWORD(bytes.size()), &got, nullptr) && got == bytes.size();
    }
    CloseHandle(file);
    exists = ok; return ok;
}
inline bool matches(const std::wstring& path, bool expectedExists, const std::string& expected) {
    std::string actual; bool exists = false;
    return read(path, actual, exists) && exists == expectedExists && (!exists || actual == expected);
}
inline bool create_verified(const std::wstring& path, const std::string& bytes) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    bool ok = WriteFile(file, bytes.data(), DWORD(bytes.size()), &written, nullptr) &&
              written == bytes.size() && FlushFileBuffers(file);
    CloseHandle(file);
    ok = ok && matches(path, true, bytes);
    if (!ok) DeleteFileW(path.c_str()); // only the file this invocation created
    return ok;
}
#ifdef BVR_CONFIG_BATCH_TESTS
// In-memory fault injection, absent from shipping builds.
inline bool (*before_replace)(size_t index) = nullptr;
#endif
class Batch {
    struct Entry {
        std::wstring path, backupSuffix, staged, rollback;
        std::string before, after;
        bool existed = false, changed = false;
    };
    std::vector<Entry> entries_;
    bool preserveRecovery_ = false, attempted_ = false;
    void cleanup() {
        if (preserveRecovery_) return;
        for (const auto& e : entries_) {
            if (!e.staged.empty()) DeleteFileW(e.staged.c_str());
            if (!e.rollback.empty()) DeleteFileW(e.rollback.c_str());
        }
    }
    bool restore() {
        bool ok = true;
        for (auto it = entries_.rbegin(); it != entries_.rend(); ++it) {
            auto& e = *it;
            if (!e.changed) continue;
            if (!matches(e.path, true, e.after)) { ok = false; continue; }
            const bool restored = e.existed ?
                MoveFileExW(e.rollback.c_str(), e.path.c_str(),
                            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE :
                DeleteFileW(e.path.c_str()) != FALSE;
            if (!restored || !matches(e.path, e.existed, e.before)) ok = false;
        }
        preserveRecovery_ = !ok;
        if (!ok) for (const auto& e : entries_)
            BVR_LOG("[config] rollback conflict; recovery files retained beside %ls", e.path.c_str());
        return ok;
    }
public:
    Batch() = default;
    Batch(const Batch&) = delete;
    Batch& operator=(const Batch&) = delete;
    ~Batch() { cleanup(); }
    bool add(const std::wstring& path, bool existed, const std::string& before,
             const std::string& after, const wchar_t* backupSuffix = L"") {
        if (attempted_ || path.empty() || after.size() > (8 << 20)) return false;
        for (const auto& e : entries_)
            if (_wcsicmp(path.c_str(), e.path.c_str()) == 0) return false;
        entries_.push_back({path, backupSuffix, {}, {}, before, after, existed, false});
        return true;
    }
    bool commit() {
        if (attempted_) return false;
        attempted_ = true;
        static std::mutex mutex;
        static std::atomic<unsigned> serial{0};
        std::lock_guard<std::mutex> guard(mutex);
        const auto suffix = L".bvr-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
                            std::to_wstring(GetTickCount64()) + L"-" + std::to_wstring(++serial);
        // Prepare the entire set and its recovery copies before the first rename.
        for (auto& e : entries_) {
            if (!matches(e.path, e.existed, e.before)) return false;
            if (e.existed && e.before == e.after) continue;
            if (e.existed && (GetFileAttributesW(e.path.c_str()) & FILE_ATTRIBUTE_READONLY)) return false;
            if (e.existed && !e.backupSuffix.empty()) {
                const auto original = e.path + e.backupSuffix;
                std::string bytes; bool exists = false;
                if (!read(original, bytes, exists) ||
                    (!exists && !create_verified(original, e.before))) return false;
            }
            const auto staged = e.path + suffix + L".tmp";
            if (!create_verified(staged, e.after)) return false;
            e.staged = staged;
            if (e.existed) {
                const auto rollback = e.path + suffix + L".rollback";
                if (!create_verified(rollback, e.before)) return false;
                e.rollback = rollback;
            }
        }
        for (const auto& e : entries_)
            if (!matches(e.path, e.existed, e.before)) return false;
        for (size_t i = 0; i < entries_.size(); ++i) {
            auto& e = entries_[i];
            if (e.staged.empty()) continue;
#ifdef BVR_CONFIG_BATCH_TESTS
            if (before_replace && !before_replace(i)) { restore(); return false; }
#endif
            if (!matches(e.path, e.existed, e.before) ||
                !MoveFileExW(e.staged.c_str(), e.path.c_str(),
                             (e.existed ? MOVEFILE_REPLACE_EXISTING : 0) | MOVEFILE_WRITE_THROUGH)) {
                restore(); return false;
            }
            e.changed = true;
            if (!matches(e.path, true, e.after)) { restore(); return false; }
        }
        return true;
    }
};
} // namespace bvr::config_batch
