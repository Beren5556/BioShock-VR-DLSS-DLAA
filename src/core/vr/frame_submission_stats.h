#pragma once
#include <cstdint>
#include <cmath>

namespace bvr::submission_probe {
// CPU-side observation only. Predicted display time is NOT the compositor's
// submission deadline, and none of these fields proves GPU completion or MTP.
struct Sample {
    bool valid = false, predictionValid = false;
    double afterWaitMs = 0, endCallMs = 0, periodMs = 0;
    double waitLeadMs = 0, submitLeadMs = 0;
};
inline Sample sample(std::int64_t waitReturn, std::int64_t submitStart,
                     std::int64_t submitEnd, std::int64_t predicted,
                     std::int64_t frequency, std::int64_t periodNs, bool converted) {
    Sample s;
    if (frequency <= 0 || waitReturn <= 0 || submitStart < waitReturn || submitEnd < submitStart)
        return s;
    const double ms = 1000.0 / double(frequency);
    s.valid = true; s.predictionValid = converted;
    s.afterWaitMs = double(submitStart-waitReturn)*ms;
    s.endCallMs = double(submitEnd-submitStart)*ms;
    s.periodMs = periodNs > 0 ? double(periodNs)/1e6 : 0;
    if (converted) {
        s.waitLeadMs = (double(predicted)-double(waitReturn))*ms;
        s.submitLeadMs = (double(predicted)-double(submitStart))*ms;
    }
    return s;
}
struct Window {
    std::uint64_t count = 0, leadSamples = 0, pastPrediction = 0;
    double afterWait = 0, endCall = 0, period = 0, waitLead = 0, submitLead = 0, minLead = 0;
    void add(const Sample& s) {
        if (!s.valid) return;
        ++count; afterWait += s.afterWaitMs; endCall += s.endCallMs; period += s.periodMs;
        if (s.predictionValid) {
            if (!leadSamples || s.submitLeadMs < minLead) minLead = s.submitLeadMs;
            ++leadSamples; waitLead += s.waitLeadMs; submitLead += s.submitLeadMs;
            if (s.submitLeadMs < 0) ++pastPrediction;
        }
    }
};
}
