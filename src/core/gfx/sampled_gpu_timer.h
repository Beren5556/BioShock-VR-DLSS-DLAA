#pragma once
#include <d3d11.h>
#include <cstdint>
#include <initializer_list>

namespace bvr {
// The DLL-only scene probe owns one enclosing disjoint query on its render
// thread. Suspend the older isolated samplers there; never nest them inside it.
inline thread_local bool diagnostic_timeline_owns_queries = false;
// Diagnostic timestamps only. Never flushes, spins or waits for query data.
// One sampled operation per stride; a busy ring drops the sample, not a frame.
class SampledGpuTimer {
    struct Slot { ID3D11Query *disjoint=nullptr, *start=nullptr, *end=nullptr; bool pending=false; } slots_[4];
    int active_ = -1;
    uint64_t calls_ = 0, stride_ = 64, samples_ = 0;
    double totalMs_ = 0, maxMs_ = 0;
public:
    SampledGpuTimer() = default;
    SampledGpuTimer(const SampledGpuTimer&) = delete;
    SampledGpuTimer& operator=(const SampledGpuTimer&) = delete;
    ~SampledGpuTimer() { release(); }
    void release() {
        for (auto& s : slots_) {
            for (auto* p : {s.disjoint, s.start, s.end}) if (p) p->Release();
            s = {};
        }
        active_ = -1; calls_ = samples_ = 0; totalMs_ = maxMs_ = 0;
    }
    void prepare(ID3D11Device* device, uint64_t stride = 64) {
        release(); stride_ = stride ? stride : 1;
        if (!device) return;
        for (auto& s : slots_) {
            D3D11_QUERY_DESC d{D3D11_QUERY_TIMESTAMP_DISJOINT, 0};
            if (FAILED(device->CreateQuery(&d, &s.disjoint))) { release(); return; }
            d.Query = D3D11_QUERY_TIMESTAMP;
            if (FAILED(device->CreateQuery(&d, &s.start)) || FAILED(device->CreateQuery(&d, &s.end))) {
                release(); return;
            }
        }
    }
    void poll(ID3D11DeviceContext* context) {
        for (auto& s : slots_) {
            if (!s.pending) continue;
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT d{}; UINT64 start=0, end=0;
            const UINT flag = D3D11_ASYNC_GETDATA_DONOTFLUSH;
            HRESULT hr = context->GetData(s.disjoint, &d, sizeof(d), flag);
            if (hr == S_FALSE) continue;
            if (FAILED(hr)) { s.pending = false; continue; }
            if (d.Disjoint || !d.Frequency) { s.pending = false; continue; }
            if (context->GetData(s.start, &start, sizeof(start), flag) != S_OK ||
                context->GetData(s.end, &end, sizeof(end), flag) != S_OK) continue;
            s.pending = false;
            if (end < start) continue;
            const double ms = 1000.0 * double(end-start) / double(d.Frequency);
            totalMs_ += ms; if (ms > maxMs_) maxMs_ = ms; ++samples_;
        }
    }
    void begin(ID3D11DeviceContext* context) {
        if (diagnostic_timeline_owns_queries) return;
        if (!context || active_ >= 0 || !slots_[0].start || (++calls_ % stride_)) return;
        poll(context);
        for (int i=0; i<4; ++i) if (!slots_[i].pending) {
            active_ = i;
            context->Begin(slots_[i].disjoint); context->End(slots_[i].start);
            return;
        }
    }
    void end(ID3D11DeviceContext* context) {
        if (active_ < 0) return;
        auto& s = slots_[active_];
        context->End(s.end); context->End(s.disjoint);
        s.pending = true; active_ = -1;
    }
    uint64_t samples() const { return samples_; }
    double average_ms() const { return samples_ ? totalMs_ / double(samples_) : 0; }
    double max_ms() const { return maxMs_; }
    void clear_stats() { samples_ = 0; totalMs_ = maxMs_ = 0; }
};
inline double diagnostic_clock_ms() {
    static const double scale = [] { LARGE_INTEGER f{}; QueryPerformanceFrequency(&f); return 1000.0 / double(f.QuadPart); }();
    LARGE_INTEGER t{}; QueryPerformanceCounter(&t); return double(t.QuadPart) * scale;
}
}
