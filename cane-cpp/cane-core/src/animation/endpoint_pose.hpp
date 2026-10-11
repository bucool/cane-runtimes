#pragma once
#include "pose_channels.hpp"

namespace cane::animation {
class EndpointPose {
    Pose& pose_;
    AnimationWork& work_;
    std::map<PropertyKey, std::size_t> keys_;
    std::vector<std::function<void(const PoseValue&)>> publish_;
    std::size_t add(PropertyKey key, PoseChannelKind kind, PoseValue setup, PoseValue current,
                    std::function<void(const PoseValue&)> setter, float minimum = 0, float maximum = 1);
public:
    std::vector<PoseChannel> schema;
    std::vector<PoseValue> initial;
    std::size_t order_index = 0;
    EndpointPose(Pose& pose, AnimationWork& work);
    [[nodiscard]] ChannelSample sample(const Layer* layer);
    void finish(const std::vector<PoseValue>& values);
};
}
