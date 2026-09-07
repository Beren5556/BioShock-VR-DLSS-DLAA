#pragma once

#include <d3d11.h>

// Experimental, deliberately vendor-neutral spatial upscaler for the OpenXR
// eye feed.  It owns its intermediate textures so it never has to create a
// render-target view over an image supplied by an OpenXR runtime.
//
// This is not DLSS/DLAA: it has no motion vectors, depth or frame history.
// The first pass is a linear upscale with an optional clamped 5-tap sharpen.

namespace bvr::spatial_upscaler {

// Build the reusable D3D11 resources. sourceFormat must belong to the
// R8G8B8A8 family and outputViewFormat must be R8G8B8A8_UNORM[_SRGB].
// Calling prepare again replaces the previous resources.
bool prepare(ID3D11Device* device, UINT sourceWidth, UINT sourceHeight,
             DXGI_FORMAT sourceFormat, UINT outputWidth, UINT outputHeight,
             DXGI_FORMAT outputViewFormat);

// Copy source into the shader-readable input, scale/filter it into the owned
// output texture, then CopyResource that result into the acquired OpenXR
// image. The immediate-context state touched by the pass is restored.
bool render(ID3D11DeviceContext* context, ID3D11Texture2D* destination,
            ID3D11Texture2D* source, float sharpness);

bool ready();
void release();

} // namespace bvr::spatial_upscaler
