#include "state.hpp"

namespace cane {
namespace {
using detail::PlayerState;
using animation::EventSink;
using animation::AnimationPlayer;
using animation::AnimationPlayback;
using ClockMutation = std::function<void(AnimationPlayer&, EventSink&)>;
void clocks(detail::RuntimePlayerImpl& p, const char* op, const ClockMutation& mutate, bool publish = true) {
    p.configure(op, [&](PlayerState& s, EventSink& live, EventSink& baseline) { mutate(s.live, live); mutate(s.baseline, baseline); }, publish);
}
const AnimationPlayback& current(const AnimationPlayer& state, std::uint32_t index, const char* op) {
    (void)state.query(index); const auto at = state.tracks().find(index);
    if (at == state.tracks().end()) throw Error(ErrorCode::not_found, op, "Current entry does not exist.", "trackIndex");
    return at->second.chain.back();
}
void current_edit(detail::RuntimePlayerImpl& p, const char* op, std::uint32_t index,
                  const std::function<void(AnimationPlayer&)>& edit, const std::function<void(AnimationPlayback&, const AnimationPlayback&)>& project) {
    p.configure(op, [&](PlayerState& s, EventSink&, EventSink&) {
        edit(s.live); const auto& e = current(s.live, index, op);
        project(s.baseline.find_entry(index, e.identity, op), e);
    });
}
}
RuntimeStep RuntimePlayer::advance_physics(float delta_seconds) {
    constexpr auto op = "advancePhysics";
    return detail::operation(op, [&] {
        auto& p = impl(); const auto sequence = p.next_sequence(op); detail::require_nonnegative(delta_seconds, op, "deltaSeconds");
        auto candidate = std::make_unique<PlayerState>(*p.state); constraints::PhysicsBudget budget;
        p.publish(*candidate, p.evaluate(*candidate, budget, {}, nullptr, op, nullptr, sequence, delta_seconds), sequence);
        RuntimeStep result{sequence, candidate->frame->time_seconds(), delta_seconds > 0, {}};
        p.state.swap(candidate); return result;
    });
}
void RuntimePlayer::set_default_mix(float seconds) { clocks(impl(), "setDefaultMix", [&](AnimationPlayer& p, EventSink&) { p.set_default_mix(seconds); }, false); }
void RuntimePlayer::set_blend_duration(std::string_view from, std::string_view to, float seconds) { clocks(impl(), "setBlendDuration", [&](AnimationPlayer& p, EventSink&) { p.set_blend_duration(from, to, seconds); }, false); }
void RuntimePlayer::play_animation(std::uint32_t track, std::string_view id, bool looping, std::optional<float> mix) {
    clocks(impl(), "playAnimation", [&](AnimationPlayer& p, EventSink& sink) { p.replace(track, id, looping, mix, sink); });
}
void RuntimePlayer::queue_animation(std::uint32_t track, std::string_view id, bool looping, float delay, std::optional<float> mix) {
    clocks(impl(), "queueAnimation", [&](AnimationPlayer& p, EventSink& sink) { p.enqueue(track, id, looping, delay, mix, sink); });
}
void RuntimePlayer::fade_out_track(std::uint32_t track, float mix) { clocks(impl(), "fadeOutTrack", [&](AnimationPlayer& p, EventSink& sink) { p.replace(track, {}, false, mix, sink); }); }
void RuntimePlayer::queue_fade_out(std::uint32_t track, float mix, float delay) { clocks(impl(), "queueFadeOut", [&](AnimationPlayer& p, EventSink& sink) { p.enqueue(track, {}, false, delay, mix, sink); }); }
void RuntimePlayer::stop_track(std::uint32_t track) { clocks(impl(), "stopTrack", [&](AnimationPlayer& p, EventSink& sink) { p.clear(track, sink); }); }
void RuntimePlayer::stop_all_tracks() { clocks(impl(), "stopAllTracks", [](AnimationPlayer& p, EventSink& sink) { p.clear_all(sink); }); }
void RuntimePlayer::set_playback_time(std::uint32_t track, float time) {
    current_edit(impl(), "setPlaybackTime", track, [&](AnimationPlayer& p) { p.set_time(track, time); },
                 [](AnimationPlayback& baseline, const AnimationPlayback& live) { baseline.time = live.time; baseline.cursor_initialized = live.cursor_initialized; });
}
void RuntimePlayer::set_animation_time(float time) {
    impl().configure("setAnimationTime", [&](PlayerState& s, EventSink&, EventSink&) {
        s.live.set_animation_time(time); s.baseline.set_detached_time(time);
        if (!s.live.query(0)) return;
        const auto& e = current(s.live, 0, "setAnimationTime"); auto& b = s.baseline.find_entry(0, e.identity, "setAnimationTime");
        b.time = e.time; b.cursor_initialized = true;
    });
}
void RuntimePlayer::set_track_options(std::uint32_t track, const TrackOptions& options) {
    current_edit(impl(), "setTrackOptions", track, [&](AnimationPlayer& p) { p.set_options(track, options); }, [](AnimationPlayback& b, const AnimationPlayback& e) {
        b.blend_weight = e.blend_weight; b.rate = e.rate; b.event_blend_threshold = e.event_blend_threshold; b.attachment_blend_threshold = e.attachment_blend_threshold;
        b.draw_order_blend_threshold = e.draw_order_blend_threshold; b.looping = e.looping; b.hold = e.hold; b.additive = e.additive;
    });
}
void RuntimePlayer::set_track_animation_range(std::uint32_t track, std::optional<std::pair<float, float>> range) {
    current_edit(impl(), "setTrackAnimationRange", track, [&](AnimationPlayer& p) { p.set_range(track, range); }, [](AnimationPlayback& b, const AnimationPlayback& e) { b.start = e.start; b.end = e.end; });
}
void RuntimePlayer::set_playback_end(std::uint32_t track, std::optional<float> time) {
    current_edit(impl(), "setPlaybackEnd", track, [&](AnimationPlayer& p) { p.set_playback_end(track, time); }, [](AnimationPlayback& b, const AnimationPlayback& e) { b.playback_end = e.playback_end; });
}
void RuntimePlayer::set_track_blend_duration(std::uint32_t track, float seconds) {
    current_edit(impl(), "setTrackBlendDuration", track, [&](AnimationPlayer& p) { p.set_blend_duration(track, seconds); }, [](AnimationPlayback& b, const AnimationPlayback& e) { b.blend_duration = e.blend_duration; });
}
void RuntimePlayer::set_queued_entry_options(std::uint32_t track, std::size_t index, const QueuedEntryOptions& options) {
    impl().configure("setQueuedEntryOptions", [&](PlayerState& s, EventSink&, EventSink&) {
        s.live.set_queued_options(track, index, options); const auto& e = s.live.tracks().at(track).queue.at(index);
        auto& b = s.baseline.find_entry(track, e.identity, "setQueuedEntryOptions"); b.start_delay = e.start_delay; b.blend_duration = e.blend_duration; b.looping = e.looping;
    });
}
void RuntimePlayer::remove_queued_entry(std::uint32_t track, std::size_t index) {
    impl().configure("removeQueuedEntry", [&](PlayerState& s, EventSink& live, EventSink& baseline) {
        (void)s.live.query(track); const auto at = s.live.tracks().find(track);
        if (at == s.live.tracks().end() || index >= at->second.queue.size()) throw Error(ErrorCode::not_found, "removeQueuedEntry", "Queued entry does not exist.", "queueIndex");
        const auto identity = at->second.queue[index].identity;
        s.live.remove_queued(track, index, live); s.baseline.remove_queued_identity(track, identity, baseline);
    });
}
std::optional<TrackState> RuntimePlayer::query_track(std::uint32_t track) const { return impl().state->live.query(track); }
std::vector<QueuedEntryState> RuntimePlayer::query_queued_entries(std::uint32_t track) const { return impl().state->live.queued(track); }
std::uint32_t RuntimePlayer::track_count() const { return impl().state->live.allocated_tracks(); }
float RuntimePlayer::default_mix() const { return impl().state->live.default_mix(); }
}
