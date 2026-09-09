#include "core/gfx/image_controls.h"
#include "core/util/log.h"
#include "game/bioshock1r/game_ini.h"
#include "game/bioshock1r/graphics_options.h"
#include <windows.h>
#include <atomic>
#include <mutex>
#include <cstdio>

namespace bvr::image_controls {
namespace {
std::atomic<bool> g_enabled{false};
std::mutex g_mutex;
Settings g_applied{}, g_requested{}, g_save{};
Settings g_beforeProbe{};
bool g_transientRequest = false;
Panel g_panel = Panel::Hidden;
bool g_initialized = false, g_pending = false, g_busy = false, g_savePending = false;
bool g_known = true, g_spatialFallback = false;
std::wstring g_ini;
std::string g_message;
namespace graphics = bvr::b1r::graphics_options;
graphics::Values g_graphics{};
size_t g_graphicsSelection = 0, g_graphicsToggleIndex = 0;
bool g_graphicsRefresh = false, g_graphicsToggle = false, g_graphicsBusy = false;
std::string g_graphicsMessage;

const char* name(RenderMode mode) noexcept {
    return mode == RenderMode::Dlss ? "DLSS" : mode == RenderMode::Dlaa ? "DLAA" : "NORMAL";
}
bool persist(const Settings& s, const std::wstring& ini) {
    if (ini.empty() || s.probe != Probe::Off) return false;
    // Stage dlss.ini next to the original: preserve unrelated keys. No visible
    // partial mode/size pair, and no saves for unconfirmed rendering requests.
    const std::wstring staged = ini + L".bvr-controls.tmp";
    if (!CopyFileW(ini.c_str(), staged.c_str(), FALSE) &&
        GetLastError() != ERROR_FILE_NOT_FOUND) return false;
    auto value = [&](const wchar_t* key, const wchar_t* val) {
        return WritePrivateProfileStringW(L"dlss", key, val, staged.c_str()) != FALSE;
    };
    auto number = [&](const wchar_t* key, uint32_t n) {
        wchar_t text[32]; swprintf_s(text, L"%u", n); return value(key, text);
    };
    bool ok = value(L"mode", s.mode == RenderMode::Dlss ? L"sr" :
                                 s.mode == RenderMode::Dlaa ? L"dlaa" : L"off") &&
              value(L"runtime", L"310.7.0") && value(L"quality", L"auto") &&
              value(L"preset", L"auto") && number(L"outputWidth", s.outputWidth) &&
              number(L"outputHeight", s.outputHeight) &&
              number(L"srScaleNumerator", s.srScale.numerator) &&
              number(L"srScaleDenominator", s.srScale.denominator) &&
              number(L"sharpnessPercent", s.sharpnessPercent);
    WritePrivateProfileStringW(nullptr, nullptr, nullptr, staged.c_str());
    if (!ok) { DeleteFileW(staged.c_str()); return false; }
    const auto oldViewport = bvr::b1r::game_ini::read_viewport();
    const bool viewportChanged = !oldViewport.valid ||
        oldViewport.windowedW != s.renderWidth || oldViewport.windowedH != s.renderHeight ||
        oldViewport.fullscreenW != s.renderWidth || oldViewport.fullscreenH != s.renderHeight;
    if (viewportChanged && !bvr::b1r::game_ini::write_viewport(s.renderWidth, s.renderHeight)) {
        DeleteFileW(staged.c_str()); return false;
    }
    ok = MoveFileExW(staged.c_str(), ini.c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    if (!ok) {
        // Best-effort rollback of the four viewport keys to the previous pair.
        if (viewportChanged && oldViewport.valid && oldViewport.windowedW == oldViewport.fullscreenW &&
            oldViewport.windowedH == oldViewport.fullscreenH)
            bvr::b1r::game_ini::write_viewport(oldViewport.windowedW, oldViewport.windowedH);
        DeleteFileW(staged.c_str());
    }
    return ok;
}
}

void set_enabled(bool on) noexcept { g_enabled.store(on, std::memory_order_release); }
bool enabled() noexcept { return g_enabled.load(std::memory_order_acquire); }
bool graphics_panel_open() noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    return enabled() && g_initialized && g_panel == Panel::Graphics;
}

void initialize(Settings effective, const wchar_t* ini) noexcept {
    if (!enabled()) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_initialized) return;
    if (ini) g_ini = ini;
    Fraction saved{GetPrivateProfileIntW(L"dlss", L"srScaleNumerator", 0, ini),
                   GetPrivateProfileIntW(L"dlss", L"srScaleDenominator", 0, ini)};
    if (valid_scale(saved)) effective.srScale = saved;
    else if (effective.mode == RenderMode::Dlss && effective.outputWidth) {
        // Recover canonical fractions before treating a legacy rounded ratio
        // as custom. 2730/4096 is the standard 2/3 profile, not a new quality.
        effective.srScale = {effective.renderWidth, effective.outputWidth};
        auto recover = [&](const auto& steps) {
            for (auto fraction : steps) {
                Settings candidate = effective; candidate.srScale = fraction;
                if (geometry(candidate) && candidate.renderWidth == effective.renderWidth &&
                    candidate.renderHeight == effective.renderHeight) {
                    effective.srScale = fraction; return true;
                }
            }
            return false;
        };
        if (!recover(kQualitySteps)) recover(kLegacyQualitySteps);
    }
    g_applied = g_requested = effective;
    g_initialized = true;
}

