#include "animation/sequence_clock.hpp"
#include "physics.hpp"
#include <tuple>
#include "runtime_data_internal.hpp"

namespace cane::constraints {
bool PhysicsEnvironment::finite() const noexcept { return std::isfinite(wind_x) && std::isfinite(wind_y) && std::isfinite(gravity_x) && std::isfinite(gravity_y); }
bool PhysicsState::finite() const noexcept {
    for (const auto value : {rest_x, rest_y, axis_x, axis_y, x_offset, y_offset, x_velocity, y_velocity, angle_offset, angle_velocity, log_stretch, stretch_velocity}) if (!std::isfinite(value)) return false;
    return true;
}
PhysicsState PhysicsState::transported(const Affine& delta) const {
    constexpr double tau = 6.283185307179586476925286766559;
    const auto vector = [&](double x, double y) -> std::pair<double, double> {
        return {delta.a * x + delta.c * y, delta.b * x + delta.d * y};
    };
    auto next = *this;
    std::tie(next.x_offset, next.y_offset) = vector(x_offset, y_offset);
    std::tie(next.x_velocity, next.y_velocity) = vector(x_velocity, y_velocity);
    if (!reset) {
        const auto [ox, oy] = vector(rest_x, rest_y); const auto [ax, ay] = vector(axis_x, axis_y);
        const double si = std::sin(angle_offset), co = std::cos(angle_offset), scale = std::exp(log_stretch);
        const double dx = scale * (co * axis_x - si * axis_y), dy = scale * (si * axis_x + co * axis_y);
        const auto [mx, my] = vector(dx, dy);
        const auto [vx, vy] = vector(dx * stretch_velocity - dy * angle_velocity, dy * stretch_velocity + dx * angle_velocity);
        const double rest_length = std::hypot(ax, ay), moved_length = std::hypot(mx, my);
        if (rest_length > 0 && moved_length > 0) {
            const double ux = mx / moved_length, uy = my / moved_length;
            double principal = std::fmod(std::atan2(my, mx) - std::atan2(ay, ax) + tau * .5, tau);
            if (principal < 0) principal += tau;
            principal -= tau * .5;
            const double orientation = static_cast<double>(delta.a) * delta.d - static_cast<double>(delta.b) * delta.c < 0 ? -1 : 1;
            const double turns = std::round((orientation * angle_offset - principal) / tau);
            next.angle_offset = principal + turns * tau;
            next.angle_velocity = (ux * vy - uy * vx) / moved_length;
            next.log_stretch = std::log(moved_length) - std::log(rest_length);
            next.stretch_velocity = (ux * vx + uy * vy) / moved_length;
        } else next.angle_offset = next.angle_velocity = next.log_stretch = next.stretch_velocity = 0;
        next.rest_x = ox + delta.tx; next.rest_y = oy + delta.ty; next.axis_x = ax; next.axis_y = ay;
    }
    if (!next.finite()) throw Error(ErrorCode::non_finite, "setRootTransform", "Physics history transport cannot be represented.");
    return next;
}
void PhysicsBudget::consume(std::uint32_t count) {
    if (count > remaining_) throw Error(ErrorCode::resource_limit, "apply", "Physics fixed-step budget is exhausted.");
    remaining_ -= count;
}
bool PhysicsStore::reset(std::optional<std::size_t> constraint) {
    if (constraint) { auto& value = states_.at(*constraint); const auto had = value.has_value(); if (had) value = PhysicsState{}; return had; }
    bool had = false; for (auto& value : states_) { had = had || value.has_value(); value.reset(); }
    animation_.reset(); raw_time_.reset(); sampled_time_.reset(); range_start_.reset(); return had;
}
PhysicsStore PhysicsStore::reconcile(const RuntimeData& previous, const RuntimeData& effective) const {
    const auto& old = detail::RuntimeDataAccess::get(previous); const auto& next = detail::RuntimeDataAccess::get(effective);
    PhysicsStore result(next.model.constraints.size());
    if (animation_) {
        const auto at = next.index.catalogs[10].find(old.clips.at(*animation_).id);
        if (at == next.index.catalogs[10].end()) return result;
        result.animation_ = at->second;
    }
    result.raw_time_ = raw_time_; result.sampled_time_ = sampled_time_; result.range_start_ = range_start_; result.range_end_ = range_end_; result.looping_ = looping_;
    for (std::size_t i = 0; i < old.model.constraints.size(); ++i) {
        const auto& before = old.model.constraints[i];
        if (before.kind != animation::ConstraintKind::physics) continue;
        const auto at = next.index.catalogs[7].find(old.catalogs[7][i].id);
        if (at == next.index.catalogs[7].end()) continue;
        const auto& after = next.model.constraints[at->second];
        if (after.kind != animation::ConstraintKind::physics) continue;
        const auto old_bone = std::get<detail::PhysicsDefinition>(before.definition).bone;
        const auto new_bone = std::get<detail::PhysicsDefinition>(after.definition).bone;
        if (old.bones[old_bone].id == next.bones[new_bone].id) result.states_[at->second] = states_[i];
    }
    return result;
}
float PhysicsStore::advance(const animation::Pose& pose, const PhysicsClockSample& clock) {
    for (const auto value : {clock.raw_time, clock.sampled_time, clock.range_start, clock.range_end, clock.duration})
        if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, "apply", "Physics clock sample must be finite.");
    const auto& clips = detail::RuntimeDataAccess::get(pose.data()).clips;
    if (clock.animation && *clock.animation >= clips.size()) throw Error(ErrorCode::invalid_argument, "apply", "Physics primary animation is outside its catalog.");
    const auto changed = animation_ != clock.animation || range_start_ != clock.range_start || range_end_ != clock.range_end || looping_ != clock.looping || (sampled_time_ && clock.sampled_time < *sampled_time_);
    const auto previous_sampled = changed ? std::optional<float>{} : sampled_time_;
    const auto delta = std::max(previous_sampled ? clock.sampled_time - *previous_sampled : clock.sampled_time, 0.0f);
    if (!std::isfinite(delta)) throw Error(ErrorCode::non_finite, "apply", "Physics elapsed time overflowed.");
    if (changed) (void)reset();
    animation_ = clock.animation; raw_time_ = clock.raw_time; sampled_time_ = clock.sampled_time; range_start_ = clock.range_start; range_end_ = clock.range_end; looping_ = clock.looping;
    if (!clock.animation || !previous_sampled || clock.sampled_time <= *previous_sampled || clock.reset_weight < .5) return delta;
    const auto& model = pose.model();
    for (const auto& timeline : clips[*clock.animation].constraints) {
        if (timeline.kind != animation::ConstraintKind::physics) continue;
        bool crossed = false;
        for (const auto& trigger : timeline.reset_triggers) {
            if (!std::isfinite(trigger.time) || trigger.time < clock.range_start || trigger.time > clock.range_end) continue;
            const float key = trigger.time == clock.range_end ? clock.duration : trigger.time - clock.range_start;
            crossed = clock.looping && clock.duration > 0
                ? animation::periodic_reset_crossed(*previous_sampled,clock.sampled_time,key,clock.duration)
                : key > *previous_sampled && key <= clock.sampled_time;
            if (crossed) break;
        }
        if (!crossed) continue;
        for (std::size_t i = 0; i < model.constraints.size(); ++i) {
            if (timeline.index && *timeline.index != i) continue;
            if (model.constraints[i].kind != animation::ConstraintKind::physics || !pose.active_constraints[i]) continue;
            const auto bone = std::get<detail::PhysicsDefinition>(model.constraints[i].definition).bone;
            if (pose.active_bones[bone]) (void)reset(i);
        }
    }
    return delta;
}
void PhysicsStore::host_motion(const animation::Pose& pose, const Affine& previous, const Affine& next, PhysicsHostMotion mode, std::optional<std::size_t> constraint) {
    if (constraint && (*constraint >= pose.model().constraints.size() || pose.model().constraints[*constraint].kind != animation::ConstraintKind::physics))
        throw Error(ErrorCode::invalid_argument, "setRootTransform", "Host motion target must be a Physics constraint.", "constraintId");
    if (mode == PhysicsHostMotion::move) return;
    if (mode == PhysicsHostMotion::teleport) {
        for (std::size_t i = 0; i < states_.size(); ++i) if ((!constraint || i == *constraint) && states_[i]) states_[i] = PhysicsState{};
        return;
    }
    if (mode != PhysicsHostMotion::preserve_inertia && mode != PhysicsHostMotion::clear_inertia)
        throw Error(ErrorCode::invalid_argument, "setRootTransform", "Unknown Physics host motion mode.", "physicsMode");
    const auto determinant = previous.determinant();
    if (determinant == 0 || !std::isfinite(determinant)) throw Error(ErrorCode::invalid_state, "setRootTransform", "Physics history transport requires an invertible previous root.", "rootTransform");
    const auto inverse = previous.inverse();
    if (!inverse) throw Error(ErrorCode::non_finite, "setRootTransform", "Previous root inverse is non-finite.", "rootTransform");
    const auto delta = next * *inverse;
    if (!delta.finite()) throw Error(ErrorCode::non_finite, "setRootTransform", "Physics root transport is non-finite.", "rootTransform");
    transported_ = states_;
    for (std::size_t i = 0; i < transported_.size(); ++i) {
        if ((constraint && i != *constraint) || !transported_[i]) continue;
        auto& s = *transported_[i];
        s = s.transported(delta);
        if (mode == PhysicsHostMotion::clear_inertia) s.x_velocity = s.y_velocity = s.angle_velocity = s.stretch_velocity = 0;
    }
    states_.swap(transported_);
}
}
