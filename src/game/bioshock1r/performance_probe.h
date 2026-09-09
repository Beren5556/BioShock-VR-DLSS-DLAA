#pragma once
#include "core/gfx/image_control_policy.h"
#include <d3d11.h>

namespace bvr::b1r::performance_probe {
#ifdef BVR_PERFORMANCE_PROBE
void configure(ID3D11Device*, const image_controls::Settings&);
void shutdown();
void scene_begin(int eye, uint64_t build);
void scene_end();
void build_end(int eye, uint64_t build);
void capture_begin(int eye, uint64_t build);
void guides_end();
void capture_end(int outcome);
#else
inline void configure(ID3D11Device*, const image_controls::Settings&) {}
inline void shutdown() {}
inline void scene_begin(int, uint64_t) {}
inline void scene_end() {}
inline void build_end(int, uint64_t) {}
inline void capture_begin(int, uint64_t) {}
inline void guides_end() {}
inline void capture_end(int) {}
#endif
}