void on_key(unsigned key) noexcept {
    if (!enabled()) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_initialized) return;
    if (g_panel == Panel::Graphics && (key == VK_F2 || key == VK_F3 || key == VK_F4)) {
        if (g_busy || g_pending || g_savePending || g_graphicsBusy || g_graphicsToggle) return;
        if (key == VK_F4) {
            if (!g_known) return;
            g_graphicsToggleIndex = g_graphicsSelection;
            g_graphicsToggle = true;
            g_graphicsMessage = "Comprobando cambio...";
        } else g_graphicsSelection = graphics::move_selection(g_graphicsSelection, key == VK_F2 ? -1 : 1);
        return;
    }
    if (key == VK_F4 && kProbeAvailable) {
        if (!g_known || g_busy || g_pending || g_savePending) return;
        if (g_applied.probe == Probe::Off) g_beforeProbe = g_applied;
        Settings next;
        if (!next_probe(g_applied, g_beforeProbe, next)) return;
        g_requested = next; g_pending = g_transientRequest = true;
        g_panel = Panel::Mode; g_message = "Preparando prueba...";
        return;
    }
    if (key == VK_F1) {
        g_panel = g_applied.probe != Probe::Off
            ? (g_panel == Panel::Hidden ? Panel::Mode : Panel::Hidden)
            : next_panel(g_panel, g_applied.mode);
        if (g_panel == Panel::Graphics) { g_graphicsRefresh = true; g_graphicsMessage = "Leyendo opciones del motor..."; }
        return;
    }
    if (key != VK_F2 && key != VK_F3) return;
    if (g_applied.probe != Probe::Off) return; // Keep the comparison geometry fixed.
    if (g_panel == Panel::Hidden) return;
    if (!g_known) return;
    if (g_busy || g_pending || g_savePending || g_graphicsBusy || g_graphicsToggle) { g_message = "Aplicando cambio..."; return; }
    Settings next = g_applied;
    bool changed = false;
    const int direction = key == VK_F2 ? -1 : 1;
    if (g_panel == Panel::Mode) {
        changed = step_mode(next, direction);
        if (!changed) {
            g_message = "Resolucion demasiado baja para esta calidad DLSS";
            return;
        }
    } else if (g_panel == Panel::Resolution) {
        changed = step_resolution(next, direction);
    } else if (g_panel == Panel::Quality) {
        changed = step_quality(next, direction);
    } else if (g_panel == Panel::Sharpness) {
        changed = step_sharpness(next, direction);
    }
    if (changed) { g_requested = next; g_pending = true; g_transientRequest = false; g_message = "Cambio solicitado..."; }
}

