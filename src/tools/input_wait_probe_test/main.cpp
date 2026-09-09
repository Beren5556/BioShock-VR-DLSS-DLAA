#include "core/gfx/input_wait_probe.h"
#include <cstdio>
#include <vector>

namespace P = bvr::input_wait_probe;
namespace {
unsigned checks = 0, failures = 0;
void expect(bool ok, const char* label) {
    ++checks; failures += !ok; std::printf("%s: %s\n", ok ? "PASS" : "FAIL", label);
}
struct Step { P::Wake wake; double elapsed; std::uint64_t input, output; };
struct Hooks {
    double time = 0;
    std::uint64_t input = 0, output = 0;
    bool inputRegistration = true, outputRegistration = true;
    double registrationCost = 0;
    unsigned inputRegistrations = 0, outputRegistrations = 0;
    std::vector<Step> steps;
    std::vector<std::uint32_t> budgets;
    std::vector<bool> watched;
    double now_ms() { return time; }
    std::uint64_t input_value() { return input; }
    std::uint64_t output_value() { return output; }
    bool register_input(std::uint64_t) { ++inputRegistrations; time += registrationCost; return inputRegistration; }
    bool register_output(std::uint64_t) { ++outputRegistrations; return outputRegistration; }
    P::Wake wait(bool watchInput, std::uint32_t budget) {
        budgets.push_back(budget); watched.push_back(watchInput);
        if (budgets.size() > steps.size()) return P::Wake::Failed;
        const auto& step = steps[budgets.size() - 1];
        if (step.elapsed > budget) { time += budget; return P::Wake::Timeout; }
        time += step.elapsed; input = step.input; output = step.output;
        return step.wake;
    }
};
bool near(double a, double b) { return std::abs(a-b) < 0.00001; }
}
int main() {
    expect(!P::sampled_frame(0) && !P::sampled_frame(15) && P::sampled_frame(16) && P::sampled_frame(32), "one sample every sixteen current frames");
    expect(P::state(P::kRemoved, 16) == P::State::Removed, "device removal is never fence completion");
    expect(P::remaining_ms(5, 4.4) == 1 && P::remaining_ms(5, 5) == 0 && P::remaining_ms(5, 6) == 0, "remaining deadline rounds once without resetting its budget");
    P::Observation o{};
    Hooks h; h.input = h.output = 16;
    expect(P::wait(h,16,5000,true,o) && !h.outputRegistrations && !h.inputRegistrations && o.outputAtEntry && o.splitObserved && !o.beforeInputMs, "already-complete output does not register or wait");
    h = {}; h.steps = {{P::Wake::Input,2,16,0},{P::Wake::Output,3,16,16}};
    expect(P::wait(h,16,5000,true,o) && o.splitObserved && near(o.beforeInputMs,2) && near(o.afterInputMs,3), "separate input wake divides the CPU-observed current-frame wait");
    expect(h.budgets.size()==2 && h.budgets[0]==5000 && h.budgets[1]==4998 && h.watched[0] && !h.watched[1], "input wake cannot renew the output timeout");
    h = {}; h.registrationCost=2; h.steps={{P::Wake::Input,1,16,0},{P::Wake::Output,1,16,16}};
    expect(P::wait(h,16,5,true,o) && h.budgets[0]==3 && h.budgets[1]==2 && near(o.totalMs,4), "extra diagnostic registration is inside the original deadline");
    h = {}; h.steps={{P::Wake::Input,4,16,0},{P::Wake::Output,2,16,16}};
    expect(!P::wait(h,16,5,true,o) && near(h.time,5) && !o.splitObserved, "late output times out at the original deadline");
    h = {}; h.steps={{P::Wake::Output,2,16,16}};
    expect(P::wait(h,16,5000,true,o) && o.coalesced && !o.splitObserved, "coalesced output-first wake never fabricates the missing input time");
    P::Counters counters; counters.add(o);
    expect(counters.sampled==1 && counters.coalesced==1 && counters.unobserved==1 && !counters.split, "unobserved/coalesced samples do not enter split averages");
    h = {}; h.inputRegistration=false; h.steps={{P::Wake::Output,2,16,16}};
    expect(P::wait(h,16,5000,true,o) && o.registrationFailed && !h.watched[0] && !o.splitObserved, "failed optional input registration cannot fail rendering");
    h = {}; h.steps={{P::Wake::Input,1,15,0},{P::Wake::Input,1,16,0},{P::Wake::Output,1,16,16}};
    expect(P::wait(h,16,5000,true,o) && o.staleInputWakes==1 && near(o.beforeInputMs,2), "a stale event needs the current fence value before attribution");
    h = {}; h.input=16; h.steps={{P::Wake::Output,2,16,16}};
    expect(P::wait(h,16,5000,true,o) && !h.inputRegistrations && o.splitObserved && !o.beforeInputMs && near(o.afterInputMs,2), "input-ready-at-entry separates remaining output wait without another wake");
    counters.add(o);
    expect(counters.frames==2 && counters.inputReady==1 && counters.inputPending==1 && counters.split==1 && near(counters.afterInputMs,2), "readiness and valid split denominators stay explicit");
    h = {}; h.input=P::kRemoved; h.steps={{P::Wake::Output,1,P::kRemoved,16}};
    expect(P::wait(h,16,5000,true,o) && o.inputAtEntry==P::State::Removed && !o.splitObserved && !h.inputRegistrations, "invalid diagnostic input does not alter a successful output contract");
    h = {}; h.steps={{P::Wake::Input,1,P::kRemoved,0},{P::Wake::Output,1,P::kRemoved,16}};
    expect(P::wait(h,16,5000,true,o) && o.inputRemovedDuringWait && !o.splitObserved, "input removal while observing is counted, not reported as zero time");
    h = {}; h.output=P::kRemoved;
    expect(!P::wait(h,16,5000,true,o) && !h.outputRegistrations, "removed output preserves failure without an unsatisfied GPU wait");
    h = {}; h.steps={{P::Wake::Process,1,0,0}};
    expect(!P::wait(h,16,5000,true,o) && !o.splitObserved, "helper exit remains a bounded failure");
    h = {}; h.steps={{P::Wake::Output,1,16,15}};
    expect(!P::wait(h,16,5000,true,o), "output event alone cannot publish an incomplete frame");
    h = {}; h.outputRegistration=false;
    expect(!P::wait(h,16,5000,true,o) && h.budgets.empty(), "output registration failure preserves existing failure contract");
    h = {}; h.steps={{P::Wake::Output,2,16,16}};
    expect(P::wait(h,16,5000,false,o) && !h.inputRegistrations && !o.sampled && !o.splitObserved && o.inputAtEntry==P::State::Pending, "unsampled frame adds snapshots but no extra completion event");
    counters.add(o);
    expect(counters.sampled==2 && counters.unsampled==1 && near(counters.unsampledTotalMs,2), "unsampled wait cost has its own count and sum for perturbation checks");
    std::printf("Input wait probe checks: %u/%u passed\n", checks-failures, checks);
    return failures ? 1 : 0;
}
