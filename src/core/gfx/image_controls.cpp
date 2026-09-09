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
bool g_english = false;

const char* tr(const char* spanish, const char* english) noexcept { return g_english ? english : spanish; }

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
const char* text(const char* spanish, const char* english) noexcept { return tr(spanish, english); }
bool graphics_panel_open() noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    return enabled() && g_initialized && g_panel == Panel::Graphics;
}

void initialize(Settings effective, const wchar_t* ini) noexcept {
    if (!enabled()) return;
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_initialized) return;
    if (ini) g_ini = ini;
    wchar_t language[8]{};
    GetPrivateProfileStringW(L"ui", L"language", L"es", language, 8, ini);
    g_english = _wcsicmp(language, L"en") == 0;
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
            g_graphicsMessage = tr("Comprobando cambio...", "Checking change...");
        } else g_graphicsSelection = graphics::move_selection(g_graphicsSelection, key == VK_F2 ? -1 : 1);
        return;
    }
    if (key == VK_F4 && kProbeAvailable) {
        if (!g_known || g_busy || g_pending || g_savePending) return;
        if (g_applied.probe == Probe::Off) g_beforeProbe = g_applied;
        Settings next;
        if (!next_probe(g_applied, g_beforeProbe, next)) return;
        g_requested = next; g_pending = g_transientRequest = true;
        g_panel = Panel::Mode; g_message = tr("Preparando prueba...", "Preparing test...");
        return;
    }
    if (key == VK_F1) {
        g_panel = g_applied.probe != Probe::Off
            ? (g_panel == Panel::Hidden ? Panel::Mode : Panel::Hidden)
            : next_panel(g_panel, g_applied.mode);
        if (g_panel == Panel::Graphics) { g_graphicsRefresh = true; g_graphicsMessage = tr("Leyendo opciones del motor...", "Reading engine options..."); }
        return;
    }
    if (key != VK_F2 && key != VK_F3) return;
    if (g_applied.probe != Probe::Off) return; // Keep the comparison geometry fixed.
    if (g_panel == Panel::Hidden) return;
    if (!g_known) return;
    if (g_busy || g_pending || g_savePending || g_graphicsBusy || g_graphicsToggle) { g_message = tr("Aplicando cambio...", "Applying change..."); return; }
    Settings next = g_applied;
    bool changed = false;
    const int direction = key == VK_F2 ? -1 : 1;
    if (g_panel == Panel::Mode) {
        changed = step_mode(next, direction);
        if (!changed) {
            g_message = tr("Resolucion demasiado baja para esta calidad DLSS", "Resolution too low for this DLSS quality");
            return;
        }
    } else if (g_panel == Panel::Resolution) {
        changed = step_resolution(next, direction);
    } else if (g_panel == Panel::Quality) {
        changed = step_quality(next, direction);
    } else if (g_panel == Panel::Sharpness) {
        changed = step_sharpness(next, direction);
    }
    if (changed) { g_requested = next; g_pending = true; g_transientRequest = false; g_message = tr("Cambio solicitado...", "Change requested..."); }
}

