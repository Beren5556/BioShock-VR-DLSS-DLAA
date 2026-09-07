// Synthetic 32-bit D3D11 stereo client for the BioShock VR DLSS 4.5 bridge.
//
// It deliberately keeps one complete IPC/resource/fence set per eye and feeds the
// two hosts in L/R order.  Every result is brought home in the same frame and read
// back: eye 0 is red, eye 1 is blue, so accidental resource/history crossing is
// observable rather than inferred from process names alone.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_4.h>
#include <dxgi1_2.h>

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

#include "../src/feed_ipc.h"

static_assert(sizeof(void *) == 4, "bvr-stereo-client32 must be compiled as x86");

namespace
{

constexpr UINT kReadbackSize = 16;
constexpr DWORD kPipeStartupTimeoutMs = 30000;
constexpr DWORD kFrameTimeoutMs = 30000;

template <typename T>
void Release(T *&p)
{
    if (p != nullptr)
    {
        p->Release();
        p = nullptr;
    }
}

std::wstring JoinPath(const std::wstring &a, const std::wstring &b)
{
    if (a.empty()) return b;
    if (a.back() == L'\\' || a.back() == L'/') return a + b;
    return a + L"\\" + b;
}

bool Exists(const std::wstring &path)
{
    return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
}

std::string Narrow(const std::wstring &s)
{
    if (s.empty()) return {};
    const int n = WideCharToMultiByte(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()),
                                      nullptr, 0, nullptr, nullptr);
    std::string out(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()),
                        out.data(), n, nullptr, nullptr);
    return out;
}

std::wstring AbsolutePath(const std::wstring &path)
{
    const DWORD need = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
    if (need == 0) return path;
    std::vector<wchar_t> buf(static_cast<size_t>(need));
    if (GetFullPathNameW(path.c_str(), need, buf.data(), nullptr) == 0) return path;
    return std::wstring(buf.data());
}

bool WriteFull(HANDLE pipe, const void *data, DWORD bytes)
{
    const BYTE *p = static_cast<const BYTE *>(data);
    while (bytes != 0)
    {
        DWORD put = 0;
        if (!WriteFile(pipe, p, bytes, &put, nullptr) || put == 0) return false;
        p += put;
        bytes -= put;
    }
    return true;
}

bool ReadFull(HANDLE pipe, void *data, DWORD bytes)
{
    BYTE *p = static_cast<BYTE *>(data);
    while (bytes != 0)
    {
        DWORD got = 0;
        if (!ReadFile(pipe, p, bytes, &got, nullptr) || got == 0) return false;
        p += got;
        bytes -= got;
    }
    return true;
}

struct Eye
{
    int index = -1;
    std::wstring directory;
    std::wstring host_exe;
    std::wstring log_path;
    HANDLE process = nullptr;
    DWORD process_id = 0;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    HANDLE done_event = nullptr;

    ID3D11Texture2D *tex[FEED_SLOTS] = {};
    HANDLE shared_handle[FEED_SLOTS] = {};
    ID3D11RenderTargetView *color_rtv = nullptr;
    ID3D11RenderTargetView *depth_rtv = nullptr;
    ID3D11RenderTargetView *mv_rtv = nullptr;
    ID3D11Texture2D *readback = nullptr;
    ID3D11Fence *fence_in = nullptr;
    ID3D11Fence *fence_out = nullptr;

    uint64_t submitted = 0;
    uint64_t completed = 0;
    uint64_t checked = 0;
    uint64_t black = 0;
    uint64_t wrong_eye = 0;
    double last_r = 0.0;
    double last_g = 0.0;
    double last_b = 0.0;
};

struct Options
{
    bool sr = false;
    UINT frames = 300;
    std::wstring runtime_root;
};

void Usage()
{
    std::printf(
        "usage: bvr-stereo-client32 --mode dlaa|sr --runtime-root <folder> [--frames 300]\n"
        "  <folder> must contain eye0\\ and eye1\\, each with the packaged x64 host,\n"
        "  official nvngx_dlss.dll, and dlss-capabilities.ini.  This client launches both hosts.\n");
}

