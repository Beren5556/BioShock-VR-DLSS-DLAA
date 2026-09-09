#pragma once

#ifndef XR_USE_PLATFORM_WIN32
#define XR_USE_PLATFORM_WIN32
#endif
#ifndef XR_USE_GRAPHICS_API_D3D11
#define XR_USE_GRAPHICS_API_D3D11
#endif
#include <windows.h>
#include <d3d11.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <string>

namespace bvr::imagehud {

// Present-thread use only. The caller must retire GPU work before destroying
// swapchains and call destroy() before destroying the associated XR session.
void destroy() noexcept;

// Independent, head-locked status layer for both eyes; never enters scene
// color, depth or motion-vector inputs. Empty UTF-8 text hides the layer.
// The caller owns the compositor layer-budget check.
const XrCompositionLayerBaseHeader* layer(
    XrSession session, XrSpace viewSpace, ID3D11Device* device,
    ID3D11DeviceContext* context, const std::string& status) noexcept;

} // namespace bvr::imagehud