bool take_request(Settings& requested, Settings& previous) noexcept {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!enabled() || !g_pending || g_busy) return false;
    requested = g_requested; previous = g_applied;
    g_pending = false; g_busy = true; g_message = tr("Aplicando cambio...", "Applying change...");
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
    g_message = g_savePending ? tr("Aplicado; guardando...", "Applied; saving...") : "";
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
    g_message = reason ? reason : tr("No aplicado; se conserva el ajuste anterior", "Not applied; previous setting retained");
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
    g_message = reason ? reason : tr("VR no disponible; reinicia el juego", "VR unavailable; restart the game");
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
            g_graphicsMessage = result == graphics::Change::Applied ? tr("Guardado. Si el efecto no cambia, reinicia el juego.", "Saved. If the effect does not change, restart the game.") :
                result == graphics::Change::AppliedNotSaved ? tr("Aplicado al motor, pero no se pudo guardar.", "Applied to the engine, but could not be saved.") :
                result == graphics::Change::RestartRequired ? tr("No disponible en caliente: cambia esta opcion en el lanzador y reinicia.", "Not available live: change this option in the launcher and restart.") :
                tr("Cambio no confirmado; no se ha guardado. Revisa el valor o reinicia.", "Change not confirmed or saved. Check the value or restart.");
        } else {
            bool needsRestart = false;
            for (const auto& value : g_graphics) needsRestart |= !value.live;
            g_graphicsMessage = !readOk ? tr("No se pudieron leer las opciones.", "Could not read the options.") : needsRestart ?
                tr("[Reinicio]: no disponible en caliente; usar el lanzador.", "[Restart]: not available live; use the launcher.") :
                tr("F4 cambia solo la opcion seleccionada.", "F4 changes only the selected option.");
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
        g_message = ok ? "" : tr("Aplicado, pero no se pudo guardar para el siguiente inicio", "Applied, but could not be saved for the next launch");
    }
    BVR_LOG("[image-controls] save confirmed settings: %s", ok ? "OK" : "FAILED");
}
std::string panel_status() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!enabled() || !g_initialized || g_panel == Panel::Hidden) return {};
    if (!g_known) return std::string(tr("ESTADO VR NO DISPONIBLE\n", "VR STATUS UNAVAILABLE\n")) + g_message;
    if (g_panel == Panel::Graphics) {
        std::string panel = tr("OPCIONES GRAFICAS\nF2 anterior | F3 siguiente | F4 cambiar | F1 cerrar\n",
                               "GRAPHICS OPTIONS\nF2 previous | F3 next | F4 change | F1 close\n");
        for (size_t i = 0; i < graphics::kCount; ++i) {
            const auto& v = g_graphics[i];
            panel += i == g_graphicsSelection ? "> " : "  ";
            panel += g_english ? graphics::kOptions[i].labelEn : graphics::kOptions[i].labelEs;
            if (graphics::kOptions[i].impact) panel += " *";
            panel += ": ";
            panel += !v.known ? "?" : i == graphics::kCount - 1 ? (v.on ? tr("Alto", "High") : tr("Bajo", "Low")) : (v.on ? tr("Si", "Yes") : tr("No", "No"));
            panel += v.live ? tr("  [F4: cambiar]\n", "  [F4: change]\n") : tr("  [F4: cambiar / Reinicio]\n", "  [F4: change / Restart]\n");
        }
        panel += tr("* Alto impacto en el rendimiento\n", "* High performance impact\n");
        panel += g_graphicsMessage;
        return panel;
    }
    char text[768];
    const auto& s = g_applied;
    if (s.probe != Probe::Off) {
        _snprintf_s(text, sizeof(text), _TRUNCATE,
            tr("PRUEBA: %s\n%u x %u por ojo | Ajustes temporales\nF4 Siguiente / salir tras C | F1 Ocultar\nMisma vista 15 s: observa la latencia de VD\n%s",
               "TEST: %s\n%u x %u per eye | Temporary settings\nF4 Next / exit after C | F1 Hide\nSame view for 15 s: watch VD latency\n%s"),
            probe_name(s.probe), s.renderWidth, s.renderHeight, g_message.c_str());
        return text;
    }
    const double percent = s.outputWidth ? 100.0 * s.renderWidth / s.outputWidth : 100.0;
    const char* selection = g_panel == Panel::Quality ? tr("CALIDAD DLSS", "DLSS QUALITY") :
                            g_panel == Panel::Resolution ? tr("RESOLUCION POR OJO", "RESOLUTION PER EYE") :
                            g_panel == Panel::Sharpness ? tr("SHARPNESS DLSS", "DLSS SHARPNESS") : tr("MODO DE RENDERIZADO", "RENDERING MODE");
    char value[192];
    if (g_panel == Panel::Mode) _snprintf_s(value, sizeof(value), _TRUNCATE, "%s", name(s.mode));
    else if (g_panel == Panel::Resolution) _snprintf_s(value, sizeof(value), _TRUNCATE,
        tr("%u x %u por ojo", "%u x %u per eye"), s.outputWidth, s.outputHeight);
    else if (g_panel == Panel::Quality) _snprintf_s(value, sizeof(value), _TRUNCATE,
        "%.1f%%  |  Render: %u x %u", 100.0 * s.srScale.numerator / s.srScale.denominator,
        s.renderWidth, s.renderHeight);
    else _snprintf_s(value, sizeof(value), _TRUNCATE, "%u%%", s.sharpnessPercent);
    _snprintf_s(text, sizeof(text), _TRUNCATE,
                tr("%s  |  %s\n%s\nSalida: %u x %u  |  Render: %u x %u (%.1f%%)\nF1 Siguiente / ocultar   F2 -   F3 +%s%s",
                   "%s  |  %s\n%s\nOutput: %u x %u  |  Render: %u x %u (%.1f%%)\nF1 Next / hide   F2 -   F3 +%s%s"),
                g_spatialFallback ? tr("RESPALDO (DLSS inactivo)", "FALLBACK (DLSS inactive)") : name(s.mode),
                selection, value, s.outputWidth, s.outputHeight,
                s.renderWidth, s.renderHeight, percent,
                g_message.empty() ? "" : "\n", g_message.c_str());
    return text;
}
} // namespace bvr::image_controls
