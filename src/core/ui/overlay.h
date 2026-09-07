#pragma once

struct IDXGISwapChain;

namespace bvr::overlay {

// ImGui debug/config overlay drawn from the Present hook. Toggled with F10.
// Initializes itself lazily on the first Present (that's when the game's
// device and window are known).
void on_present(IDXGISwapChain* swapchain);
void on_resize(); // drop backbuffer references before ResizeBuffers

// Session 22: programmatic visibility (any thread; applied on the next
// present). The `vroverlay on|off` seam command - the harness cannot press
// F10, and it doubles as the user's recovery if a keyboard state wedges.
void set_visible(bool on);

// Present-thread snapshot used by temporal render paths. The overlay is drawn
// between on_present_begin and on_present_end, so feeding it into DLSS would
// give UI pixels no matching depth or motion vectors.
bool visible();

} // namespace bvr::overlay
