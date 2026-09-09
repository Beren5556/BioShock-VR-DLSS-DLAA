#pragma once
#include <array>
#include <cstddef>

namespace bvr::b1r::graphics_options {
struct Definition { const char* key; const char* labelEs; const char* labelEn; bool impact; bool defaultOn; };
inline constexpr Definition kOptions[] = {
    {"HighDetailShaders", "Shaders de alto detalle", "High-detail shaders", false, true},
    {"Shadows", "Sombras", "Shadows", true, true},
    {"RealTimeReflection", "Reflejos", "Reflections", true, false},
    {"PostProcessing", "Posprocesado", "Post-processing", false, true},
    {"UseRippleSystem", "Ondulaciones del agua", "Water ripples", true, false},
    {"UseHighDetailSoftParticles", "Particulas de alta calidad", "High-quality particles", false, true},
    {"UseDistortion", "Distorsion", "Distortion", false, true},
    {"UseHighDetailPostProcEffects", "Posprocesado de alta calidad", "High-quality post-processing", false, true},
    {"FluidSurfaceDetail", "Detalle de fluidos", "Fluid detail", false, true}
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
