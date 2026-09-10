#pragma once

#include "core/hooks/pattern_scan.h"

namespace bvr::b2r::exit_guard {

// Fail-soft, BS2-only. Advance the engine's own empty-viewport exit branch
// before its unsafe Viewports[0] access, and clean up VR after any accepted
// native non-forced RequestExit. Does not catch faults in the engine.
bool install(const bvr::pattern_scan::ProcessImage& image);

// Only the verified BS2 main-window WM_CLOSE route uses this. Ask the engine
// to leave its loop while its viewports are still alive, allowing native
// shutdown to drain workers before destroying them. False means forward the
// original message unchanged. Menu/save/confirmation logic stays native; its
// accepted RequestExit is observed by the same terminal cleanup hook.
bool request_window_close();

} // namespace bvr::b2r::exit_guard
