#pragma once
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>

namespace cane {
enum class TrackBlend { replace, additive };
struct TrackOptions {
    std::optional<float> blend_weight, playback_rate, event_blend_threshold, attachment_blend_threshold, draw_order_blend_threshold;
    std::optional<bool> looping, hold_previous_pose;
    std::optional<TrackBlend> blend;
};
struct QueuedEntryOptions {
    std::optional<float> start_delay_seconds, blend_duration_seconds;
    std::optional<bool> looping;
};
struct TrackState {
    std::uint32_t track_index = 0;
    std::optional<std::string> animation_id;
    float playback_time = 0, animation_time = 0, animation_range_start = 0, animation_range_end = 0, start_delay_seconds = 0;
    std::optional<float> playback_end;
    float blend_weight = 1, playback_rate = 1;
    bool looping = false, hold_previous_pose = false;
    TrackBlend blend = TrackBlend::replace;
    float blend_elapsed = 0, blend_duration = 0, blend_progress = 1;
    float event_blend_threshold = 0, attachment_blend_threshold = 0, draw_order_blend_threshold = 0;
    std::size_t queued_entry_count = 0;
};
struct QueuedEntryState {
    std::uint32_t track_index = 0;
    std::size_t queue_index = 0;
    std::optional<std::string> animation_id;
    float start_delay_seconds = 0, blend_duration = 0;
    bool looping = false;
};
}
