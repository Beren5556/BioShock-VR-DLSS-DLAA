#pragma once
#include "core/gfx/sampled_gpu_timer.h"
#include <array>

namespace bvr {
// A sparse, non-blocking timeline for ONE eye. Only the immediate-context
// owner calls this. Timestamps measure queue elapsed intervals (including
// scheduling/idle gaps), not exclusive GPU busy time or pure NGX duration.
class EyeTimeline {
public:
    enum Point { SceneStart, SceneEnd, CaptureStart, GuidesEnd, CaptureEnd, Points };
    struct Result {
        int eye = -1, outcome = -1;
        uint64_t build = 0, startMs = 0, endMs = 0;
        bool cameraValid = false;
        float position[3]{};
        int32_t rotation[3]{};
        std::array<double, Points-1> cpuMs{}, gpuMs{};
    };
private:
    struct Slot {
        ID3D11Query* disjoint = nullptr;
        std::array<ID3D11Query*, Points> stamps{};
        std::array<double, Points> cpu{};
        Result result{};
        unsigned marks = 0;
        bool pending = false;
    };
    std::array<Slot, 8> slots_{};
    ID3D11DeviceContext* context_ = nullptr;
    int active_ = -1;
    uint64_t calls_[2]{}, stride_ = 16, dropped_ = 0;
    DWORD thread_ = 0;
    bool owner() const { return context_ && thread_ == GetCurrentThreadId(); }
public:
    EyeTimeline() = default;
    EyeTimeline(const EyeTimeline&) = delete;
    EyeTimeline& operator=(const EyeTimeline&) = delete;
    ~EyeTimeline() { release(); }
    bool ready() const { return context_ && slots_[0].disjoint; }
    bool active() const { return active_ >= 0; }
    uint64_t dropped() const { return dropped_; }
    bool matches(int eye, uint64_t build) const {
        return active() && slots_[active_].result.eye == eye && slots_[active_].result.build == build;
    }
    void cancel() {
        if (!active() || !owner()) return;
        auto& s = slots_[active_];
        context_->End(s.disjoint);
        s.pending = true; s.marks = 0; active_ = -1; ++dropped_;
    }
    void release() {
        cancel();
        for (auto& s : slots_) {
            if (s.disjoint) s.disjoint->Release();
            for (auto* q : s.stamps) if (q) q->Release();
            s = {};
        }
        if (context_) context_->Release();
        context_ = nullptr; active_ = -1; thread_ = 0;
        calls_[0] = calls_[1] = dropped_ = 0;
    }
    bool prepare(ID3D11Device* device, uint64_t stride = 16) {
        release(); stride_ = stride ? stride : 1;
        if (!device) return false;
        device->GetImmediateContext(&context_); thread_ = GetCurrentThreadId();
        if (!context_) return false;
        for (auto& s : slots_) {
            D3D11_QUERY_DESC desc{D3D11_QUERY_TIMESTAMP_DISJOINT, 0};
            if (FAILED(device->CreateQuery(&desc, &s.disjoint))) { release(); return false; }
            desc.Query = D3D11_QUERY_TIMESTAMP;
            for (auto& q : s.stamps)
                if (FAILED(device->CreateQuery(&desc, &q))) { release(); return false; }
        }
        return true;
    }
    template<class Receive> void poll(Receive receive) {
        if (!owner()) return;
        constexpr UINT flags = D3D11_ASYNC_GETDATA_DONOTFLUSH;
        for (auto& s : slots_) {
            if (!s.pending) continue;
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT disjoint{};
            const HRESULT hr = context_->GetData(s.disjoint, &disjoint, sizeof(disjoint), flags);
            if (hr == S_FALSE) continue;
            if (FAILED(hr) || disjoint.Disjoint || !disjoint.Frequency || s.marks != Points) {
                if (s.marks == Points) ++dropped_;
                s.pending = false; continue;
            }
            std::array<UINT64, Points> ticks{};
            bool waiting = false, invalid = false;
            for (unsigned i=0; i<Points; ++i) {
                const HRESULT got = context_->GetData(s.stamps[i], &ticks[i], sizeof(ticks[i]), flags);
                waiting |= got == S_FALSE; invalid |= FAILED(got);
            }
            if (waiting && !invalid) continue;
            for (unsigned i=1; i<Points; ++i) invalid |= ticks[i] < ticks[i-1];
            s.pending = false;
            if (invalid) { ++dropped_; continue; }
            for (unsigned i=0; i<Points-1; ++i) {
                s.result.gpuMs[i] = 1000.0 * double(ticks[i+1]-ticks[i]) / double(disjoint.Frequency);
                s.result.cpuMs[i] = s.cpu[i+1]-s.cpu[i];
            }
            receive(s.result);
        }
    }
    bool begin(int eye, uint64_t build) {
        if (!owner() || !ready() || eye < 0 || eye > 1 || !build || active()) return false;
        if (++calls_[eye] % stride_) return false;
        for (unsigned i=0; i<slots_.size(); ++i) if (!slots_[i].pending) {
            auto& s = slots_[i];
            s.result = {}; s.result.eye = eye; s.result.build = build;
            s.result.startMs = GetTickCount64(); s.marks = 0;
            active_ = static_cast<int>(i);
            context_->Begin(s.disjoint); mark(SceneStart);
            return true;
        }
        ++dropped_; return false; // Ring full: lose a measurement, never wait for a frame.
    }
    void mark(Point point) {
        if (!owner() || !active()) return;
        auto& s = slots_[active_];
        if (s.marks != static_cast<unsigned>(point)) { cancel(); return; }
        s.cpu[point] = diagnostic_clock_ms();
        context_->End(s.stamps[point]); ++s.marks;
    }
    void camera(const float* position, const int32_t* rotation) {
        if (!owner() || !active() || !position || !rotation) return;
        auto& r = slots_[active_].result; r.cameraValid = true;
        for (int i=0; i<3; ++i) { r.position[i]=position[i]; r.rotation[i]=rotation[i]; }
    }
    void finish(int outcome) {
        if (!owner() || !active()) return;
        mark(CaptureEnd);
        if (!active()) return;
        auto& s = slots_[active_];
        s.result.outcome = outcome; s.result.endMs = GetTickCount64();
        context_->End(s.disjoint); s.pending = true; active_ = -1;
    }
};
}
