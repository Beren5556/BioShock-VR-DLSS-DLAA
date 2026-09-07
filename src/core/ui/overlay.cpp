#include "overlay.h"

#include "core/framework/framework.h"
#include "core/gfx/frame_inspector.h"
#include "core/input/xinput_bridge.h"
#include "core/util/crash.h"
#include "core/util/log.h"
#include "core/vr/openxr_runtime.h"
#include "game/igame_adapter.h"

#include <windows.h>
#include <d3d11.h>

#include <imgui.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>

#include <atomic>

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND, UINT, WPARAM, LPARAM);

namespace bvr::overlay {
namespace {

constexpr float kOverlayUiScale = 3.52f;
constexpr float kOverlayWindowSize = 1802.24f;

bool g_initialized = false;
bool g_visible = false;
std::atomic<int> g_visibleRequest{-1}; // session 22: seam toggle (-1 = none)
ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_context = nullptr;
ID3D11RenderTargetView* g_rtv = nullptr;
ID3D11Texture2D* g_rtvBackbuffer = nullptr; // identity only, never deref'd
std::atomic<unsigned> g_renderTargetWidth{0};
std::atomic<unsigned> g_renderTargetHeight{0};
HWND g_window = nullptr;
WNDPROC g_originalWndProc = nullptr;

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wparam, LPARAM lparam) {
    // Session 38: the subclass is on the GAME's main window, so it is the
    // earliest game-agnostic sight of a close. BS2's engine faults on its own
    // exit path (hook-free-proven); noting teardown here turns that into a
    // quiet fast exit instead of a dump per close. WM_ENDSESSION covers
    // logoff/shutdown. Always forwarded - observation only.
    if (msg == WM_CLOSE || msg == WM_DESTROY || (msg == WM_ENDSESSION && wparam))
        crash::note_teardown(msg == WM_CLOSE     ? "WM_CLOSE"
                             : msg == WM_DESTROY ? "WM_DESTROY"
                                                 : "WM_ENDSESSION");
    if (g_visible) {
        // ImGui's Win32 backend reports mouse positions in client-window
        // coordinates, while the overlay is rendered into the (much larger)
        // VR backbuffer. Convert mouse movement to that coordinate space too.
        LPARAM imguiLparam = lparam;
        if (msg == WM_MOUSEMOVE) {
            RECT clientRect{};
            const unsigned targetWidth =
                g_renderTargetWidth.load(std::memory_order_relaxed);
            const unsigned targetHeight =
                g_renderTargetHeight.load(std::memory_order_relaxed);
            if (targetWidth != 0 && targetHeight != 0 &&
                GetClientRect(hwnd, &clientRect)) {
                const int clientWidth = clientRect.right - clientRect.left;
                const int clientHeight = clientRect.bottom - clientRect.top;
                if (clientWidth > 0 && clientHeight > 0) {
                    const int mouseX = static_cast<short>(LOWORD(lparam));
                    const int mouseY = static_cast<short>(HIWORD(lparam));
                    const int scaledX = MulDiv(
                        mouseX, static_cast<int>(targetWidth), clientWidth);
                    const int scaledY = MulDiv(
                        mouseY, static_cast<int>(targetHeight), clientHeight);
                    imguiLparam = MAKELPARAM(static_cast<WORD>(scaledX),
                                             static_cast<WORD>(scaledY));
                }
            }
        }
        ImGui_ImplWin32_WndProcHandler(hwnd, msg, wparam, imguiLparam);
        // Session 22 (user report: overlay unusable while scrolling): while
        // ImGui owns the mouse/keyboard, CONSUME those messages instead of
        // letting the game fight the overlay for them (the wheel doubled as
        // weapon-cycle, clicks re-captured the cursor mid-drag).
        ImGuiIO& io = ImGui::GetIO();
        bool mouseMsg = msg >= WM_MOUSEFIRST && msg <= WM_MOUSELAST;
        bool keyMsg = msg >= WM_KEYFIRST && msg <= WM_KEYLAST;
        if ((mouseMsg && io.WantCaptureMouse) || (keyMsg && io.WantCaptureKeyboard))
            return TRUE;
    }
    return CallWindowProcW(g_originalWndProc, hwnd, msg, wparam, lparam);
}

bool CreateRenderTarget(IDXGISwapChain* swapchain) {
    ID3D11Texture2D* backbuffer = nullptr;
    if (FAILED(swapchain->GetBuffer(0, IID_PPV_ARGS(&backbuffer))))
        return false;
    D3D11_TEXTURE2D_DESC backbufferDesc{};
    backbuffer->GetDesc(&backbufferDesc);
    HRESULT hr = g_device->CreateRenderTargetView(backbuffer, nullptr, &g_rtv);
    if (SUCCEEDED(hr)) {
        g_rtvBackbuffer = backbuffer;
        g_renderTargetWidth.store(backbufferDesc.Width,
                                  std::memory_order_relaxed);
        g_renderTargetHeight.store(backbufferDesc.Height,
                                   std::memory_order_relaxed);
        BVR_LOG("overlay render target: %ux%u", backbufferDesc.Width,
                backbufferDesc.Height);
    } else {
        g_rtvBackbuffer = nullptr;
    }
    backbuffer->Release();
    return SUCCEEDED(hr);
}

bool Init(IDXGISwapChain* swapchain) {
    if (FAILED(swapchain->GetDevice(IID_PPV_ARGS(&g_device))))
        return false;
    g_device->GetImmediateContext(&g_context);

    DXGI_SWAP_CHAIN_DESC desc{};
    swapchain->GetDesc(&desc);
    g_window = desc.OutputWindow;

    if (!CreateRenderTarget(swapchain)) return false;

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr; // don't scatter imgui.ini into the game folder

    // The stock 420 px overlay is extremely small on the high-resolution VR
    // target used by this installation. Rasterize the default font at the
    // enlarged size (instead of stretching its texture) and scale the widget
    // spacing to keep the panel readable in-headset.
    ImFontConfig fontConfig{};
    fontConfig.SizePixels = 13.0f * kOverlayUiScale;
    io.Fonts->AddFontDefault(&fontConfig);
    ImGui::StyleColorsDark();
    ImGui::GetStyle().ScaleAllSizes(kOverlayUiScale);
    ImGui_ImplWin32_Init(g_window);
    ImGui_ImplDX11_Init(g_device, g_context);

    g_originalWndProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(
        g_window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WndProc)));

    BVR_LOG("overlay initialized (hwnd=%p) - F10 toggles it", g_window);
    return true;
}

