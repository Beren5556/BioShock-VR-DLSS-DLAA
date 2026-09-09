// Engine console-command execution without the (dead) Tab console: the seam
// calls the engine's own Exec-chain entries directly with a stub
// FOutputDevice. `exec <cmd>` enters at UWindowsViewport::Exec, `execc <cmd>`
// at UWindowsClient::Exec - two lanes so chain-forwarding gaps can be probed
// per link. Game thread only (called from the command seam inside the
// CalcView detour), SEH-guarded, fail-soft.

#pragma once

#include <cstdint>
#include <string>

namespace bvr::b1r::console_exec {

// args = the raw command text after the seam verb.
void run_viewport(const char* args);
void run_client(const char* args);
void run_engine(const char* args); // UGameEngine::Exec - forwards to script

// Allowlisted RenderConfig settings only. GET captures the engine's own value;
// SET alone is not proof of application. Calls run exclusively on the game thread.
bool read_render_option(const char* key, std::wstring& value);
bool set_render_option(const char* key, const char* value);

enum class ResolutionDispatch { Dispatched, Unavailable, Fault };

// Dedicated, validated SETRES lane. Game thread only; the caller must first
// close the XR pair and retire size-dependent resources. This does not alter
// the generic Exec seam or its failure latch, accepts no command text and
// never persists the INI. Dispatched means HANDLED, NOT a confirmed resize.
ResolutionDispatch set_viewport_resolution(uint32_t width, uint32_t height);

} // namespace bvr::b1r::console_exec