bool ParseOptions(int argc, wchar_t **argv, Options &o)
{
    for (int i = 1; i < argc; ++i)
    {
        if (wcscmp(argv[i], L"--mode") == 0 && i + 1 < argc)
        {
            const wchar_t *v = argv[++i];
            if (_wcsicmp(v, L"sr") == 0) o.sr = true;
            else if (_wcsicmp(v, L"dlaa") == 0) o.sr = false;
            else return false;
        }
        else if (wcscmp(argv[i], L"--runtime-root") == 0 && i + 1 < argc)
            o.runtime_root = AbsolutePath(argv[++i]);
        else if (wcscmp(argv[i], L"--frames") == 0 && i + 1 < argc)
        {
            const unsigned long v = wcstoul(argv[++i], nullptr, 10);
            if (v == 0 || v > 100000) return false;
            o.frames = static_cast<UINT>(v);
        }
        else if (wcscmp(argv[i], L"--help") == 0 || wcscmp(argv[i], L"-h") == 0)
        {
            Usage();
            std::exit(0);
        }
        else return false;
    }
    return !o.runtime_root.empty();
}

bool CheckCleanRuntime(Eye &eye, const std::wstring &root)
{
    eye.directory = JoinPath(root, eye.index == 0 ? L"eye0" : L"eye1");
    eye.log_path = JoinPath(
        eye.directory,
        eye.index == 0 ? L"BioShockVR-DLSS45-eye0.log" : L"BioShockVR-DLSS45-eye1.log");
    eye.host_exe = JoinPath(eye.directory, L"BioShockVR-DLSS45-Host64.exe");

    const wchar_t *required[] = {
        L"BioShockVR-DLSS45-Host64.exe", L"nvngx_dlss.dll", L"dlss-capabilities.ini"
    };
    for (const wchar_t *name : required)
    {
        const std::wstring path = JoinPath(eye.directory, name);
        if (!Exists(path))
        {
            std::printf("FAIL eye%d: missing %s\n", eye.index, Narrow(path).c_str());
            return false;
        }
    }

    // Phase 1 is deliberately DLSS 4.5 SR/DLAA only.  A local proxy or neural
    // rendering add-on would make this transport test ambiguous, so reject it.
    const wchar_t *forbidden[] = {
        L"nvngx_dlssnr.dll", L"renodx-dlss5.addon64", L"dlss5-feed.addon64",
        L"dxgi.dll", L"d3d11.dll", L"ReShade64.dll", L"ReShade32.dll"
    };
    for (const wchar_t *name : forbidden)
    {
        const std::wstring path = JoinPath(eye.directory, name);
        if (Exists(path))
        {
            std::printf("FAIL eye%d: runtime is not clean; forbidden phase-1 file: %s\n",
                        eye.index, Narrow(path).c_str());
            return false;
        }
    }
    return true;
}

bool StartHost(Eye &eye, DWORD client_pid)
{
    std::wstring command = L"\"" + eye.host_exe + L"\" " + std::to_wstring(client_pid) +
                           L" --eye " + std::to_wstring(eye.index) + L" --hide";
    std::vector<wchar_t> mutable_command(command.begin(), command.end());
    mutable_command.push_back(L'\0');

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi = {};
    if (!CreateProcessW(eye.host_exe.c_str(), mutable_command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, eye.directory.c_str(), &si, &pi))
    {
        std::printf("FAIL eye%d: CreateProcessW error %lu\n", eye.index, GetLastError());
        return false;
    }
    CloseHandle(pi.hThread);
    eye.process = pi.hProcess;
    eye.process_id = pi.dwProcessId;
    std::printf("eye%d host launched: pid=%lu cwd=%s\n", eye.index,
                static_cast<unsigned long>(eye.process_id), Narrow(eye.directory).c_str());
    return true;
}

bool ConnectPipe(Eye &eye, DWORD client_pid)
{
    wchar_t name[128] = {};
    swprintf_s(name, L"\\\\.\\pipe\\bvr-dlss45.%lu.eye%d",
               static_cast<unsigned long>(client_pid), eye.index);
    const ULONGLONG deadline = GetTickCount64() + kPipeStartupTimeoutMs;
    while (GetTickCount64() < deadline)
    {
        eye.pipe = CreateFileW(name, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                               OPEN_EXISTING, 0, nullptr);
        if (eye.pipe != INVALID_HANDLE_VALUE) break;

        if (eye.process != nullptr && WaitForSingleObject(eye.process, 0) == WAIT_OBJECT_0)
        {
            DWORD code = 0;
            GetExitCodeProcess(eye.process, &code);
            std::printf("FAIL eye%d: host exited before pipe connection (code %lu)\n",
                        eye.index, static_cast<unsigned long>(code));
            return false;
        }
        if (GetLastError() == ERROR_PIPE_BUSY) WaitNamedPipeW(name, 100);
        else Sleep(50);
    }
    if (eye.pipe == INVALID_HANDLE_VALUE)
    {
        std::printf("FAIL eye%d: pipe did not appear within %lu ms: %s\n", eye.index,
                    static_cast<unsigned long>(kPipeStartupTimeoutMs), Narrow(name).c_str());
        return false;
    }
    std::printf("eye%d pipe connected: %s\n", eye.index, Narrow(name).c_str());
    return true;
}

