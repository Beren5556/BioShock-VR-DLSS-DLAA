#pragma once

// Pure validation shared by the real BS2 projection hook and its tests.
// The audited builder writes row-major finite D3D perspective, with w=z.
#include <cmath>
#include <cstdint>

namespace bvr::b2r::temporal_contract {

inline int32_t rotation_delta(int32_t current, int32_t previous) {
    const uint32_t wrapped = (static_cast<uint32_t>(current) -
                              static_cast<uint32_t>(previous)) & 0xffffu;
    return wrapped >= 32768u ? static_cast<int32_t>(wrapped) - 65536
                             : static_cast<int32_t>(wrapped);
}

inline bool same_pose(const float* locationA, const int32_t* rotationA,
                       const float* locationB, const int32_t* rotationB) {
    if (!locationA || !locationB || !rotationA || !rotationB) return false;
    for (int axis = 0; axis < 3; ++axis) {
        if (!std::isfinite(locationA[axis]) || !std::isfinite(locationB[axis]) ||
            std::fabs(locationA[axis] - locationB[axis]) > 0.01f ||
            ((static_cast<uint32_t>(rotationA[axis]) -
               static_cast<uint32_t>(rotationB[axis])) & 0xffffu) != 0)
            return false;
    }
    return true;
}
inline bool finite_projection(const float* matrix, float nearPlane, float farPlane,
                              float* tanX, float* tanY) {
    if (!matrix || !tanX || !tanY || !std::isfinite(nearPlane) ||
        !std::isfinite(farPlane) || nearPlane <= 0 || farPlane <= nearPlane)
        return false;
    for (int i = 0; i < 16; ++i)
        if (!std::isfinite(matrix[i])) return false;
    if (matrix[0] <= 0 || matrix[5] <= 0) return false;
    constexpr int zeros[] = {1, 2, 3, 4, 6, 7, 8, 9, 12, 13, 15};
    for (int i : zeros)
        if (std::fabs(matrix[i]) > 0.00001f) return false;
    const float a = farPlane / (farPlane - nearPlane);
    const float b = -nearPlane * a;
    if (std::fabs(matrix[11] - 1.0f) > 0.00001f ||
        std::fabs(matrix[10] - a) > 0.00001f ||
        std::fabs(matrix[14] - b) > std::fmax(0.0001f, std::fabs(b) * 0.00001f))
        return false;
    *tanX = 1.0f / matrix[0];
    *tanY = 1.0f / matrix[5];
    return std::isfinite(*tanX) && std::isfinite(*tanY);
}
} // namespace bvr::b2r::temporal_contract
