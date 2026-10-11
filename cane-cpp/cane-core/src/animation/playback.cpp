#include "playback.hpp"
#include "runtime_data_internal.hpp"
#include <limits>

namespace cane::animation {
namespace {
void index_valid(std::uint32_t index, const char* op) {
    if (index > 4095) throw Error(ErrorCode::invalid_argument, op, "Track index must be in 0..4095.", "trackIndex");
}
void finite(float value, const char* op, const char* field) {
    if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, op, "AnimationPlayer scalar must be finite.", field);
}
void nonnegative(float value, const char* op, const char* field) {
    finite(value, op, field);
    if (value < 0) throw Error(ErrorCode::invalid_argument, op, "AnimationPlayer scalar must be non-negative.", field);
}
void unit(const std::optional<float>& value, const char* op, const char* field) {
    if (!value) return;
    finite(*value, op, field);
    if (*value < 0 || *value > 1) throw Error(ErrorCode::invalid_argument, op, "AnimationPlayer weight must be in 0..1.", field);
}
float progress(const Track& track, std::size_t at) {
    const auto& e = track.chain[at];
    return at == 0 || e.blend_duration == 0 ? 1 : std::clamp(e.blend_elapsed / e.blend_duration, 0.0f, 1.0f);
}
}
float AnimationPlayback::pose_time() const noexcept {
    const float d = duration(); if (d == 0) return start;
    float value = looping ? std::fmod(time, d) : std::clamp(time, 0.0f, d);
    if (value < 0) value += d;
    return start + value;
}
float AnimationPlayer::time() const noexcept {
    const auto found = tracks_.find(0); return found == tracks_.end() ? detached_time_ : found->second.chain.back().pose_time();
}
AnimationPlayer AnimationPlayer::clone_configuration() const {
    AnimationPlayer result(data_); result.default_mix_ = default_mix_; result.mixes_ = mixes_; return result;
}
AnimationPlayer AnimationPlayer::rebind(RuntimeData data, const char* operation) const {
    auto result = *this; result.data_ = std::move(data);
    const auto bind = [&](AnimationPlayback& entry) {
        if (!entry.clip) return;
        entry.clip = result.resolve(entry.clip->id, operation);
        if (entry.start < 0 || entry.end < entry.start || entry.end > entry.clip->duration)
            throw Error(ErrorCode::invalid_argument, operation, "Active or queued animation range no longer fits the clip.", "animationRange", entry.clip->id);
    };
    for (auto& track : result.tracks_) {
        for (auto& entry : track.second.chain) bind(entry);
        for (auto& entry : track.second.queue) bind(entry);
    }
    for (const auto& pair : result.mixes_) { (void)result.resolve(pair.first.first, operation); (void)result.resolve(pair.first.second, operation); }
    return result;
}
void AnimationPlayer::set_detached_time(float time) { nonnegative(time, "setAnimationTime", "timeSeconds"); detached_time_ = time; }
AnimationPlayer AnimationPlayer::reconcile(RuntimeData data) const {
    auto result = *this; result.data_ = std::move(data);
    const auto& next = detail::RuntimeDataAccess::get(result.data_);
    const auto bind = [&](AnimationPlayback& entry) {
        if (!entry.clip) return true;
        const auto at = next.index.catalogs[10].find(entry.clip->id);
        if (at == next.index.catalogs[10].end()) return false;
        const bool full_range = entry.start == 0 && entry.end == entry.clip->duration;
        entry.clip = &next.clips[at->second];
        if (full_range) { entry.start = 0; entry.end = entry.clip->duration; }
        else { entry.start = std::min(entry.start, entry.clip->duration); entry.end = std::min(entry.end, entry.clip->duration); }
        // Absolute elapsed time, event identity/cursor and mix progress remain unchanged.
        return true;
    };
    for (auto track = result.tracks_.begin(); track != result.tracks_.end();) {
        auto& chain = track->second.chain; std::size_t drop = 0;
        for (std::size_t i = 0; i < chain.size(); ++i) if (!bind(chain[i])) drop = i + 1;
        if (drop == chain.size()) { track = result.tracks_.erase(track); continue; }
        chain.erase(chain.begin(), chain.begin() + static_cast<std::ptrdiff_t>(drop));
        auto& queue = track->second.queue;
        queue.erase(std::remove_if(queue.begin(), queue.end(), [&](auto& e) { return !bind(e); }), queue.end());
        ++track;
    }
    for (auto pair = result.mixes_.begin(); pair != result.mixes_.end();) {
        if (!next.index.catalogs[10].count(pair->first.first) || !next.index.catalogs[10].count(pair->first.second)) pair = result.mixes_.erase(pair);
        else ++pair;
    }
    return result;
}
const Clip* AnimationPlayer::resolve(std::optional<std::string_view> id, const char* operation) const {
    if (!id) return nullptr;
    const auto& state = detail::RuntimeDataAccess::get(data_); const auto found = state.index.catalogs[10].find(std::string(*id));
    if (found == state.index.catalogs[10].end()) throw Error(ErrorCode::not_found, operation, "Animation does not exist.", "animationId", std::string(*id));
    return &state.clips[found->second];
}
Track& AnimationPlayer::require_track(std::uint32_t index, const char* operation) {
    index_valid(index, operation); const auto found = tracks_.find(index);
    if (found == tracks_.end()) throw Error(ErrorCode::not_found, operation, "Current track entry does not exist.", "trackIndex");
    return found->second;
}
AnimationPlayback AnimationPlayer::make_entry(const Clip* clip, bool looping, float mix, const char* operation) {
    if (next_identity_ == std::numeric_limits<std::uint64_t>::max()) throw Error(ErrorCode::resource_limit, operation, "AnimationPlayer entry identities are exhausted.", "trackIndex");
    AnimationPlayback e; e.clip = clip; e.end = clip ? clip->duration : 0; e.looping = looping;
    e.blend_duration = mix; e.identity = next_identity_++; return e;
}
float AnimationPlayer::mix(const AnimationPlayback& from, const Clip* to) const {
    if (!from.clip || !to) return default_mix_;
    const auto found = mixes_.find({from.clip->id, to->id}); return found == mixes_.end() ? default_mix_ : found->second;
}
void AnimationPlayer::set_default_mix(float seconds) { nonnegative(seconds, "setDefaultMix", "durationSeconds"); default_mix_ = seconds; }
void AnimationPlayer::set_blend_duration(std::string_view from, std::string_view to, float seconds) {
    nonnegative(seconds, "setBlendDuration", "durationSeconds"); (void)resolve(from, "setBlendDuration"); (void)resolve(to, "setBlendDuration");
    mixes_[{std::string(from), std::string(to)}] = seconds;
}
void AnimationPlayer::retire(const Track& track, std::size_t count, std::uint32_t index, double offset, EventSink& sink) {
    if (count == 0) return;
    const auto block_order = track.chain[count - 1].identity;
    for (std::size_t at = count; at-- > 0;) {
        const auto view = track.chain[at].event_view();
        sink.lifecycle(EventKind::playback_ended, index, view, offset, block_order);
        sink.lifecycle(EventKind::playback_removed, index, view, offset, block_order);
    }
}
void AnimationPlayer::replace(std::uint32_t index, std::optional<std::string_view> id, bool looping, std::optional<float> seconds, EventSink& sink) {
    const auto* op = sink.operation().c_str(); index_valid(index, op); if (seconds) nonnegative(*seconds, op, "mixSeconds");
    const auto* clip = resolve(id, op); const auto found = tracks_.find(index);
    const float duration = found == tracks_.end() ? 0 : seconds.value_or(mix(found->second.chain.back(), clip));
    auto next = make_entry(clip, looping, duration, op);
    if (found != tracks_.end()) {
        auto& old = found->second; sink.lifecycle(EventKind::playback_interrupted, index, old.chain.back().event_view(), 0);
        if (duration == 0) retire(old, old.chain.size(), index, 0, sink);
        for (const auto& queued : old.queue) sink.lifecycle(EventKind::playback_removed, index, queued.event_view(), 0);
        if (duration == 0) old.chain.clear();
        old.queue.clear(); old.chain.push_back(std::move(next));
    } else { Track track; track.chain.push_back(std::move(next)); tracks_.emplace(index, std::move(track)); }
    allocated_tracks_ = std::max(allocated_tracks_, index + 1);
    sink.lifecycle(EventKind::playback_started, index, tracks_.at(index).chain.back().event_view(), 0);
}
void AnimationPlayer::enqueue(std::uint32_t index, std::optional<std::string_view> id, bool looping, float delay,
                        std::optional<float> seconds, EventSink& sink) {
    const auto* op = sink.operation().c_str(); index_valid(index, op); finite(delay, op, "startDelaySeconds");
    if (seconds) nonnegative(*seconds, op, "mixSeconds");
    const auto* clip = resolve(id, op);
    const auto found = tracks_.find(index);
    if (found == tracks_.end()) { replace(index, id, looping, 0.0f, sink); return; }
    auto& track = found->second; const auto& previous = track.queue.empty() ? track.chain.back() : track.queue.back();
    const float duration = seconds.value_or(mix(previous, clip));
    const float threshold = delay > 0 ? delay : std::max(0.0f, previous.duration() - duration + delay);
    finite(threshold, op, "startDelaySeconds"); auto e = make_entry(clip, looping, duration, op); e.start_delay = threshold;
    track.queue.push_back(std::move(e));
}
void AnimationPlayer::clear(std::uint32_t index, EventSink& sink, double offset) {
    index_valid(index, sink.operation().c_str()); const auto found = tracks_.find(index); if (found == tracks_.end()) return;
    const auto& track = found->second; retire(track, track.chain.size(), index, offset, sink);
    for (const auto& queued : track.queue) sink.lifecycle(EventKind::playback_removed, index, queued.event_view(), offset);
    tracks_.erase(found);
}
void AnimationPlayer::clear_all(EventSink& sink) { while (!tracks_.empty()) clear(tracks_.begin()->first, sink); }
void AnimationPlayer::set_time(std::uint32_t index, float value) {
    nonnegative(value, "setPlaybackTime", "timeSeconds"); auto& e = require_track(index, "setPlaybackTime").chain.back(); e.time = value; e.cursor_initialized = true;
}
void AnimationPlayer::set_animation_time(float value) {
    nonnegative(value, "setAnimationTime", "timeSeconds"); detached_time_ = value;
    const auto found = tracks_.find(0); if (found == tracks_.end()) return;
    auto& e = found->second.chain.back(); e.time = std::clamp(value, e.start, e.end) - e.start; e.cursor_initialized = true;
}
void AnimationPlayer::set_options(std::uint32_t index, const TrackOptions& o) {
    constexpr auto* op = "setTrackOptions"; index_valid(index, op);
    unit(o.blend_weight, op, "blendWeight"); unit(o.event_blend_threshold, op, "eventBlendThreshold"); unit(o.attachment_blend_threshold, op, "attachmentBlendThreshold"); unit(o.draw_order_blend_threshold, op, "drawOrderBlendThreshold");
    if (o.playback_rate) finite(*o.playback_rate, op, "playbackRate");
    if (o.blend && *o.blend != TrackBlend::replace && *o.blend != TrackBlend::additive) throw Error(ErrorCode::invalid_argument, op, "Unknown track blend.", "blend");
    auto& e = require_track(index, op).chain.back();
    if (o.blend_weight) e.blend_weight = *o.blend_weight;
    if (o.playback_rate) e.rate = *o.playback_rate;
    if (o.event_blend_threshold) e.event_blend_threshold = *o.event_blend_threshold;
    if (o.attachment_blend_threshold) e.attachment_blend_threshold = *o.attachment_blend_threshold;
    if (o.draw_order_blend_threshold) e.draw_order_blend_threshold = *o.draw_order_blend_threshold;
    if (o.looping) e.looping = *o.looping;
    if (o.hold_previous_pose) e.hold = *o.hold_previous_pose;
    if (o.blend) e.additive = *o.blend == TrackBlend::additive;
}
void AnimationPlayer::set_range(std::uint32_t index, std::optional<std::pair<float, float>> range) {
    constexpr auto* op = "setTrackAnimationRange"; auto& e = require_track(index, op).chain.back();
    const float start = range ? range->first : 0, end = range ? range->second : e.clip ? e.clip->duration : 0;
    nonnegative(start, op, "start"); nonnegative(end, op, "end");
    if (start > end || end > (e.clip ? e.clip->duration : 0)) throw Error(ErrorCode::invalid_argument, op, "Invalid animation range.", "range");
    e.start = start; e.end = end;
}
void AnimationPlayer::set_playback_end(std::uint32_t index, std::optional<float> value) {
    if (value) nonnegative(*value, "setPlaybackEnd", "playbackEnd");
    require_track(index, "setPlaybackEnd").chain.back().playback_end = value;
}
void AnimationPlayer::set_blend_duration(std::uint32_t index, float value) {
    nonnegative(value, "setTrackBlendDuration", "blendDuration"); require_track(index, "setTrackBlendDuration").chain.back().blend_duration = value;
}
void AnimationPlayer::set_queued_options(std::uint32_t index, std::size_t queue_index, const QueuedEntryOptions& options) {
    constexpr auto* op = "setQueuedEntryOptions";
    if (!options.start_delay_seconds && !options.blend_duration_seconds && !options.looping) throw Error(ErrorCode::invalid_argument, op, "At least one queued option is required.", "options");
    if (options.start_delay_seconds) nonnegative(*options.start_delay_seconds, op, "startDelaySeconds");
    if (options.blend_duration_seconds) nonnegative(*options.blend_duration_seconds, op, "blendDurationSeconds");
    auto& queue = require_track(index, op).queue;
    if (queue_index >= queue.size()) throw Error(ErrorCode::not_found, op, "Queued entry does not exist.", "queueIndex");
    auto& e = queue[queue_index]; if (options.start_delay_seconds) e.start_delay = *options.start_delay_seconds;
    if (options.blend_duration_seconds) e.blend_duration = *options.blend_duration_seconds;
    if (options.looping) e.looping = *options.looping;
}
void AnimationPlayer::remove_queued(std::uint32_t index, std::size_t queue_index, EventSink& sink) {
    auto& queue = require_track(index, sink.operation().c_str()).queue;
    if (queue_index >= queue.size()) throw Error(ErrorCode::not_found, sink.operation(), "Queued entry does not exist.", "queueIndex");
    sink.lifecycle(EventKind::playback_removed, index, queue[queue_index].event_view(), 0);
    queue.erase(queue.begin() + static_cast<std::ptrdiff_t>(queue_index));
}
void AnimationPlayer::remove_queued_identity(std::uint32_t index, std::uint64_t identity, EventSink& sink) {
    const auto& queue = require_track(index, sink.operation().c_str()).queue;
    for (std::size_t i = 0; i < queue.size(); ++i) if (queue[i].identity == identity) { remove_queued(index, i, sink); return; }
    throw Error(ErrorCode::invalid_state, sink.operation(), "Entry is not queued in this configuration baseline.", "queueIndex");
}
AnimationPlayback& AnimationPlayer::find_entry(std::uint32_t index, std::uint64_t identity, const char* op) {
    auto& t = require_track(index, op);
    for (auto& e : t.chain) if (e.identity == identity) return e;
    for (auto& e : t.queue) if (e.identity == identity) return e;
    throw Error(ErrorCode::invalid_state, op, "Entry identity is absent from this configuration baseline.", "trackIndex");
}
std::optional<TrackState> AnimationPlayer::query(std::uint32_t index) const {
    index_valid(index, "queryTrackState"); const auto found = tracks_.find(index); if (found == tracks_.end()) return {};
    const auto& track = found->second; const auto& e = track.chain.back(); TrackState state;
    state.track_index = index; if (e.clip) state.animation_id = e.clip->id;
    state.playback_time = e.time; state.animation_time = e.pose_time(); state.animation_range_start = e.start; state.animation_range_end = e.end;
    state.start_delay_seconds = e.start_delay; state.playback_end = e.playback_end; state.blend_weight = e.blend_weight; state.playback_rate = e.rate;
    state.looping = e.looping; state.hold_previous_pose = e.hold; state.blend = e.additive ? TrackBlend::additive : TrackBlend::replace;
    state.blend_elapsed = e.blend_elapsed; state.blend_duration = e.blend_duration; state.blend_progress = progress(track, track.chain.size() - 1);
    state.event_blend_threshold = e.event_blend_threshold; state.attachment_blend_threshold = e.attachment_blend_threshold; state.draw_order_blend_threshold = e.draw_order_blend_threshold;
    state.queued_entry_count = track.queue.size(); return state;
}
std::vector<QueuedEntryState> AnimationPlayer::queued(std::uint32_t index) const {
    index_valid(index, "queryQueuedEntries"); std::vector<QueuedEntryState> values;
    const auto found = tracks_.find(index); if (found == tracks_.end()) return values;
    values.reserve(found->second.queue.size());
    for (const auto& e : found->second.queue) values.push_back({index, values.size(), e.clip ? std::optional<std::string>(e.clip->id) : std::nullopt, e.start_delay, e.blend_duration, e.looping});
    return values;
}
}
