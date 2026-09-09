#pragma once
#include <cstdint>

// Opt-in use of existing IPC v8 fields. An old helper's half-image transport
// test must never be mistaken for this full-image stereo timing comparison.
namespace bvr_latency_probe {
constexpr std::int32_t kFullImageTransport = 2;
constexpr std::uint32_t kFullImageAck = 4u;
inline bool valid_copy(std::uint32_t width, std::uint32_t height,
                       std::uint32_t outputWidth, std::uint32_t outputHeight,
                       std::uint32_t colorFormat, std::uint32_t outputFormat) {
    return width && height && width == outputWidth && height == outputHeight &&
           colorFormat && colorFormat == outputFormat;
}
inline bool matching_ack(bool transport, std::uint32_t flags) {
    return transport == ((flags & kFullImageAck) != 0);
}
}