void DrawUi() {
    const ImVec2 displaySize = ImGui::GetIO().DisplaySize;
    // DisplaySize is explicitly matched to the real VR backbuffer below, so
    // this is the actual texture centre rather than the desktop-window centre.
    ImGui::SetNextWindowPos(ImVec2(displaySize.x * 0.5f, displaySize.y * 0.5f),
                            ImGuiCond_Always, ImVec2(0.5f, 0.5f));
    ImGui::SetNextWindowSize(ImVec2(kOverlayWindowSize, kOverlayWindowSize),
                             ImGuiCond_FirstUseEver);
    // The title identifies this as the DLSS add-on and names its upstream base;
    // it deliberately does not present itself as an official upstream release.
    ImGui::Begin(BVR_IDENTITY);
    ImGui::Text("%.1f fps (%.2f ms)", ImGui::GetIO().Framerate,
                1000.0f / ImGui::GetIO().Framerate);
    ImGui::Separator();
    vr::draw_debug_ui();
    ImGui::Separator();
    input::draw_debug_ui();
    if (auto* adapter = game::adapter()) {
        ImGui::Separator();
        adapter->drawDebugUi();
    }
    ImGui::Separator();
    frame_inspector::draw_debug_ui();
    ImGui::Separator();
    ImGui::TextWrapped("Log: %%LOCALAPPDATA%%\\BioshockVR\\bioshockvr.log");
    ImGui::End();
}

} // namespace

void on_present(IDXGISwapChain* swapchain) {
    if (!g_initialized) {
        g_initialized = Init(swapchain);
        if (!g_initialized) return;
    }
    // Session 22 (user report: overlay gone until an alt-tab): if the game
    // swaps its backbuffer object WITHOUT a ResizeBuffers (fullscreen-state
    // churn), a held RTV keeps drawing into the dead buffer - F10 toggles an
    // overlay nobody can see. Track the buffer identity and re-create.
    {
        ID3D11Texture2D* bb = nullptr;
        if (SUCCEEDED(swapchain->GetBuffer(0, IID_PPV_ARGS(&bb)))) {
            if (g_rtv && bb != g_rtvBackbuffer) {
                g_rtv->Release();
                g_rtv = nullptr;
                BVR_LOG("overlay: backbuffer identity changed - RTV re-created");
            }
            bb->Release();
        }
    }
    if (!g_rtv && !CreateRenderTarget(swapchain)) return;

    // F10, edge-triggered (Insert was the original choice, but not every
    // keyboard has it); the seam request lane covers harness toggling.
    static bool wasDown = false;
    bool isDown = (GetAsyncKeyState(VK_F10) & 0x8000) != 0;
    if (isDown && !wasDown) {
        g_visible = !g_visible;
        ImGui::GetIO().MouseDrawCursor = g_visible;
    }
    wasDown = isDown;
    int req = g_visibleRequest.exchange(-1, std::memory_order_relaxed);
    if (req >= 0) {
        g_visible = req != 0;
        ImGui::GetIO().MouseDrawCursor = g_visible;
    }

    if (!g_visible) return;

    ImGui_ImplDX11_NewFrame();
    ImGui_ImplWin32_NewFrame();
    // The Win32 backend sets DisplaySize from the logical desktop client area
    // (1536x864 here), but the overlay is drawn into the square VR backbuffer.
    // Use the render target's real dimensions for viewport, projection and UI
    // placement so 50%/50% is genuinely centred in the headset image.
    ImGuiIO& io = ImGui::GetIO();
    const unsigned targetWidth =
        g_renderTargetWidth.load(std::memory_order_relaxed);
    const unsigned targetHeight =
        g_renderTargetHeight.load(std::memory_order_relaxed);
    if (targetWidth != 0 && targetHeight != 0) {
        io.DisplaySize = ImVec2(static_cast<float>(targetWidth),
                              static_cast<float>(targetHeight));
    }
    ImGui::NewFrame();
    DrawUi();
    ImGui::Render();
    g_context->OMSetRenderTargets(1, &g_rtv, nullptr);
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
}

void on_resize() {
    if (g_rtv) {
        g_rtv->Release();
        g_rtv = nullptr;
    }
    g_rtvBackbuffer = nullptr;
    g_renderTargetWidth.store(0, std::memory_order_relaxed);
    g_renderTargetHeight.store(0, std::memory_order_relaxed);
}

void set_visible(bool on) {
    g_visibleRequest.store(on ? 1 : 0, std::memory_order_relaxed);
}

bool visible() {
    return g_visible;
}

} // namespace bvr::overlay
