#pragma once
#include <array>
#include <cstddef>

namespace bvr::b1r::graphics_options {
struct Definition { const char* key; const char* label; bool impact; bool defaultOn; };
inline constexpr Definition kOptions[] = {
    {"HighDetailShaders", "High-detail shaders", false, true},
    {"Shadows", "Shadows", true, true},
    {"RealTimeReflection", "Reflections", true, false},
    {"PostProcessing", "Post-processing", false, true},
    {"UseRippleSystem", "Water ripples", true, false},
    {"UseHighDetailSoftParticles", "High-quality particles", false, true},
    {"UseDistortion", "Distortion", false, true},
    {"UseHighDetailPostProcEffects", "High-quality post-processing", false, true},
    {"FluidSurfaceDetail", "Fluid detail", false, true}
};
inline constexpr size_t kCount = sizeof(kOptions) / sizeof(kOptions[0]);
struct Value { bool on = false; bool known = false; bool live = false; };
using Values = std::array<Value, kCount>;
enum class Change { Applied, AppliedNotSaved, RestartRequired, Failed };
inline size_t move_selection(size_t index, int direction) noexcept {
    return (index + (direction < 0 ? kCount - 1 : 1)) % kCount;
}
// Only called from the game-thread menu queue, never from a rendering hook.
Values read();
Change toggle(size_t index, Value& observed);
} // namespace bvr::b1r::graphics_options
