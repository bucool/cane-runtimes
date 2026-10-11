#include "event_lattice.hpp"
#include "events.hpp"
#include <limits>

namespace cane::animation {
EventSink::EventSink(const RuntimeData& data, std::string operation, bool collect, std::size_t capacity)
    : data_(data), operation_(std::move(operation)), collecting_(collect), capacity_(capacity) {
    if (capacity > max_runtime_events_v1)
        throw Error(ErrorCode::invalid_argument, operation_, "The event budget exceeds the Runtime API limit.", "eventCapacity");
    if (collecting_) occurrences_.reserve(capacity_);
}
void EventSink::require(double minimum) const {
    if (!std::isfinite(minimum) || minimum > static_cast<double>(capacity_ - count_))
        throw Error(ErrorCode::resource_limit, operation_, "The operation exceeds its remaining event budget.",
                    operation_ == "seek" || operation_ == "sampleAt" ? "timeSeconds" : "deltaSeconds");
    if (minimum < 0)
        throw Error(ErrorCode::invalid_argument, operation_, "An event count cannot be negative.", "eventCount");
}
void EventSink::append(EventKind kind, std::uint32_t track, const EventEntry& entry,
                       const EventKey* key, double offset, std::optional<std::uint64_t> order) {
    if (!std::isfinite(offset) || offset < 0)
        throw Error(ErrorCode::non_finite, operation_, "An event crossing requires a finite non-negative wall offset.", "eventOffset");
    require(1);
    if (collecting_) {
        const unsigned phase = kind == EventKind::playback_interrupted || kind == EventKind::playback_ended || kind == EventKind::playback_removed ? 1u : 0u;
        occurrences_.push_back({offset, track, order.value_or(entry.order), phase, count_, kind, entry.clip, key});
    }
    ++count_;
}
void EventSink::lifecycle(EventKind kind, std::uint32_t track, const EventEntry& entry, double offset, std::optional<std::uint64_t> retirement_order) {
    if (kind == EventKind::custom_event)
        throw Error(ErrorCode::invalid_argument, operation_, "A user event requires its authored key.", "eventKind");
    append(kind, track, entry, nullptr, offset, retirement_order);
}
void EventSink::user(std::uint32_t track, const EventEntry& entry, const EventKey& key, double offset) {
    if (!entry.clip) throw Error(ErrorCode::invalid_state, operation_, "An empty entry cannot emit an authored event.", "animationId");
    append(EventKind::custom_event, track, entry, &key, offset);
}
EventBatch EventSink::finish() {
    if (!collecting_ || count_ != capacity_)
        throw Error(ErrorCode::invalid_state, operation_, "Collected events differ from the preflight count.", "eventCount");
    std::sort(occurrences_.begin(), occurrences_.end(), [](const Occurrence& a, const Occurrence& b) {
        return std::tie(a.offset, a.track, a.entry_order, a.phase, a.ordinal)
             < std::tie(b.offset, b.track, b.entry_order, b.phase, b.ordinal);
    });
    std::vector<RuntimeEvent> values;
    values.reserve(count_);
    for (const auto& at : occurrences_) {
        RuntimeEvent value; value.kind = at.kind; value.track_index = at.track;
        if (at.clip) value.animation_id = at.clip->id;
        if (at.key) {
            const auto& key = *at.key;
            value.timeline_time_seconds = key.time;
            value.name = key.name;
            if (key.event_index) {
                const auto& event = data_.catalog(RuntimeCatalogKind::event).at(*key.event_index);
                value.event_id = event.id; value.name = event.name;
            }
            value.integer_value = key.integer_value; value.string_value = key.string_value;
            value.number_value = key.number_value; value.volume = key.volume; value.balance = key.balance;
            if (key.audio_index) value.audio_id = data_.catalog(RuntimeCatalogKind::audio).at(*key.audio_index).id;
        }
        values.push_back(std::move(value));
    }
    return EventBatch(std::move(values));
}

bool collect_crossings(const EventEntry& entry, std::uint32_t track, const EventInterval& interval,
                       EventSink& sink, const IncomingEventMix* incoming) {
    const auto finite = [](double value) { return std::isfinite(value); };
    if (!finite(interval.previous) || !finite(interval.current) || !finite(interval.base_offset) ||
        !finite(interval.delta) || !finite(interval.base_offset + interval.delta) ||
        !finite(entry.range_start) || !finite(entry.range_end) || !finite(entry.event_blend_threshold) ||
        (incoming && (!finite(incoming->time) || !finite(incoming->duration))))
        throw Error(ErrorCode::non_finite, sink.operation(), "Event traversal values must be finite.", "timeSeconds");
    if (interval.base_offset < 0 || interval.delta < 0 || entry.range_start < 0 || entry.range_end < entry.range_start ||
        (entry.clip && entry.range_end > entry.clip->duration) || entry.event_blend_threshold < 0 || entry.event_blend_threshold > 1 ||
        (!entry.looping && (interval.previous < 0 || interval.current < 0)) ||
        (incoming && (incoming->time < 0 || incoming->duration < 0)) ||
        (interval.delta == 0 && interval.previous != interval.current))
        throw Error(ErrorCode::invalid_argument, sink.operation(), "Event traversal range or interval is invalid.", "timeSeconds");
    if (interval.previous == interval.current) return entry.cursor_initialized;
    if (!entry.clip) return true;

    const double previous = interval.previous, current = interval.current;
    const double duration = static_cast<float>(entry.range_end - entry.range_start);
    const bool forward = current > previous;
    const auto& keys = entry.clip->events;
    const double requested=interval.delta*entry.playback_rate;
    const auto relative_wall = [&](double raw) { return requested==0?0:std::clamp((raw-previous)/requested,0.0,1.0)*interval.delta; };
    const auto wall = [&](double raw) { return interval.base_offset + relative_wall(raw); };
    const auto enabled = [&](double relative_offset) {
        if (!incoming) return true;
        const double progress = incoming->duration == 0 ? 1 :
            std::clamp((static_cast<double>(incoming->time) + relative_offset) / incoming->duration, 0.0, 1.0);
        return progress < entry.event_blend_threshold;
    };
    const auto user = [&](const EventKey& key, double raw) {
        const double relative = relative_wall(raw);
        if (enabled(relative)) sink.user(track, entry, key, interval.base_offset + relative);
    };
    float initial=entry.looping&&duration>0?std::fmod(interval.previous,static_cast<float>(duration)):std::clamp(interval.previous,0.0f,static_cast<float>(duration));
    if(initial<0)initial+=static_cast<float>(duration);
    initial+=entry.range_start;
    if (!entry.cursor_initialized && enabled(0))
        for (const auto& key : keys) if (key.time == initial) sink.user(track, entry, key, interval.base_offset);
    if (duration == 0) return true;
    if (!entry.looping) {
        if (forward) {
            for (const auto& key : keys) {
                const double raw = static_cast<float>(key.time - entry.range_start);
                if (raw >= 0 && raw <= duration && raw > previous && raw <= current) user(key, raw);
            }
            if (previous < duration && current >= duration) sink.lifecycle(EventKind::playback_completed, track, entry, wall(duration));
        } else {
            for (auto key = keys.rbegin(); key != keys.rend(); ++key) {
                const double raw = static_cast<float>(key->time - entry.range_start);
                if (raw >= 0 && raw <= duration && raw >= current && raw < previous) user(*key, raw);
            }
            if (previous > 0 && current <= 0) sink.lifecycle(EventKind::playback_completed, track, entry, wall(0));
        }
        return true;
    }

    const event_lattice::Bounds bounds{std::min(previous,current),std::max(previous,current),!forward,forward};
    std::optional<event_lattice::Bounds> timeline=bounds;
    if(incoming){
        const double remaining=entry.event_blend_threshold*static_cast<double>(incoming->duration)-incoming->time;
        if(incoming->duration==0 || remaining<=0)timeline.reset();
        else if(remaining<=interval.delta){const double threshold=previous+requested*(remaining/interval.delta);
            if(forward){if(threshold<=bounds.low)timeline.reset();else if(threshold<=bounds.high){timeline->high=threshold;timeline->include_high=false;}}
            else {if(threshold>=bounds.high)timeline.reset();else if(threshold>=bounds.low){timeline->low=threshold;timeline->include_low=false;}}
        }
    }
    struct Candidate { double cycle,offset; int phase; std::size_t declaration; const EventKey* key; };
    std::vector<Candidate> candidates;
    const auto add=[&](const std::optional<event_lattice::Bounds>& b,double key_offset,int phase,std::size_t declaration,const EventKey* key){
        if(!b)return;
        const auto indices=event_lattice::indices(*b,key_offset,duration);if(!indices)return;
        const double count=indices->second-indices->first+1;sink.require(count+static_cast<double>(candidates.size()));
        for(std::size_t n=0;n<static_cast<std::size_t>(count);++n){double cycle=indices->first+static_cast<double>(n),position=key_offset;if(position==duration){++cycle;position=0;}candidates.push_back({cycle,position,phase,declaration,key});}
    };
    add(bounds,0,1,0,nullptr);
    for(std::size_t i=0;i<keys.size();++i){const auto& k=keys[i];if(k.time<entry.range_start || k.time>entry.range_end)continue;add(timeline,static_cast<float>(k.time-entry.range_start),k.time==entry.range_end?0:k.time==entry.range_start?2:1,i,&k);}
    std::sort(candidates.begin(),candidates.end(),[&](const Candidate& a,const Candidate& b){const auto x=std::tie(a.cycle,a.offset,a.phase,a.declaration),y=std::tie(b.cycle,b.offset,b.phase,b.declaration);return forward?x<y:y<x;});
    for(const auto& candidate:candidates){const auto relative=event_lattice::relative(candidate.cycle,candidate.offset,duration,previous);
        const auto at=interval.base_offset+(requested==0?0:std::clamp(relative/requested,0.0,1.0)*interval.delta);
        if(candidate.key)sink.user(track,entry,*candidate.key,at);else sink.lifecycle(EventKind::playback_completed,track,entry,at);
    }
    return true;
}
}