bool Hello(Eye &eye, DWORD client_pid)
{
    HANDLE self_in_host = nullptr;
    if (!DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), eye.process,
                         &self_in_host,
                         PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION,
                         FALSE, 0))
    {
        std::printf("FAIL eye%d: DuplicateHandle(self -> host) error %lu\n",
                    eye.index, GetLastError());
        return false;
    }

    FeedHello hello = {};
    hello.magic = FEED_IPC_MAGIC;
    hello.version = FEED_IPC_VERSION;
    hello.pid = client_pid;
    hello.client_kind = FEED_CLIENT_D3D11;
    hello.self_process = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(self_in_host));
    FeedHelloAck ack = {};
    if (!WriteFull(eye.pipe, &hello, sizeof(hello)) || !ReadFull(eye.pipe, &ack, sizeof(ack)))
    {
        std::printf("FAIL eye%d: hello exchange failed (error %lu)\n", eye.index, GetLastError());
        return false;
    }
    if (ack.magic != FEED_IPC_MAGIC || ack.version != FEED_IPC_VERSION)
    {
        std::printf("FAIL eye%d: protocol mismatch (magic 0x%08X, host v%u, client v%u)\n",
                    eye.index, ack.magic, ack.version, FEED_IPC_VERSION);
        return false;
    }
    std::printf("eye%d hello ok: protocol v%u, host panel %ux%u (unused)\n",
                eye.index, ack.version, ack.panel_width, ack.panel_height);
    return true;
}

bool MakeSharedTexture(ID3D11Device *device, UINT w, UINT h, DXGI_FORMAT format,
                       UINT bind_flags, ID3D11Texture2D **texture, HANDLE *shared_handle)
{
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = w;
    desc.Height = h;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = bind_flags;
    desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE | D3D11_RESOURCE_MISC_SHARED;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, texture);
    if (FAILED(hr))
    {
        std::printf("CreateTexture2D %ux%u fmt=%u failed 0x%08X\n", w, h, format,
                    static_cast<unsigned>(hr));
        return false;
    }

    IDXGIResource1 *resource = nullptr;
    hr = (*texture)->QueryInterface(__uuidof(IDXGIResource1),
                                    reinterpret_cast<void **>(&resource));
    if (SUCCEEDED(hr))
    {
        hr = resource->CreateSharedHandle(nullptr,
                                          DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE,
                                          nullptr, shared_handle);
        resource->Release();
    }
    if (FAILED(hr))
    {
        std::printf("CreateSharedHandle failed 0x%08X\n", static_cast<unsigned>(hr));
        return false;
    }
    return true;
}

