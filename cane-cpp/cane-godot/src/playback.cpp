#include "skeleton.hpp"
#include "script_values.hpp"
#include <godot_cpp/core/class_db.hpp>
#include <cmath>

namespace cane_godot {
namespace {
std::optional<float> optional_mix(double seconds) {
    if (seconds == -1) return {};
    const auto value = scalar(seconds, "mix_seconds");
    require(value >= 0, "mix_seconds must be -1 or non-negative.", "mix_seconds");
    return value;
}
std::size_t queue_index(std::int64_t value) {
    require(value >= 0 && static_cast<std::uint64_t>(value) <= std::numeric_limits<std::size_t>::max(),
        "Queue index is out of range.", "queue_index");
    return static_cast<std::size_t>(value);
}
cane::TrackOptions track_options(const godot::Dictionary& options) {
    cane::TrackOptions result;
    const auto keys = options.keys();
    for (std::int64_t i = 0; i < keys.size(); ++i) {
        const auto key = option_key(keys[i]); const auto value = options[keys[i]];
        if (key == "blend_weight") result.blend_weight = number(value, key.c_str());
        else if (key == "playback_rate") result.playback_rate = number(value, key.c_str());
        else if (key == "event_blend_threshold") result.event_blend_threshold = number(value, key.c_str());
        else if (key == "attachment_blend_threshold") result.attachment_blend_threshold = number(value, key.c_str());
        else if (key == "draw_order_blend_threshold") result.draw_order_blend_threshold = number(value, key.c_str());
        else if (key == "looping") result.looping = boolean(value, key.c_str());
        else if (key == "hold_previous_pose") result.hold_previous_pose = boolean(value, key.c_str());
        else if (key == "blend") {
            require(value.get_type() == godot::Variant::STRING || value.get_type() == godot::Variant::STRING_NAME,
                "blend must be replace or additive.", "blend");
            const auto blend = text(static_cast<godot::String>(value));
            require(blend == "replace" || blend == "additive", "Unknown track blend.", "blend");
            result.blend = blend == "replace" ? cane::TrackBlend::replace : cane::TrackBlend::additive;
        } else require(false, "Unknown track option.", key.c_str());
    }
    return result;
}
cane::QueuedEntryOptions queued_options(const godot::Dictionary& options) {
    cane::QueuedEntryOptions result;
    const auto keys = options.keys();
    for (std::int64_t i = 0; i < keys.size(); ++i) {
        const auto key = option_key(keys[i]); const auto value = options[keys[i]];
        if (key == "start_delay_seconds") result.start_delay_seconds = number(value, key.c_str());
        else if (key == "blend_duration_seconds") result.blend_duration_seconds = number(value, key.c_str());
        else if (key == "looping") result.looping = boolean(value, key.c_str());
        else require(false, "Unknown queued entry option.", key.c_str());
    }
    return result;
}
godot::Variant optional_id(const std::optional<std::string>& id) { return id ? godot::Variant(text(*id)) : godot::Variant(); }
godot::Dictionary track_state(const cane::TrackState& state) {
    godot::Dictionary result;
    result["track_index"] = state.track_index; result["animation_id"] = optional_id(state.animation_id);
    result["playback_time"] = state.playback_time; result["animation_time"] = state.animation_time;
    result["animation_range_start"] = state.animation_range_start; result["animation_range_end"] = state.animation_range_end;
    result["start_delay_seconds"] = state.start_delay_seconds;
    result["playback_end"] = state.playback_end ? godot::Variant(*state.playback_end) : godot::Variant();
    result["blend_weight"] = state.blend_weight; result["playback_rate"] = state.playback_rate;
    result["looping"] = state.looping; result["hold_previous_pose"] = state.hold_previous_pose;
    result["blend"] = state.blend == cane::TrackBlend::replace ? "replace" : "additive";
    result["blend_elapsed"] = state.blend_elapsed; result["blend_duration"] = state.blend_duration; result["blend_progress"] = state.blend_progress;
    result["event_blend_threshold"] = state.event_blend_threshold; result["attachment_blend_threshold"] = state.attachment_blend_threshold;
    result["draw_order_blend_threshold"] = state.draw_order_blend_threshold;
    result["queued_entry_count"] = static_cast<std::int64_t>(state.queued_entry_count);
    return result;
}
}

void CaneSkeleton::bind_playback_methods() {
    using godot::ClassDB; using godot::D_METHOD;
    ClassDB::bind_method(D_METHOD("play", "animation_id", "looping", "track", "mix_seconds"), &CaneSkeleton::play, DEFVAL(true), DEFVAL(0), DEFVAL(-1.0));
    ClassDB::bind_method(D_METHOD("queue", "animation_id", "start_delay_seconds", "looping", "track", "mix_seconds"), &CaneSkeleton::queue, DEFVAL(0.0), DEFVAL(false), DEFVAL(0), DEFVAL(-1.0));
    ClassDB::bind_method(D_METHOD("play_empty", "mix_seconds", "track"), &CaneSkeleton::play_empty, DEFVAL(0));
    ClassDB::bind_method(D_METHOD("queue_empty", "mix_seconds", "start_delay_seconds", "track"), &CaneSkeleton::queue_empty, DEFVAL(0.0), DEFVAL(0));
    ClassDB::bind_method(D_METHOD("set_default_mix", "seconds"), &CaneSkeleton::set_default_mix);
    ClassDB::bind_method(D_METHOD("set_blend_duration", "from_animation_id", "to_animation_id", "seconds"), &CaneSkeleton::set_blend_duration);
    ClassDB::bind_method(D_METHOD("set_playback_time", "track", "seconds"), &CaneSkeleton::set_playback_time);
    ClassDB::bind_method(D_METHOD("set_animation_time", "seconds"), &CaneSkeleton::set_animation_time);
    ClassDB::bind_method(D_METHOD("set_track_options", "track", "options"), &CaneSkeleton::set_track_options);
    ClassDB::bind_method(D_METHOD("set_track_animation_range", "track", "range"), &CaneSkeleton::set_track_animation_range, DEFVAL(godot::Variant()));
    ClassDB::bind_method(D_METHOD("set_playback_end", "track", "seconds"), &CaneSkeleton::set_playback_end, DEFVAL(godot::Variant()));
    ClassDB::bind_method(D_METHOD("set_track_blend_duration", "track", "seconds"), &CaneSkeleton::set_track_blend_duration);
    ClassDB::bind_method(D_METHOD("set_queued_entry_options", "track", "queue_index", "options"), &CaneSkeleton::set_queued_entry_options);
    ClassDB::bind_method(D_METHOD("remove_queued_entry", "track", "queue_index"), &CaneSkeleton::remove_queued_entry);
    ClassDB::bind_method(D_METHOD("stop_track", "track"), &CaneSkeleton::stop_track, DEFVAL(0));
    ClassDB::bind_method(D_METHOD("stop_all_tracks"), &CaneSkeleton::stop_all_tracks);
    ClassDB::bind_method(D_METHOD("get_track_state", "track"), &CaneSkeleton::get_track_state, DEFVAL(0));
    ClassDB::bind_method(D_METHOD("get_queued_entries", "track"), &CaneSkeleton::get_queued_entries, DEFVAL(0));
    ClassDB::bind_method(D_METHOD("get_track_count"), &CaneSkeleton::get_track_count);
    ClassDB::bind_method(D_METHOD("get_default_mix"), &CaneSkeleton::get_default_mix);
}
bool CaneSkeleton::play(const godot::String& id, bool looping, std::int64_t track, double mix) {
    return perform([&](cane::RuntimePlayer& p) { p.play_animation(track_index(track), text(id), looping, optional_mix(mix)); }, false);
}
bool CaneSkeleton::queue(const godot::String& id, double delay, bool looping, std::int64_t track, double mix) {
    return perform([&](cane::RuntimePlayer& p) { p.queue_animation(track_index(track), text(id), looping, scalar(delay, "start_delay_seconds"), optional_mix(mix)); }, false);
}
bool CaneSkeleton::play_empty(double mix, std::int64_t track) {
    return perform([&](cane::RuntimePlayer& p) { p.fade_out_track(track_index(track), scalar(mix, "mix_seconds")); }, false);
}
bool CaneSkeleton::queue_empty(double mix, double delay, std::int64_t track) {
    return perform([&](cane::RuntimePlayer& p) { p.queue_fade_out(track_index(track), scalar(mix, "mix_seconds"), scalar(delay, "start_delay_seconds")); }, false);
}
bool CaneSkeleton::set_default_mix(double seconds) {
    return perform([&](cane::RuntimePlayer& p) { p.set_default_mix(scalar(seconds, "seconds")); }, false);
}
bool CaneSkeleton::set_blend_duration(const godot::String& from, const godot::String& to, double seconds) {
    return perform([&](cane::RuntimePlayer& p) { p.set_blend_duration(text(from), text(to), scalar(seconds, "seconds")); }, false);
}
bool CaneSkeleton::set_playback_time(std::int64_t track, double seconds) {
    return perform([&](cane::RuntimePlayer& p) { p.set_playback_time(track_index(track), scalar(seconds, "seconds")); }, false);
}
bool CaneSkeleton::set_animation_time(double seconds) {
    return perform([&](cane::RuntimePlayer& p) { p.set_animation_time(scalar(seconds, "seconds")); }, false);
}
bool CaneSkeleton::set_track_options(std::int64_t track, const godot::Dictionary& options) {
    return perform([&](cane::RuntimePlayer& p) { p.set_track_options(track_index(track), track_options(options)); }, false);
}
bool CaneSkeleton::set_track_animation_range(std::int64_t track, const godot::Variant& range) {
    return perform([&](cane::RuntimePlayer& p) {
        std::optional<std::pair<float, float>> value;
        require(range.get_type() == godot::Variant::NIL || range.get_type() == godot::Variant::VECTOR2,
            "Animation range must be a Vector2 or null.", "range");
        if (range.get_type() == godot::Variant::VECTOR2) {
            const godot::Vector2 v = range; value = std::make_pair(scalar(v.x, "range.x"), scalar(v.y, "range.y"));
        }
        p.set_track_animation_range(track_index(track), value);
    }, false);
}
bool CaneSkeleton::set_playback_end(std::int64_t track, const godot::Variant& seconds) {
    return perform([&](cane::RuntimePlayer& p) {
        p.set_playback_end(track_index(track), seconds.get_type() == godot::Variant::NIL ? std::nullopt : std::optional<float>(number(seconds, "seconds")));
    }, false);
}
bool CaneSkeleton::set_track_blend_duration(std::int64_t track, double seconds) {
    return perform([&](cane::RuntimePlayer& p) { p.set_track_blend_duration(track_index(track), scalar(seconds, "seconds")); }, false);
}
bool CaneSkeleton::set_queued_entry_options(std::int64_t track, std::int64_t index, const godot::Dictionary& options) {
    return perform([&](cane::RuntimePlayer& p) { p.set_queued_entry_options(track_index(track), queue_index(index), queued_options(options)); }, false);
}
bool CaneSkeleton::remove_queued_entry(std::int64_t track, std::int64_t index) {
    return perform([&](cane::RuntimePlayer& p) { p.remove_queued_entry(track_index(track), queue_index(index)); }, false);
}
bool CaneSkeleton::stop_track(std::int64_t track) { return perform([&](cane::RuntimePlayer& p) { p.stop_track(track_index(track)); }, false); }
bool CaneSkeleton::stop_all_tracks() { return perform([](cane::RuntimePlayer& p) { p.stop_all_tracks(); }, false); }

godot::Dictionary CaneSkeleton::get_track_state(std::int64_t track) {
    godot::Dictionary result;
    query_player([&](const cane::RuntimePlayer& p) { if (const auto state = p.query_track(track_index(track))) assign_dictionary(result, track_state(*state)); });
    return result;
}
godot::Array CaneSkeleton::get_queued_entries(std::int64_t track) {
    godot::Array result;
    query_player([&](const cane::RuntimePlayer& p) {
        for (const auto& entry : p.query_queued_entries(track_index(track))) {
            godot::Dictionary value; value["track_index"] = entry.track_index;
            value["queue_index"] = static_cast<std::int64_t>(entry.queue_index); value["animation_id"] = optional_id(entry.animation_id);
            value["start_delay_seconds"] = entry.start_delay_seconds; value["blend_duration"] = entry.blend_duration; value["looping"] = entry.looping;
            result.push_back(value);
        }
    });
    return result;
}
std::int64_t CaneSkeleton::get_track_count() {
    std::int64_t result = 0; query_player([&](const cane::RuntimePlayer& p) { result = p.track_count(); }); return result;
}
double CaneSkeleton::get_default_mix() {
    double result = 0; query_player([&](const cane::RuntimePlayer& p) { result = p.default_mix(); }); return result;
}
}
