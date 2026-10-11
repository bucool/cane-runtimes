#pragma once
#include "pose.hpp"
#include <functional>
#include <limits>
#include <map>
#include <set>
#include <variant>

namespace cane::animation {
enum class PoseChannelKind { scalar, angle, bounded, vector, choice, order };
struct PoseValue {
    float number = 0;
    std::vector<float> components;
    std::variant<std::monostate, bool, TransformMode, std::optional<std::string>, std::uint32_t, std::vector<std::size_t>> choice;
    static PoseValue numeric(float value) { PoseValue v; v.number = value; return v; }
    static PoseValue vector(std::vector<float> values) { PoseValue v; v.components = std::move(values); return v; }
    template<class T> static PoseValue discrete(T value) { PoseValue v; v.choice = std::move(value); return v; }
};
struct PoseChannel {
    PoseChannelKind kind = PoseChannelKind::scalar;
    PoseValue setup;
    float minimum = 0, maximum = 1;
};
class AnimationWork {
    std::size_t remaining_ = 1'000'000'000;
public:
    void use(std::size_t amount = 1) {
        if (amount > remaining_) throw Error(ErrorCode::invalid_argument, "apply", "Animation work limit exceeded.");
        remaining_ -= amount;
    }
};
struct ChannelSample {
    std::map<std::size_t, PoseValue> values;
    std::vector<std::vector<std::size_t>> order_patches;
    float weight = 0;
    bool additive = false;
};
inline float narrow_pose_number(double value) {
    if (!std::isfinite(value) || std::abs(value) > std::numeric_limits<float>::max())
        throw Error(ErrorCode::non_finite, "apply", "Animation channel exceeded binary32 range.");
    return static_cast<float>(value);
}
inline double pose_angle_delta(double value) { return std::fmod(std::fmod(value + 180, 360) + 360, 360) - 180; }
inline PoseValue mix_pose_value(const PoseChannel& channel, const PoseValue& from, const PoseValue& to, float weight, bool additive) {
    if (weight == 0) return from;
    if (channel.kind == PoseChannelKind::choice || channel.kind == PoseChannelKind::order) return weight >= .5f ? to : from;
    const double w = weight;
    const auto linear = [w, additive](float a, float base, float b) {
        return additive ? a + w * (static_cast<double>(b) - base) : (1 - w) * a + w * b;
    };
    if (channel.kind == PoseChannelKind::vector) {
        if (from.components.size() != to.components.size() || from.components.size() != channel.setup.components.size())
            throw Error(ErrorCode::invalid_argument, "apply", "Animation vector layout changed.");
        std::vector<float> result(from.components.size());
        for (std::size_t i = 0; i < result.size(); ++i) result[i] = narrow_pose_number(linear(from.components[i], channel.setup.components[i], to.components[i]));
        return PoseValue::vector(std::move(result));
    }
    if (channel.kind == PoseChannelKind::angle) {
        const double value = !additive && weight == 1 ? to.number : from.number + w * pose_angle_delta(static_cast<double>(to.number) - (additive ? channel.setup.number : from.number));
        return PoseValue::numeric(narrow_pose_number(value));
    }
    double value = linear(from.number, channel.setup.number, to.number);
    if (channel.kind == PoseChannelKind::bounded) value = std::clamp(value, static_cast<double>(channel.minimum), static_cast<double>(channel.maximum));
    return PoseValue::numeric(narrow_pose_number(value));
}
inline std::vector<PoseValue> apply_channel_sample(const std::vector<PoseChannel>& schema, const std::vector<PoseValue>& underlay, const ChannelSample& sample, std::size_t order_index, AnimationWork& work) {
    work.use(schema.size() + sample.values.size());
    auto result = underlay;
    if (sample.weight == 0) return result;
    for (const auto& pair : sample.values) {
        const auto& channel = schema.at(pair.first);
        if (channel.kind == PoseChannelKind::vector) work.use(pair.second.components.size());
        result[pair.first] = mix_pose_value(channel, underlay[pair.first], pair.second, sample.weight, sample.additive);
    }
    if (sample.weight >= .5f && !sample.order_patches.empty()) {
        auto order = std::get<std::vector<std::size_t>>(underlay[order_index].choice);
        for (const auto& patch : sample.order_patches) {
            work.use(order.size() + patch.size());
            const std::set<std::size_t> members(patch.begin(), patch.end());
            if (members.size() != patch.size()) throw Error(ErrorCode::invalid_argument, "apply", "Duplicate order member.");
            std::size_t cursor = 0;
            for (auto& id : order) if (members.count(id)) id = patch.at(cursor++);
            if (cursor != patch.size()) throw Error(ErrorCode::invalid_argument, "apply", "Unknown order member.");
        }
        result[order_index] = PoseValue::discrete(std::move(order));
    }
    return result;
}
inline std::vector<PoseValue> transition_channels(const std::vector<PoseChannel>& schema, const std::vector<PoseValue>& from, const std::vector<PoseValue>& to, const ChannelSample& sample, float progress, bool hold, AnimationWork& work) {
    if (progress == 0) return from;
    if (progress == 1) return to;
    work.use(schema.size());
    std::vector<PoseValue> result; result.reserve(schema.size());
    for (std::size_t i = 0; i < schema.size(); ++i) {
        if (hold && sample.values.count(i) == 0) { result.push_back(from[i]); continue; }
        if (schema[i].kind == PoseChannelKind::vector) work.use(from[i].components.size());
        result.push_back(mix_pose_value(schema[i], from[i], to[i], progress, false));
    }
    return result;
}
}
