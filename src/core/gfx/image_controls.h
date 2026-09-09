#pragma once
#include "core/gfx/image_control_policy.h"
#include <string>

namespace bvr::image_controls {
void set_enabled(bool on) noexcept; // Only the BioShock 1 adapter opts in.
bool enabled() noexcept;
bool graphics_panel_open() noexcept;
void on_key(unsigned virtualKey) noexcept;
std::string panel_status();
void game_tick() noexcept; // Saves confirmed settings on the game thread.
// Initialize once from the EFFECTIVE path, not merely a requested ini mode.
void initialize(Settings effective, const wchar_t* dlssIni) noexcept;
bool take_request(Settings& requested, Settings& previous) noexcept;
void confirm(const Settings& effective) noexcept;
void reject(const char* reason) noexcept;
void report_effective(const Settings& effective, const char* reason,
                      bool spatialFallback = false) noexcept;
void unavailable(const char* reason) noexcept;
} // namespace bvr::image_controls