bool CreateEyeResources(Eye &eye, ID3D11Device *device, UINT work_w, UINT work_h,
                        UINT output_w, UINT output_h)
{
    if (!MakeSharedTexture(device, work_w, work_h, DXGI_FORMAT_R8G8B8A8_UNORM,
                           D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET,
                           &eye.tex[FEED_COLOR], &eye.shared_handle[FEED_COLOR]) ||
        !MakeSharedTexture(device, output_w, output_h, DXGI_FORMAT_R8G8B8A8_UNORM,
                           D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS,
                           &eye.tex[FEED_OUTPUT], &eye.shared_handle[FEED_OUTPUT]) ||
        !MakeSharedTexture(device, work_w, work_h, DXGI_FORMAT_R32_FLOAT,
                           D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET,
                           &eye.tex[FEED_DEPTH], &eye.shared_handle[FEED_DEPTH]) ||
        !MakeSharedTexture(device, work_w, work_h, DXGI_FORMAT_R16G16_FLOAT,
                           D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET,
                           &eye.tex[FEED_MV], &eye.shared_handle[FEED_MV]))
        return false;

    if (FAILED(device->CreateRenderTargetView(eye.tex[FEED_COLOR], nullptr, &eye.color_rtv)) ||
        FAILED(device->CreateRenderTargetView(eye.tex[FEED_DEPTH], nullptr, &eye.depth_rtv)) ||
        FAILED(device->CreateRenderTargetView(eye.tex[FEED_MV], nullptr, &eye.mv_rtv)))
    {
        std::printf("FAIL eye%d: input RTV creation failed\n", eye.index);
        return false;
    }

    D3D11_TEXTURE2D_DESC readback = {};
    readback.Width = kReadbackSize;
    readback.Height = kReadbackSize;
    readback.MipLevels = 1;
    readback.ArraySize = 1;
    readback.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    readback.SampleDesc.Count = 1;
    readback.Usage = D3D11_USAGE_STAGING;
    readback.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    if (FAILED(device->CreateTexture2D(&readback, nullptr, &eye.readback)))
    {
        std::printf("FAIL eye%d: readback texture creation failed\n", eye.index);
        return false;
    }
    eye.done_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (eye.done_event == nullptr)
    {
        std::printf("FAIL eye%d: completion event creation failed (%lu)\n",
                    eye.index, GetLastError());
        return false;
    }
    return true;
}

bool Build(Eye &eye, ID3D11Device5 *device5, bool sr, UINT work_w, UINT work_h,
           UINT output_w, UINT output_h)
{
    FeedBuild build = {};
    build.width = work_w;
    build.height = work_h;
    build.color_fmt = DXGI_FORMAT_R8G8B8A8_UNORM;
    build.output_fmt = DXGI_FORMAT_R8G8B8A8_UNORM;
    build.hdr = 0;
    // BioShock Remastered's audited projection maps near to 0 and far to 1.
    build.depth_inverted = 0;
    build.flags_override = -1;
    build.transport = 0;
    build.mv_scale_x = 1.0f;
    build.mv_scale_y = 1.0f;
    for (int i = 0; i < FEED_SLOTS; ++i)
        build.tex[i] = static_cast<uint64_t>(reinterpret_cast<uintptr_t>(eye.shared_handle[i]));
    // FEED_BUILD_ASYNC_HOME is intentionally absent: this test waits for n and
    // reads Output(n) before it ever submits the other eye's next frame.
    build.client_flags = 0;
    if (sr)
    {
        build.target_width = output_w;
        build.target_height = output_h;
    }

    const BYTE tag = 'B';
    FeedBuildAck ack = {};
    if (!WriteFull(eye.pipe, &tag, 1) || !WriteFull(eye.pipe, &build, sizeof(build)) ||
        !ReadFull(eye.pipe, &ack, sizeof(ack)))
    {
        std::printf("FAIL eye%d: build exchange failed (error %lu)\n", eye.index, GetLastError());
        return false;
    }
    if (!ack.ok)
    {
        std::printf("FAIL eye%d: host rejected build (NGX 0x%08X, flags 0x%X)\n",
                    eye.index, ack.ngx_result, ack.flags);
        return false;
    }
    const bool sr_active = (ack.flags & FEED_ACK_SR_ACTIVE) != 0;
    if (sr_active != sr)
    {
        std::printf("FAIL eye%d: requested %s but host acknowledged %s (flags 0x%X)\n",
                    eye.index, sr ? "SR" : "DLAA", sr_active ? "SR" : "DLAA", ack.flags);
        return false;
    }
    // This harness requests exactly 960x540 -> 1920x1080 (ratio 0.5).
    // NVSDK_NGX_PerfQuality_Value_MaxPerf is 0; accepting Quality (2) here
    // would reintroduce the overlapping-range selector regression.
    if (sr && ack.sr_quality != 0u)
    {
        std::printf("FAIL eye%d: ratio 0.5 must select Performance (0), host returned %u\n",
                    eye.index, ack.sr_quality);
        return false;
    }
    if (ack.output_fmt != DXGI_FORMAT_R8G8B8A8_UNORM)
    {
        std::printf("FAIL eye%d: unexpected output format %u\n", eye.index, ack.output_fmt);
        return false;
    }

    HANDLE in_handle = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(ack.fence_in));
    HANDLE out_handle = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(ack.fence_out));
    const HRESULT hr_in = device5->OpenSharedFence(in_handle, __uuidof(ID3D11Fence),
                                                   reinterpret_cast<void **>(&eye.fence_in));
    const HRESULT hr_out = device5->OpenSharedFence(out_handle, __uuidof(ID3D11Fence),
                                                    reinterpret_cast<void **>(&eye.fence_out));
    if (in_handle != nullptr) CloseHandle(in_handle);
    if (out_handle != nullptr) CloseHandle(out_handle);
    if (FAILED(hr_in) || FAILED(hr_out) || eye.fence_in == nullptr || eye.fence_out == nullptr)
    {
        std::printf("FAIL eye%d: OpenSharedFence failed 0x%08X/0x%08X\n",
                    eye.index, static_cast<unsigned>(hr_in), static_cast<unsigned>(hr_out));
        return false;
    }

    std::printf("eye%d build ok: %ux%u -> %ux%u %s, protocol v%u, async_home=0",
                eye.index, work_w, work_h, output_w, output_h,
                sr ? "DLSS 4.5 SR" : "DLAA 4.5", FEED_IPC_VERSION);
    if (sr) std::printf(", sr_quality=%u", ack.sr_quality);
    std::printf("\n");
    return true;
}

