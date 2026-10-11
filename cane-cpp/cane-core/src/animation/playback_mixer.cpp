#include "playback.hpp"
#include "endpoint_pose.hpp"

namespace cane::animation {
namespace {
float progress(const Track& track, std::size_t i) {
    const auto& e = track.chain[i];
    return i == 0 || e.blend_duration == 0 ? 1 : std::clamp(e.blend_elapsed / e.blend_duration, 0.0f, 1.0f);
}
}
void AnimationPlayer::apply(Pose& pose, PlaybackWorkspace& scratch, const Sampling& sampling) const {
    (void)scratch;
    AnimationWork work; EndpointPose projection(pose,work); auto values = projection.initial;
    for (const auto& pair : tracks_) {
        const auto& track = pair.second;
        if (track.chain.empty()) continue;
        std::size_t begin = track.chain.size() - 1;
        while (begin > 0 && progress(track,begin) < 1) { work.use(); --begin; }
        const auto underlay = values; auto outgoing = values; bool first = true;
        for (std::size_t i = begin; i < track.chain.size(); ++i) {
            work.use(); const auto& entry = track.chain[i]; const float fraction = progress(track,i);
            if (!first && fraction == 0) continue;
            ChannelSample sample;
            if (entry.clip) {
                Layer layer{*entry.clip}; layer.time = sampling.time(entry.pose_time()); layer.alpha = entry.blend_weight;
                layer.additive = entry.additive; layer.stepped = sampling.stepped;
                if (i + 1 < track.chain.size()) {
                    const float incoming = progress(track,i+1);
                    layer.attachments = incoming < entry.attachment_blend_threshold;
                    layer.draw_order = incoming < entry.draw_order_blend_threshold;
                }
                sample = projection.sample(&layer);
            }
            auto incoming = apply_channel_sample(projection.schema,underlay,sample,projection.order_index,work);
            outgoing = first || fraction == 1 ? std::move(incoming) : transition_channels(projection.schema,outgoing,incoming,sample,fraction,entry.hold,work);
            first = false;
        }
        values = std::move(outgoing);
    }
    projection.finish(values);
}
}
