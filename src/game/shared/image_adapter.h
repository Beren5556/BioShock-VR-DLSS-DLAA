#pragma once
#include "game/bioshock1r/game_ini.h"
#include "game/bioshock1r/graphics_options.h"
#include "game/shared/resolution_mailbox.h"
#include <cstdint>
#include <string>

namespace bvr::active_image {
using ResolutionRequestStatus = bvr::game::ResolutionStatus;
bool enqueue_resolution(uint32_t width, uint32_t height);
ResolutionRequestStatus resolution_request_status();
bool cancel_pending_resolution();
// Common geometry only. BS2 translates its governing Shared.ini pair here.
using Viewport = bvr::b1r::game_ini::Viewport;
Viewport read_viewport();
bool write_viewport(uint32_t width, uint32_t height);
bool save_configuration(uint32_t width, uint32_t height, const std::wstring& ini,
                        const std::wstring& staged, const std::string& before, bool existed);
namespace graphics {
using bvr::b1r::graphics_options::Definition;
using bvr::b1r::graphics_options::Value;
using bvr::b1r::graphics_options::Values;
using bvr::b1r::graphics_options::Change;
using bvr::b1r::graphics_options::kOptions;
using bvr::b1r::graphics_options::kCount;
using bvr::b1r::graphics_options::move_selection;
Values read();
Change toggle(size_t index, Value& observed);
}
}
