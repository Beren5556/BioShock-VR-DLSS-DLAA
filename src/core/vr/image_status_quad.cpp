#include "image_status_quad.h"

#include "core/util/log.h"

#include <algorithm>
#include <cstdint>
#include <vector>

#ifdef _MSC_VER
#pragma comment(lib, "gdi32.lib")
#endif

namespace bvr::imagehud {
namespace {

constexpr uint32_t kWidth = 1024;
constexpr uint32_t kCompactHeight = 256;
constexpr uint32_t kHeight = 480; // All nine graphics rows; compact pages keep their prior size.
constexpr size_t kPixelCount = static_cast<size_t>(kWidth) * kHeight;
constexpr int64_t kFormat = DXGI_FORMAT_R8G8B8A8_UNORM;

struct State {
    XrSession session = XR_NULL_HANDLE;
    XrSwapchain swapchain = XR_NULL_HANDLE;
    ID3D11Device* device = nullptr; // borrowed, identity check only
    std::vector<XrSwapchainImageD3D11KHR> images;
    std::vector<uint32_t> pixels;
    std::string text;
    uint64_t revision = 0;
    uint64_t publishedRevision = 0;
    uint32_t acquiredIndex = 0;
    bool acquired = false;
    bool failed = false;
    bool loggedLive = false;
    XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
};

State g_state;

// A private memory DC/DIB, not a window or desktop capture. All GDI objects
// are deselected before deletion, including partial-initialization failures.
struct BitmapText {
    HDC dc = nullptr;
    HBITMAP bitmap = nullptr;
    HFONT font = nullptr;
    HGDIOBJ oldBitmap = nullptr;
    HGDIOBJ oldFont = nullptr;
    void* bits = nullptr;