float Halton(UINT index, UINT base)
{
    float f = 1.0f;
    float result = 0.0f;
    while (index != 0)
    {
        f /= static_cast<float>(base);
        result += f * static_cast<float>(index % base);
        index /= base;
    }
    return result;
}

bool WaitForHostFence(Eye &eye, uint64_t value)
{
    if (eye.fence_out->GetCompletedValue() >= value) return true;
    ResetEvent(eye.done_event);
    const HRESULT hr = eye.fence_out->SetEventOnCompletion(value, eye.done_event);
    if (FAILED(hr))
    {
        std::printf("FAIL eye%d frame %llu: SetEventOnCompletion failed 0x%08X\n",
                    eye.index, static_cast<unsigned long long>(value), static_cast<unsigned>(hr));
        return false;
    }
    HANDLE waits[2] = { eye.done_event, eye.process };
    const DWORD wait = WaitForMultipleObjects(2, waits, FALSE, kFrameTimeoutMs);
    if (wait == WAIT_OBJECT_0) return true;
    if (wait == WAIT_OBJECT_0 + 1)
    {
        DWORD code = 0;
        GetExitCodeProcess(eye.process, &code);
        std::printf("FAIL eye%d frame %llu: host exited while waiting (code %lu)\n",
                    eye.index, static_cast<unsigned long long>(value),
                    static_cast<unsigned long>(code));
    }
    else
        std::printf("FAIL eye%d frame %llu: fence timeout/error (wait=0x%08lX)\n",
                    eye.index, static_cast<unsigned long long>(value),
                    static_cast<unsigned long>(wait));
    return false;
}

bool ReadAndClassify(Eye &eye, ID3D11DeviceContext *context, UINT output_w, UINT output_h)
{
    D3D11_BOX source = {};
    source.left = output_w / 2 - kReadbackSize / 2;
    source.top = output_h / 2 - kReadbackSize / 2;
    source.front = 0;
    source.right = source.left + kReadbackSize;
    source.bottom = source.top + kReadbackSize;
    source.back = 1;
    context->CopySubresourceRegion(eye.readback, 0, 0, 0, 0,
                                   eye.tex[FEED_OUTPUT], 0, &source);
    context->Flush();

    D3D11_MAPPED_SUBRESOURCE mapped = {};
    const HRESULT hr = context->Map(eye.readback, 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr))
    {
        std::printf("FAIL eye%d frame %llu: output Map failed 0x%08X\n",
                    eye.index, static_cast<unsigned long long>(eye.submitted),
                    static_cast<unsigned>(hr));
        return false;
    }

    uint64_t sum_r = 0, sum_g = 0, sum_b = 0;
    for (UINT y = 0; y < kReadbackSize; ++y)
    {
        const BYTE *row = static_cast<const BYTE *>(mapped.pData) + y * mapped.RowPitch;
        for (UINT x = 0; x < kReadbackSize; ++x)
        {
            sum_r += row[x * 4 + 0];
            sum_g += row[x * 4 + 1];
            sum_b += row[x * 4 + 2];
        }
    }
    context->Unmap(eye.readback, 0);

    const double denom = 255.0 * static_cast<double>(kReadbackSize * kReadbackSize);
    eye.last_r = static_cast<double>(sum_r) / denom;
    eye.last_g = static_cast<double>(sum_g) / denom;
    eye.last_b = static_cast<double>(sum_b) / denom;
    ++eye.checked;
    if (eye.last_r + eye.last_g + eye.last_b < 0.03) ++eye.black;

    // A generous margin survives auto-exposure and the first temporal frames while
    // still making a red/blue eye swap unambiguous.
    const bool correct = eye.index == 0
        ? eye.last_r > eye.last_b + 0.08
        : eye.last_b > eye.last_r + 0.08;
    if (!correct) ++eye.wrong_eye;
    return true;
}

