#pragma once
#include <d3d11.h>

// Optional same-size, bounded post-DLSS sharpening. Not NGX's deprecated
// sharpness field, not an upscaler, and never used by NORMAL/DLAA.
namespace bvr::dlss_sharpen {
bool prepare(ID3D11Device* device, ID3D11Texture2D* left, ID3D11Texture2D* right);
bool ready();
bool render(ID3D11DeviceContext* context, int eye, ID3D11Texture2D* destination,
            unsigned percent);
void release();
}
