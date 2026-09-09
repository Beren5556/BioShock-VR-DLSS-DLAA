// Offscreen only: exercise the production rasterizer. Never creates an XR session.
#include "core/vr/image_status_quad.cpp"
#include "game/bioshock1r/graphics_options.h"
#include <cstdio>
namespace bvr::log { void write(const char*, ...) {} }
int wmain(int argc, wchar_t** argv) {
    namespace hud = bvr::imagehud;
    std::string text = "OPCIONES GRAFICAS\nF2 anterior | F3 siguiente | F4 cambiar | F1 cerrar\n";
    for (const auto& option : bvr::b1r::graphics_options::kOptions) {
        text += "  "; text += option.labelEs;
        if (option.impact) text += " *";
        text += ": Alto  [F4: cambiar / Reinicio]\n";
    }
    text += "* Alto impacto en el rendimiento\nNo disponible en caliente: cambia esta opcion en el lanzador y reinicia.";
    std::vector<uint32_t> pixels;
    if (!hud::rasterize(text, pixels) || pixels.size() != hud::kPixelCount) {
        std::printf("FAIL: HUD rasterization (pixels=%zu, expected=%zu)\n", pixels.size(), hud::kPixelCount); return 1;
    }
    hud::BitmapText measure;
    measure.dc = CreateCompatibleDC(nullptr);
    measure.font = CreateFontW(-24, 0, 0, 0, FW_MEDIUM, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    if (!measure.dc || !measure.font) return 2;
    measure.oldFont = SelectObject(measure.dc, measure.font);
    const std::wstring wide(text.begin(), text.end());
    RECT bounds{24, 18, LONG(hud::kWidth - 24), LONG(hud::kHeight - 18)};
    const int height = DrawTextW(measure.dc, wide.c_str(), int(wide.size()), &bounds,
        DT_LEFT | DT_WORDBREAK | DT_NOPREFIX | DT_CALCRECT);
    if (!height || bounds.bottom > LONG(hud::kHeight - 18) || bounds.right > LONG(hud::kWidth - 24)) {
        std::printf("FAIL: text needs height=%d, bottom=%ld/%u, right=%ld/%u\n",
            height, bounds.bottom, hud::kHeight - 18, bounds.right, hud::kWidth - 24); return 1;
    }
    std::printf("PASS: nine rows, per-row F4 and longest restart message fit: %d px of %u px. No VR/game opened.\n",
        height, hud::kHeight - 36);
    if (argc == 2) {
        // BMP artifact is generated from the actual HUD pixels, not a desktop capture.
        BITMAPFILEHEADER file{}; BITMAPINFOHEADER info{};
        file.bfType = 0x4d42; file.bfOffBits = sizeof(file) + sizeof(info);
        file.bfSize = file.bfOffBits + DWORD(pixels.size() * sizeof(uint32_t));
        info.biSize = sizeof(info); info.biWidth = hud::kWidth; info.biHeight = -LONG(hud::kHeight);
        info.biPlanes = 1; info.biBitCount = 32; info.biCompression = BI_RGB;
        for (auto& rgba : pixels) rgba = (rgba & 0xff00ff00u) | ((rgba & 0xffu) << 16) | ((rgba >> 16) & 0xffu);
        HANDLE out = CreateFileW(argv[1], GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
        if (out == INVALID_HANDLE_VALUE) return 2;
        DWORD written = 0;
        bool ok = WriteFile(out, &file, sizeof(file), &written, nullptr) && written == sizeof(file);
        ok = ok && WriteFile(out, &info, sizeof(info), &written, nullptr) && written == sizeof(info);
        ok = ok && WriteFile(out, pixels.data(), DWORD(pixels.size() * 4), &written, nullptr) && written == pixels.size() * 4;
        CloseHandle(out); if (!ok) return 2;
    }
    return 0;
}
