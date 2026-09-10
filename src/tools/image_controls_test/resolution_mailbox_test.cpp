#include "game/shared/resolution_mailbox.h"
#include <cstdio>
int main() {
    bvr::game::ResolutionMailbox queue;
    using State = bvr::game::ResolutionStatus;
    int checks = 0, failures = 0;
    auto check = [&](bool ok, const char* name) { ++checks; if (!ok) ++failures;
        std::printf("%s: %s\n", ok ? "PASS" : "FAIL", name); };
    check(queue.status() == State::Unavailable, "idle is not confirmation");
    check(!queue.offer(0, 2048) && !queue.offer(9000, 9000) && !queue.offer(1025, 1024), "reject invalid geometry");
    check(queue.offer(2048, 2048), "offer");
    check(!queue.offer(4096, 4096), "one pending request, never overwrite");
    check(queue.cancel() && queue.take() == 0, "cancel before dispatch");
    check(queue.offer(4096, 3072), "new request after cancellation");
    check(queue.take() == ((uint64_t(4096) << 32) | 3072), "exact geometry survives queue");
    check(!queue.cancel() && !queue.offer(1024, 1024), "in-flight dispatch cannot be cancelled or replaced");
    check(queue.status() == State::Pending, "taken is still pending, not success");
    queue.complete(true);
    check(queue.status() == State::Dispatched, "dispatch requires DXGI confirmation by caller");
    check(queue.offer(2048, 2048), "new request after completion");
    queue.take(); queue.complete(false);
    check(queue.status() == State::Unavailable, "failed dispatch is not success");
    check(!queue.cancel() && queue.take() == 0, "failure leaves no queued resize");
    std::printf("%d checks, %d failures\n", checks, failures);
    return failures ? 1 : 0;
}