bool SubmitFrame(Eye &eye, ID3D11DeviceContext *context, ID3D11DeviceContext4 *context4,
                 bool sr, UINT output_w, UINT output_h, UINT frame)
{
    const float wave = static_cast<float>(static_cast<int>(frame % 31) - 15) / 300.0f;
    const float color0[4] = { 0.82f + wave, 0.035f, 0.015f, 1.0f };
    const float color1[4] = { 0.015f, 0.035f, 0.82f + wave, 1.0f };
    const float depth[4] = { 0.55f, 0.55f, 0.55f, 0.55f };
    const float motion[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
    context->ClearRenderTargetView(eye.color_rtv, eye.index == 0 ? color0 : color1);
    context->ClearRenderTargetView(eye.depth_rtv, depth);
    context->ClearRenderTargetView(eye.mv_rtv, motion);

    const uint64_t n = ++eye.submitted;
    HRESULT hr = context4->Signal(eye.fence_in, n);
    if (FAILED(hr))
    {
        std::printf("FAIL eye%d frame %llu: input Signal failed 0x%08X\n",
                    eye.index, static_cast<unsigned long long>(n), static_cast<unsigned>(hr));
        return false;
    }
    context->Flush();

    FeedFrameMsg frame_msg = {};
    frame_msg.n = n;
    frame_msg.reset = n == 1 ? 1u : 0u;
    if (sr)
    {
        frame_msg.jitter_x = Halton(frame, 2) - 0.5f;
        frame_msg.jitter_y = Halton(frame, 3) - 0.5f;
    }
    const BYTE tag = 'F';
    if (!WriteFull(eye.pipe, &tag, 1) || !WriteFull(eye.pipe, &frame_msg, sizeof(frame_msg)))
    {
        std::printf("FAIL eye%d frame %llu: pipe write failed (error %lu)\n",
                    eye.index, static_cast<unsigned long long>(n), GetLastError());
        return false;
    }

    // Explicit same-frame contract: wait for Output(n), queue the D3D11 fence wait,
    // copy that exact output to staging and Map it before another frame for this eye.
    if (!WaitForHostFence(eye, n)) return false;
    hr = context4->Wait(eye.fence_out, n);
    if (FAILED(hr))
    {
        std::printf("FAIL eye%d frame %llu: output Wait failed 0x%08X\n",
                    eye.index, static_cast<unsigned long long>(n), static_cast<unsigned>(hr));
        return false;
    }
    if (!ReadAndClassify(eye, context, output_w, output_h)) return false;
    eye.completed = n;
    return true;
}

std::string ReadTextFile(const std::wstring &path)
{
    std::ifstream f(path, std::ios::binary);
    if (!f) return {};
    return std::string(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
}

bool VerifyHostLog(const Eye &eye, DWORD client_pid)
{
    const std::string log = ReadTextFile(eye.log_path);
    if (log.empty())
    {
        std::printf("FAIL eye%d: host log missing/empty: %s\n",
                    eye.index, Narrow(eye.log_path).c_str());
        return false;
    }
    char pipe[128] = {};
    sprintf_s(pipe, "\\\\.\\pipe\\bvr-dlss45.%lu.eye%d",
              static_cast<unsigned long>(client_pid), eye.index);
    const char *required[] = {
        pipe,
        "protocol v8, D3D11 client",
        "frame 1 evaluated",
        "frame 2 evaluated",
        "frame 3 evaluated",
        "pending game fence waits released"
    };
    bool ok = true;
    for (const char *needle : required)
    {
        if (log.find(needle) == std::string::npos)
        {
            std::printf("FAIL eye%d log: missing '%s'\n", eye.index, needle);
            ok = false;
        }
    }

    char other_pipe[128] = {};
    sprintf_s(other_pipe, "\\\\.\\pipe\\bvr-dlss45.%lu.eye%d",
              static_cast<unsigned long>(client_pid), 1 - eye.index);
    if (log.find(other_pipe) != std::string::npos)
    {
        std::printf("FAIL eye%d log contains the other eye's pipe: %s\n",
                    eye.index, other_pipe);
        ok = false;
    }
    std::printf("eye%d log %s: %s\n", eye.index, ok ? "verified" : "FAILED",
                Narrow(eye.log_path).c_str());
    return ok;
}

void ClosePipes(Eye eyes[2])
{
    for (int i = 0; i < 2; ++i)
    {
        Eye &eye = eyes[i];
        if (eye.pipe != INVALID_HANDLE_VALUE)
        {
            CloseHandle(eye.pipe);
            eye.pipe = INVALID_HANDLE_VALUE;
        }
    }
}

bool WaitForHosts(Eye eyes[2])
{
    bool ok = true;
    for (int i = 0; i < 2; ++i)
    {
        Eye &eye = eyes[i];
        if (eye.process == nullptr) continue;
        DWORD wait = WaitForSingleObject(eye.process, 30000);
        if (wait == WAIT_TIMEOUT)
        {
            std::printf("FAIL eye%d: host did not exit after pipe close; terminating test child\n", eye.index);
            TerminateProcess(eye.process, 99);
            WaitForSingleObject(eye.process, 5000);
            ok = false;
        }
        DWORD code = 0;
        if (!GetExitCodeProcess(eye.process, &code) || code != 0)
        {
            std::printf("FAIL eye%d: host exit code %lu\n", eye.index,
                        static_cast<unsigned long>(code));
            ok = false;
        }
    }
    return ok;
}

void CleanupEye(Eye &eye)
{
    Release(eye.fence_in);
    Release(eye.fence_out);
    Release(eye.readback);
    Release(eye.color_rtv);
    Release(eye.depth_rtv);
    Release(eye.mv_rtv);
    for (int i = 0; i < FEED_SLOTS; ++i)
    {
        Release(eye.tex[i]);
        if (eye.shared_handle[i] != nullptr)
        {
            CloseHandle(eye.shared_handle[i]);
            eye.shared_handle[i] = nullptr;
        }
    }
    if (eye.done_event != nullptr) CloseHandle(eye.done_event);
    if (eye.process != nullptr) CloseHandle(eye.process);
    eye.done_event = nullptr;
    eye.process = nullptr;
}

} // namespace

int wmain(int argc, wchar_t **argv)
{
    Options options;
    if (!ParseOptions(argc, argv, options))
    {
        Usage();
        return 2;
    }

    const UINT work_w = options.sr ? 960u : 640u;
    const UINT work_h = options.sr ? 540u : 360u;
    const UINT output_w = options.sr ? 1920u : work_w;
    const UINT output_h = options.sr ? 1080u : work_h;
    const DWORD client_pid = GetCurrentProcessId();
    std::printf("BioShock VR stereo bridge test: x86 client pid=%lu, IPC v%u, mode=%s, %u frames/eye\n",
                static_cast<unsigned long>(client_pid), FEED_IPC_VERSION,
                options.sr ? "DLSS 4.5 SR" : "DLAA 4.5", options.frames);
    std::printf("Two independent hosts/resources/histories; same-frame sync; feed order L,R.\n");

    Eye eyes[2];
    eyes[0].index = 0;
    eyes[1].index = 1;
    bool ok = CheckCleanRuntime(eyes[0], options.runtime_root) &&
              CheckCleanRuntime(eyes[1], options.runtime_root);

    ID3D11Device *device = nullptr;
    ID3D11DeviceContext *context = nullptr;
    ID3D11Device5 *device5 = nullptr;
    ID3D11DeviceContext4 *context4 = nullptr;
    D3D_FEATURE_LEVEL actual_level = D3D_FEATURE_LEVEL_9_1;
    if (ok)
    {
        const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                                       D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                       levels, _countof(levels), D3D11_SDK_VERSION,
                                       &device, &actual_level, &context);
        if (hr == E_INVALIDARG)
            hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                                   D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                   &levels[1], 1, D3D11_SDK_VERSION,
                                   &device, &actual_level, &context);
        if (FAILED(hr) || device == nullptr || context == nullptr)
        {
            std::printf("FAIL: D3D11CreateDevice failed 0x%08X\n", static_cast<unsigned>(hr));
            ok = false;
        }
        else
        {
            hr = device->QueryInterface(__uuidof(ID3D11Device5), reinterpret_cast<void **>(&device5));
            const HRESULT hr2 = context->QueryInterface(__uuidof(ID3D11DeviceContext4),
                                                        reinterpret_cast<void **>(&context4));
            if (FAILED(hr) || FAILED(hr2) || device5 == nullptr || context4 == nullptr)
            {
                std::printf("FAIL: D3D11 fence interfaces unavailable 0x%08X/0x%08X\n",
                            static_cast<unsigned>(hr), static_cast<unsigned>(hr2));
                ok = false;
            }
            else
                std::printf("D3D11 device ready: feature level %X, pointer size %u bytes\n",
                            static_cast<unsigned>(actual_level), static_cast<unsigned>(sizeof(void *)));
        }
    }

    if (ok) ok = StartHost(eyes[0], client_pid) && StartHost(eyes[1], client_pid);
    if (ok) ok = ConnectPipe(eyes[0], client_pid) && ConnectPipe(eyes[1], client_pid);
    if (ok) ok = Hello(eyes[0], client_pid) && Hello(eyes[1], client_pid);
    if (ok)
        ok = CreateEyeResources(eyes[0], device, work_w, work_h, output_w, output_h) &&
             CreateEyeResources(eyes[1], device, work_w, work_h, output_w, output_h);
    if (ok)
        ok = Build(eyes[0], device5, options.sr, work_w, work_h, output_w, output_h) &&
             Build(eyes[1], device5, options.sr, work_w, work_h, output_w, output_h);

    if (ok)
    {
        for (UINT frame = 1; frame <= options.frames && ok; ++frame)
        {
            // Strict alternating L/R order, each call completes and validates Output(n).
            ok = SubmitFrame(eyes[0], context, context4, options.sr, output_w, output_h, frame) &&
                 SubmitFrame(eyes[1], context, context4, options.sr, output_w, output_h, frame);
            if (ok && (frame <= 3 || frame % 100 == 0 || frame == options.frames))
            {
                std::printf("stereo frame %u: eye0 #%llu RGB %.3f/%.3f/%.3f | "
                            "eye1 #%llu RGB %.3f/%.3f/%.3f\n",
                            frame,
                            static_cast<unsigned long long>(eyes[0].completed),
                            eyes[0].last_r, eyes[0].last_g, eyes[0].last_b,
                            static_cast<unsigned long long>(eyes[1].completed),
                            eyes[1].last_r, eyes[1].last_g, eyes[1].last_b);
            }
        }
    }

    ClosePipes(eyes);
    const bool hosts_ok = WaitForHosts(eyes);
    bool logs_ok = true;
    for (int i = 0; i < 2; ++i)
    {
        // Only demand a complete log if this eye actually reached the frame loop.
        if (eyes[i].submitted != 0) logs_ok = VerifyHostLog(eyes[i], client_pid) && logs_ok;
    }

    for (const Eye &eye : eyes)
    {
        std::printf("eye%d counters: submitted=%llu completed=%llu checked=%llu black=%llu wrong-eye=%llu "
                    "final-fence=%llu\n",
                    eye.index,
                    static_cast<unsigned long long>(eye.submitted),
                    static_cast<unsigned long long>(eye.completed),
                    static_cast<unsigned long long>(eye.checked),
                    static_cast<unsigned long long>(eye.black),
                    static_cast<unsigned long long>(eye.wrong_eye),
                    static_cast<unsigned long long>(eye.fence_out ? eye.fence_out->GetCompletedValue() : 0));
        if (eye.submitted != options.frames || eye.completed != options.frames ||
            eye.checked != options.frames || eye.black != 0 || eye.wrong_eye != 0 ||
            eye.fence_out == nullptr || eye.fence_out->GetCompletedValue() < options.frames)
            ok = false;
    }
    ok = ok && hosts_ok && logs_ok;

    // Hosts are gone and have drained their queues, so their duplicated resources are
    // no longer live.  It is now safe to release the two client-side sets.
    CleanupEye(eyes[0]);
    CleanupEye(eyes[1]);
    Release(context4);
    Release(device5);
    Release(context);
    Release(device);

    std::printf("RESULT: %s (%s, %u frames per eye, no async handoff)\n",
                ok ? "PASS" : "FAIL", options.sr ? "DLSS 4.5 SR" : "DLAA 4.5",
                options.frames);
    return ok ? 0 : 1;
}
