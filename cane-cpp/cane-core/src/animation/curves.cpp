#include "curves.hpp"
#include "cubic_roots.hpp"
#include "cane/error.hpp"
#include "cane/math.hpp"
#include <algorithm>
#include <cmath>

namespace cane::animation {
namespace {
void finite(float value, const char* field) {
    if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, "sampleCurve", "Animation input must be finite.", field);
}
float result(float value) {
    if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, "sampleCurve", "Animation arithmetic produced a non-finite result.");
    return value;
}

}
Curve::Curve(CurveKind kind, float x1, float y1, float x2, float y2)
    : kind_(kind), x1_(x1), y1_(y1), x2_(x2), y2_(y2) {
    if (kind != CurveKind::linear && kind != CurveKind::stepped && kind != CurveKind::bezier && kind != CurveKind::bezier_value)
        throw Error(ErrorCode::invalid_argument, "compileCurve", "Unknown curve kind.", "type");
    finite(x1, "cx1"); finite(y1, "cy1/dy1"); finite(x2, "cx2"); finite(y2, "cy2/dy2");
}
float Curve::sample(float from, float to, float progress, bool angular, bool discrete) const {
    if (angular) to = from + wrap_degrees(to - from);
    finite(from, "from"); finite(to, "to"); finite(progress, "progress");
    if (progress <= 0) return from;
    if (progress >= 1) return to;
    if (discrete || kind_ == CurveKind::stepped) return from;
    if (kind_ == CurveKind::linear) return result(from + (to - from) * progress);
    const double start = from, end = to;
    const bool percent = kind_ == CurveKind::bezier;
    const double c1 = percent ? start + (end - start) * y1_ : start + y1_;
    const double c2 = percent ? start + (end - start) * y2_ : end + y2_;
    return result(static_cast<float>(cubic_roots::sample({0, x1_, x2_, 1}, {start, c1, c2, end}, progress)));
}

Curve PropertyCurves::select(std::string_view property) const {
    const auto found = properties.find(std::string(property));
    return found == properties.end() ? default_curve : found->second;
}
ScalarChannel::ScalarChannel(std::vector<ScalarKey> keys) {
    keys_.reserve(keys.size());
    for (const auto& key : keys) {
        finite(key.time, "time"); finite(key.value, "value");
        if (key.time < 0 || (!keys_.empty() && key.time < keys_.back().time))
            throw Error(ErrorCode::validation_failed, "compileChannel", "Key times must be non-negative and in declaration-time order.", "time");
        if (!keys_.empty() && key.time == keys_.back().time) keys_.back() = key;
        else keys_.push_back(key);
    }
}
ScalarChannel ScalarChannel::merge(const ScalarChannel& paired, const ScalarChannel& axis) {
    ScalarChannel result; result.keys_.reserve(paired.keys_.size() + axis.keys_.size());
    auto p = paired.keys_.begin(), q = axis.keys_.begin();
    while (p != paired.keys_.end() || q != axis.keys_.end()) {
        if (q == axis.keys_.end() || (p != paired.keys_.end() && p->time <= q->time)) result.keys_.push_back(*p++);
        else result.keys_.push_back(*q++);
    }
    return result;
}
std::optional<float> ScalarChannel::sample(float time, bool angular, bool discrete) const {
    finite(time, "time");
    if (keys_.empty() || time < keys_.front().time) return {};
    const auto after = std::upper_bound(keys_.begin(), keys_.end(), time, [](float t, const ScalarKey& key) { return t < key.time; });
    const auto& outgoing = *std::prev(after);
    if (after == keys_.end() || time == outgoing.time) return outgoing.value;
    return outgoing.curve.sample(outgoing.value, after->value, (time - outgoing.time) / (after->time - outgoing.time), angular, discrete);
}
float fixed_frame_time(float pose_time, float frame_step_seconds) {
    finite(pose_time, "time"); finite(frame_step_seconds, "frameStepSeconds");
    if (frame_step_seconds <= 0) throw Error(ErrorCode::invalid_argument, "sampleAt", "Frame step must be positive.", "frameStepSeconds");
    const auto frames = static_cast<double>(pose_time) / frame_step_seconds;
    return result(static_cast<float>(std::floor(frames + .5) * frame_step_seconds));
}
}