    ~BitmapText() {
        if (dc && oldFont && oldFont != HGDI_ERROR) SelectObject(dc, oldFont);
        if (dc && oldBitmap && oldBitmap != HGDI_ERROR) SelectObject(dc, oldBitmap);
        if (font) DeleteObject(font);
        if (bitmap) DeleteObject(bitmap);
        if (dc) DeleteDC(dc);
    }
    BitmapText() = default;
    BitmapText(const BitmapText&) = delete;
    BitmapText& operator=(const BitmapText&) = delete;
};

bool rasterize(const std::string& text, std::vector<uint32_t>& pixels) {
    const int byteCount = static_cast<int>(text.size());
    const int charCount = MultiByteToWideChar(CP_UTF8, 0, text.data(), byteCount,
                                             nullptr, 0);
    if (charCount <= 0) return false;
    std::wstring wide(static_cast<size_t>(charCount), L'\0');
    if (MultiByteToWideChar(CP_UTF8, 0, text.data(), byteCount,
                            wide.data(), charCount) != charCount)
        return false;

    BitmapText target;
    target.dc = CreateCompatibleDC(nullptr);
    if (!target.dc) return false;
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = static_cast<LONG>(kWidth);
    info.bmiHeader.biHeight = -static_cast<LONG>(kHeight);
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    target.bitmap = CreateDIBSection(target.dc, &info, DIB_RGB_COLORS,
                                      &target.bits, nullptr, 0);
    if (!target.bitmap || !target.bits) return false;
    target.oldBitmap = SelectObject(target.dc, target.bitmap);
    if (!target.oldBitmap || target.oldBitmap == HGDI_ERROR) return false;
    target.font = CreateFontW(-24, 0, 0, 0, FW_MEDIUM, FALSE, FALSE, FALSE,
                               DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
                               CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
                               DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    if (!target.font) return false;
    target.oldFont = SelectObject(target.dc, target.font);
    if (!target.oldFont || target.oldFont == HGDI_ERROR) return false;

    auto* dib = static_cast<uint32_t*>(target.bits);
    std::fill_n(dib, kPixelCount, uint32_t{0x0020252b});
    if (!SetBkMode(target.dc, TRANSPARENT)) return false;
    if (SetTextColor(target.dc, RGB(245, 245, 245)) == CLR_INVALID) return false;
    RECT bounds{24, 18, static_cast<LONG>(kWidth - 24),
                         static_cast<LONG>(kHeight - 18)};
    if (!DrawTextW(target.dc, wide.data(), charCount, &bounds,
                    DT_LEFT | DT_WORDBREAK | DT_NOPREFIX | DT_END_ELLIPSIS))
        return false;
    if (!GdiFlush()) return false;

    // DIB pixels are B,G,R,unused; the compositor expects R,G,B,A.
    // Grayscale antialiasing avoids ClearType color fringes in the headset.
    pixels.resize(kPixelCount);
    for (size_t i = 0; i < kPixelCount; ++i) {
        const uint32_t bgr = dib[i];
        pixels[i] = 0xff000000u | (bgr & 0x0000ff00u) |
                    ((bgr & 0x000000ffu) << 16) |
                    ((bgr & 0x00ff0000u) >> 16);
    }
    return true;
}

void fail(const char* operation, XrResult result = XR_ERROR_RUNTIME_FAILURE) noexcept {
    BVR_LOG("[image-hud] disabled: %s (%d)", operation, static_cast<int>(result));
    // An upload may already be queued when release/runtime failure is seen.
    // Keep the swapchain owned until the caller's GPU-retired destroy point;
    // error handling must not destroy a texture that the GPU may still use.
    g_state.failed = true; // retry only after a session/resource reset
}

bool create(XrSession session, ID3D11Device* device) {
    g_state.session = session;
    g_state.device = device;
    uint32_t count = 0;
    XrResult result = xrEnumerateSwapchainFormats(session, 0, &count, nullptr);
    if (result != XR_SUCCESS || count == 0 || count > 256) {
        fail("enumerate formats", result);
        return false;
    }
    std::vector<int64_t> formats(count);
    result = xrEnumerateSwapchainFormats(session, count, &count, formats.data());
    if (result != XR_SUCCESS || count > formats.size()) {
        fail("enumerate format values", result);
        return false;
    }
    formats.resize(count);
    if (std::find(formats.begin(), formats.end(), kFormat) == formats.end()) {
        fail("RGBA8 format unavailable");
        return false;
    }
    XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
    info.usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT |
                      XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT |
                      XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
    info.format = kFormat;
    info.sampleCount = 1;
    info.width = kWidth;
    info.height = kHeight;
    info.faceCount = 1;
    info.arraySize = 1;
    info.mipCount = 1;
    result = xrCreateSwapchain(session, &info, &g_state.swapchain);
    if (result != XR_SUCCESS) {
        fail("create swapchain", result);
        return false;
    }
    result = xrEnumerateSwapchainImages(g_state.swapchain, 0, &count, nullptr);
    if (result != XR_SUCCESS || count == 0 || count > 32) {
        fail("enumerate image count", result);
        return false;
    }
    g_state.images.assign(count, {XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
    result = xrEnumerateSwapchainImages(
        g_state.swapchain, count, &count,
        reinterpret_cast<XrSwapchainImageBaseHeader*>(g_state.images.data()));
    if (result != XR_SUCCESS || count == 0 || count > g_state.images.size()) {
        fail("enumerate images", result);
        return false;
    }
    g_state.images.resize(count);
    for (const auto& image : g_state.images) {
        if (!image.texture) {
            fail("null image texture");
            return false;
        }
    }
    BVR_LOG("[image-hud] quad ready (%ux%u, %u images)", kWidth, kHeight, count);
    return true;
}

} // namespace

void destroy() noexcept {
    if (g_state.swapchain != XR_NULL_HANDLE) {
        const XrResult result = xrDestroySwapchain(g_state.swapchain);
        if (XR_FAILED(result))
            BVR_LOG("[image-hud] swapchain destroy failed (%d)",
                    static_cast<int>(result));
    }
    // Image textures are owned by OpenXR and must not be Released separately.
    g_state = State{};
}

const XrCompositionLayerBaseHeader* layer(
    XrSession session, XrSpace viewSpace, ID3D11Device* device,
    ID3D11DeviceContext* context, const std::string& status) noexcept {
    if (session == XR_NULL_HANDLE || viewSpace == XR_NULL_HANDLE ||
        !device || !context || status.empty())
        return nullptr;
    if (g_state.session != session || g_state.device != device) destroy();
    if (g_state.failed) return nullptr;
    try {
        if (g_state.swapchain == XR_NULL_HANDLE && !create(session, device))
            return nullptr;
        const size_t length = (std::min)(status.size(), size_t{4096});
        if (g_state.text.size() != length ||
            g_state.text.compare(0, length, status, 0, length) != 0) {
            const std::string nextText(status, 0, length);
            if (!rasterize(nextText, g_state.pixels)) {
                fail("rasterize text");
                return nullptr;
            }
            g_state.text = nextText;
            if (++g_state.revision == 0) {
                g_state.revision = 1;
                g_state.publishedRevision = 0;
            }
        }
        // OpenXR retains the last released image. Stable text needs no GPU
        // upload or image acquire/wait cycle on subsequent game frames.
        if (g_state.publishedRevision != g_state.revision) {
            if (!g_state.acquired) {
                XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
                const XrResult result = xrAcquireSwapchainImage(
                    g_state.swapchain, &acquire, &g_state.acquiredIndex);
                if (result != XR_SUCCESS) {
                    fail("acquire image", result);
                    return nullptr;
                }
                g_state.acquired = true;
            }
            // A pending image is retained until a successful wait. Never
            // block gameplay or release an image whose wait timed out.
            XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};
            wait.timeout = 0;
            const XrResult waitResult = xrWaitSwapchainImage(g_state.swapchain, &wait);
            if (waitResult == XR_TIMEOUT_EXPIRED) return nullptr;
            if (waitResult != XR_SUCCESS) {
                fail("wait image", waitResult);
                return nullptr;
            }
            if (g_state.acquiredIndex >= g_state.images.size()) {
                fail("image index out of range");
                return nullptr;
            }
            context->UpdateSubresource(g_state.images[g_state.acquiredIndex].texture,
                                       0, nullptr, g_state.pixels.data(),
                                       kWidth * sizeof(uint32_t), 0);
            XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
            const XrResult releaseResult = xrReleaseSwapchainImage(
                g_state.swapchain, &release);
            if (releaseResult != XR_SUCCESS) {
                fail("release image", releaseResult);
                return nullptr;
            }
            g_state.acquired = false;
            g_state.publishedRevision = g_state.revision;
        }
        auto& quad = g_state.quad;
        quad = {XR_TYPE_COMPOSITION_LAYER_QUAD};
        quad.space = viewSpace;
        quad.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
        quad.subImage.swapchain = g_state.swapchain;
        const uint32_t visibleHeight = status.rfind("GRAPHICS OPTIONS\n", 0) == 0 ? kHeight : kCompactHeight;
        quad.subImage.imageRect = {{0, 0}, {static_cast<int32_t>(kWidth),
                                          static_cast<int32_t>(visibleHeight)}};
        quad.pose.orientation.w = 1.0f;
        // Upper view, slightly left of center: keep the full panel readable in VR.
        // Keep the proven font, quad dimensions, depth and binocular visibility.
        // Lower the existing panel by 8 cm. The larger page grows downward,
        // without moving its header, changing text scale, depth or stereo.
        quad.pose.position = {-0.10f, 0.30f - 0.45f * (visibleHeight - kCompactHeight) / kWidth, -1.2f};
        quad.size = {0.9f, 0.9f * static_cast<float>(visibleHeight) /
                                   static_cast<float>(kWidth)};
        if (!g_state.loggedLive) {
            g_state.loggedLive = true;
            BVR_LOG("[image-hud] layer live (head-locked, both eyes, x=-0.10, compact y=0.30)");
        }
        return reinterpret_cast<const XrCompositionLayerBaseHeader*>(&quad);
    } catch (...) {
        fail("allocation or text preparation");
        return nullptr;
    }
}

} // namespace bvr::imagehud
