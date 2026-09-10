#include "core/util/game_exit_gate.h"

#include <cstdio>
#include <thread>

namespace {
int failures = 0;
void check(bool value, const char* label) {
    std::printf("%s %s\n", value ? "PASS" : "FAIL", label);
    if (!value) ++failures;
}
}

int main() {
    using namespace bvr::game_exit;
    {
        Gate value;
        {
            Scope first(value, true);
            Scope nested(value, true);
            check(bool(first) && bool(nested), "nested Present/Resize is reentrant");
            bool otherEntered = true;
            std::thread other([&] { Scope competing(value, true); otherEntered = bool(competing); });
            other.join();
            check(!otherEntered, "another render callback does not block or enter active scope");
        }
        ShutdownScope exit(value, 50);
        check(bool(exit), "released scopes permit terminal shutdown");
        Scope latePresent(value, true);
        Scope lateInit(value, true, true);
        check(!latePresent && !lateInit, "terminal gate rejects late callbacks and initialization");
        Scope unrelatedHost(value, false);
        check(bool(unrelatedHost), "non-BS2 host bypasses terminal gate unchanged");
    }
    {
        Gate value;
        {
            Scope present(value, true);
            ShutdownScope invalidExit(value, 50);
            check(!invalidExit && value.terminal(), "shutdown inside active callback fails and remains terminal");
        }
        ShutdownScope retry(value, 50);
        check(bool(retry), "failed reentrant shutdown can retry only after callback leaves");
    }
    {
        Gate value;
        HANDLE waiterStarted = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!waiterStarted) return 2;
        bool waiterEntered = true;
        std::thread waiter;
        {
            Scope active(value, true);
            waiter = std::thread([&] {
                SetEvent(waiterStarted);
                Scope initializing(value, true, true);
                waiterEntered = bool(initializing);
            });
            WaitForSingleObject(waiterStarted, 1000);
            Sleep(10);
            ShutdownScope invalidHere(value, 0); // seals even though this scope owns the lock
            check(!invalidHere, "terminal latch can precede a queued initializer acquiring the lock");
        }
        waiter.join();
        CloseHandle(waiterStarted);
        check(!waiterEntered, "queued initializer rechecks terminal after acquiring the lock");
    }
    {
        Gate value;
        HANDLE entered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        HANDLE release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!entered || !release) return 2;
        std::thread worker([&] {
            Scope init(value, true, true);
            SetEvent(entered);
            WaitForSingleObject(release, 3000);
        });
        check(WaitForSingleObject(entered, 1000) == WAIT_OBJECT_0, "initialization worker holds gate");
        const ULONGLONG start = GetTickCount64();
        {
            ShutdownScope timedOut(value, 35);
            check(!timedOut, "busy initialization is not reported as completed shutdown");
        }
        check(GetTickCount64() - start < 500, "busy shutdown wait is bounded");
        SetEvent(release);
        worker.join();
        CloseHandle(entered);
        CloseHandle(release);
        {
            ShutdownScope retry(value, 50);
            check(bool(retry), "terminal cleanup may retry after initialization drains");
        }
        Scope resurrection(value, true, true);
        check(!resurrection, "successful retry cannot resurrect VR");
    }
    {
        Gate value;
        HANDLE entered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!entered) return 2;
        std::thread worker([&] {
            Scope present(value, true);
            SetEvent(entered);
            Sleep(25);
        });
        WaitForSingleObject(entered, 1000);
        {
            ShutdownScope drained(value, 1000);
            check(bool(drained), "shutdown waits for in-flight callback to fully finish");
        }
        worker.join();
        CloseHandle(entered);
    }
    std::printf("game_exit_gate_test: %s\n", failures ? "FAIL" : "PASS");
    return failures ? 1 : 0;
}