bool take_request(Settings& requested, Settings& previous) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!enabled() || !g_pending || g_busy) return false;
    requested = g_requested; previous = g_applied;
    g_pending = false; g_busy = true; g_message = "Aplicando cambio...";
    return true;
}
void confirm(const Settings& effective) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_applied = g_requested = g_save = effective;
    g_known = true; g_spatialFallback = false;
    if ((g_panel == Panel::Quality || g_panel == Panel::Sharpness) && effective.mode != RenderMode::Dlss)
        g_panel = Panel::Resolution;
    g_pending = g_busy = false;
    g_savePending = !g_transientRequest && effective.probe == Probe::Off;
    g_message = g_savePending ? "Aplicado; guardando..." : "";
    if (g_transientRequest) {
        BVR_LOG("[perf-probe] APPLIED %s render=%ux%u output=%ux%u; session-only, no INI writes",
                probe_name(effective.probe), effective.renderWidth, effective.renderHeight,
                effective.outputWidth, effective.outputHeight);
    }
    g_transientRequest = false;
    BVR_LOG("[image-controls] APPLIED %s %ux%u -> %ux%u, SR preference=%u/%u",
            name(effective.mode), effective.renderWidth, effective.renderHeight,
            effective.outputWidth, effective.outputHeight,
            effective.srScale.numerator, effective.srScale.denominator);
}
void reject(const char* reason) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_requested = g_applied; g_pending = g_busy = g_transientRequest = false;
    g_message = reason ? reason : "No aplicado; se conserva el ajuste anterior";
    BVR_LOG("[image-controls] rejected: %s", g_message.c_str());
}
void report_effective(const Settings& effective, const char* reason, bool spatialFallback) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    Settings observed = effective;
    observed.srScale = g_applied.srScale;
    observed.sharpnessPercent = g_applied.sharpnessPercent;
    const bool changed = !same(observed, g_applied) || !g_known ||
                         spatialFallback != g_spatialFallback;
    if (!changed && !reason) return;
    g_applied = g_requested = observed; g_pending = g_busy = false;
    g_known = true; g_spatialFallback = spatialFallback;
    if (changed) g_savePending = false; // Do not persist an obsolete confirmed size.
    if ((g_panel == Panel::Quality || g_panel == Panel::Sharpness) && effective.mode != RenderMode::Dlss)
        g_panel = Panel::Resolution;
    g_message = reason ? reason : "";
}
void unavailable(const char* reason) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_known = false; g_pending = g_busy = g_savePending = false;
    g_message = reason ? reason : "VR no disponible; reinicia el juego";
}
namespace {
void graphics_tick() {
    bool refresh, toggle;
    size_t index;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (!enabled() || !g_initialized || !g_known || g_busy || g_pending || g_savePending || g_graphicsBusy) return;
        refresh = g_graphicsRefresh;
        toggle = g_graphicsToggle;
        if (!refresh && !toggle) return;
        index = g_graphicsToggleIndex;
        g_graphicsRefresh = g_graphicsToggle = false;
        g_graphicsBusy = true;
    }
    graphics::Values values{};
    graphics::Value observed{};
    graphics::Change result = graphics::Change::Failed;
    bool readOk = false;
    try {
        if (refresh) { values = graphics::read(); readOk = true; }
        if (toggle) result = graphics::toggle(index, observed);
    } catch (...) { result = graphics::Change::Failed; }
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (readOk) g_graphics = values;
        if (toggle) {
            if (result != graphics::Change::RestartRequired) g_graphics[index] = observed;
            else g_graphics[index].live = false;
            g_graphicsMessage = result == graphics::Change::Applied ? "Guardado. Si el efecto no cambia, reinicia el juego." :
                result == graphics::Change::AppliedNotSaved ? "Aplicado al motor, pero no se pudo guardar." :
                result == graphics::Change::RestartRequired ? "No disponible en caliente: cambia esta opcion en el lanzador y reinicia." :
                "Cambio no confirmado; no se ha guardado. Revisa el valor o reinicia.";
        } else {
            bool needsRestart = false;
            for (const auto& value : g_graphics) needsRestart |= !value.live;
            g_graphicsMessage = !readOk ? "No se pudieron leer las opciones." : needsRestart ?
                "[Reinicio]: no disponible en caliente; usar el lanzador." :
                "F4 cambia solo la opcion seleccionada.";
        }
        g_graphicsBusy = false;
    }
}
}
void game_tick() noexcept {
    graphics_tick();
    Settings saved; std::wstring ini;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (!g_savePending) return;
        saved = g_save; ini = g_ini;
    }
    bool ok = false;
    try { ok = persist(saved, ini); } catch (...) {}
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_savePending = false;
        g_message = ok ? "" : "Aplicado, pero no se pudo guardar para el siguiente inicio";
    }
    BVR_LOG("[image-controls] save confirmed settings: %s", ok ? "OK" : "FAILED");
}
std::string panel_status() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!enabled() || !g_initialized || g_panel == Panel::Hidden) return {};
    if (!g_known) return std::string("ESTADO VR NO DISPONIBLE\n") + g_message;
    if (g_panel == Panel::Graphics) {
        std::string panel = "OPCIONES GRAFICAS\nF2 anterior | F3 siguiente | F4 cambiar | F1 cerrar\n";
        for (size_t i = 0; i < graphics::kCount; ++i) {
            const auto& v = g_graphics[i];
            panel += i == g_graphicsSelection ? "> " : "  ";
            panel += graphics::kOptions[i].label;
            if (graphics::kOptions[i].impact) panel += " *";
            panel += ": ";
            panel += !v.known ? "?" : i == graphics::kCount - 1 ? (v.on ? "Alto" : "Bajo") : (v.on ? "Si" : "No");
            panel += v.live ? "  [F4: cambiar]\n" : "  [F4: cambiar / Reinicio]\n";
        }
        panel += "* Alto impacto en el rendimiento\n";
        panel += g_graphicsMessage;
        return panel;
    }
    char text[768];
    const auto& s = g_applied;
    if (s.probe != Probe::Off) {
        _snprintf_s(text, sizeof(text), _TRUNCATE,
            "PRUEBA: %s\n%u x %u por ojo | Ajustes temporales\n"
            "F4 Siguiente / salir tras C | F1 Ocultar\n"
            "Misma vista 15 s: observa la latencia de VD\n%s",
            probe_name(s.probe), s.renderWidth, s.renderHeight, g_message.c_str());
        return text;
    }
    const double percent = s.outputWidth ? 100.0 * s.renderWidth / s.outputWidth : 100.0;
    const char* selection = g_panel == Panel::Quality ? "CALIDAD DLSS" :
                            g_panel == Panel::Resolution ? "RESOLUCION POR OJO" :
                            g_panel == Panel::Sharpness ? "SHARPNESS DLSS" : "MODO DE RENDERIZADO";
    char value[192];
    if (g_panel == Panel::Mode) _snprintf_s(value, sizeof(value), _TRUNCATE, "%s", name(s.mode));
    else if (g_panel == Panel::Resolution) _snprintf_s(value, sizeof(value), _TRUNCATE,
        "%u x %u por ojo", s.outputWidth, s.outputHeight);
    else if (g_panel == Panel::Quality) _snprintf_s(value, sizeof(value), _TRUNCATE,
        "%.1f%%  |  Render: %u x %u", 100.0 * s.srScale.numerator / s.srScale.denominator,
        s.renderWidth, s.renderHeight);
    else _snprintf_s(value, sizeof(value), _TRUNCATE, "%u%%", s.sharpnessPercent);
    _snprintf_s(text, sizeof(text), _TRUNCATE,
                "%s  |  %s\n%s\nSalida: %u x %u  |  Render: %u x %u (%.1f%%)\n"
                "F1 Siguiente / ocultar   F2 -   F3 +%s%s",
                g_spatialFallback ? "RESPALDO (DLSS inactivo)" : name(s.mode),
                selection, value, s.outputWidth, s.outputHeight,
                s.renderWidth, s.renderHeight, percent,
                g_message.empty() ? "" : "\n", g_message.c_str());
    return text;
}
} // namespace bvr::image_controls
