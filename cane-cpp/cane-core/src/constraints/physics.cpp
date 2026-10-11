#include "physics.hpp"
#include "solvers.hpp"
#include "damped_spring.hpp"
#include <tuple>

namespace cane::constraints {
namespace {
constexpr double tau = 6.283185307179586476925286766559;
double wrap_radians(double angle) {
    auto value = std::fmod(angle + tau * .5, tau); if (value < 0) value += tau; return value - tau * .5;
}
Affine rotate_basis(const Affine& m, float angle, bool first_only) {
    const float co = static_cast<float>(std::cos(static_cast<double>(angle))), si = static_cast<float>(std::sin(static_cast<double>(angle)));
    return {co * m.a - si * m.b, si * m.a + co * m.b,
        first_only ? m.c : co * m.c - si * m.d, first_only ? m.d : si * m.c + co * m.d, m.tx, m.ty};
}
}
void solve_physics(Context& context, std::size_t index, PhysicsStore& store, const PhysicsStep& input, PhysicsBudget& budget) {
    using P = animation::ConstraintProperty;
    auto& pose = context.pose; pose.diagnostics.at(index) = std::monostate{};
    const auto& d = std::get<detail::PhysicsDefinition>(pose.model().constraints.at(index).definition);
    const auto p = pose.constraints[index]; const float mix = p[P::mix], reference = pose.data().skeleton().reference_scale;
    if (!pose.active_constraints[index] || !pose.active_bones[d.bone] || mix == 0 || p.physics.fps == 0
        || !std::isfinite(reference) || reference <= 0 || !input.environment.finite()) return;
    const auto& id = pose.data().catalog(RuntimeCatalogKind::constraint)[index].id;
    const auto failure = [&] { return Error(ErrorCode::non_finite, "apply", "Physics state or geometry cannot be represented.", {}, id); };
    auto state = store.state(index).value_or(PhysicsState{});
    const float delta = std::isfinite(input.delta_seconds) ? std::max(input.delta_seconds, 0.0f) : 0;
    const auto current = pose.world[d.bone]; auto matrix = current;
    const double length = std::max(pose.data().bones()[d.bone].length, 0.0f), axis_x = length * current.a, axis_y = length * current.b;
    const bool xe = p.physics.x > 0, ye = p.physics.y > 0, re = p.physics.rotate > 0 || p.physics.shear_x > 0, se = p.physics.scale_x > 0;
    const double count_value = !state.reset && (xe || ye || re || se) && delta > 0
        ? std::ceil(static_cast<float>(static_cast<double>(delta) * p.physics.fps)) : 0;
    if (!std::isfinite(count_value) || count_value > budget.remaining()) throw Error(ErrorCode::resource_limit, "apply", "Physics fixed-step budget is exhausted.");
    const auto count = static_cast<std::uint32_t>(count_value);
    PhysicsDiagnostic diagnostic; diagnostic.fixed_steps = count;
    diagnostic.configured_limit = std::max(p.physics.limit, 0.0f); diagnostic.required_limit = 0.0f;
    const auto observe = [&](double motion, double limit, float root_scale) {
        diagnostic.limit_saturated = diagnostic.limit_saturated || std::abs(motion) > limit;
        const float rounded = std::abs(static_cast<float>(motion)); if (rounded == 0 || !diagnostic.required_limit) return;
        const float denominator = delta * std::abs(root_scale), candidate = rounded / denominator;
        if (denominator == 0 || !std::isfinite(candidate)) diagnostic.required_limit.reset();
        else diagnostic.required_limit = std::max(*diagnostic.required_limit, candidate);
    };
    if (!state.reset) {
        const double inertia = std::clamp(p[P::inertia], 0.0f, 1.0f), limit = diagnostic.configured_limit;
        const double motion_x = (current.tx - state.rest_x) * inertia, motion_y = (current.ty - state.rest_y) * inertia;
        const double limit_x = limit * delta * std::abs(input.root_scale_x), limit_y = limit * delta * std::abs(input.root_scale_y);
        if (xe || re || se) observe(motion_x, limit_x, input.root_scale_x);
        if (ye || re || se) observe(motion_y, limit_y, input.root_scale_y);
        const double pivot_x = std::clamp(motion_x, -limit_x, limit_x), pivot_y = std::clamp(motion_y, -limit_y, limit_y);
        if (xe) state.x_offset -= pivot_x;
        if (ye) state.y_offset -= pivot_y;
        const double axis_length = std::hypot(axis_x, axis_y), previous_length = std::hypot(state.axis_x, state.axis_y);
        if (axis_length > 0 && previous_length > 0) {
            const double ux = axis_x / axis_length, uy = axis_y / axis_length;
            if (re) state.angle_offset -= inertia * wrap_radians(std::atan2(axis_y, axis_x) - std::atan2(state.axis_y, state.axis_x))
                + (ux * pivot_y - uy * pivot_x) / axis_length;
            if (se) state.log_stretch -= inertia * (std::log(axis_length) - std::log(previous_length)) + (ux * pivot_x + uy * pivot_y) / axis_length;
        }
        const double mass = p[P::mass]; if (!std::isfinite(mass) || mass <= 0) throw failure();
        const double stiffness = std::max(p[P::strength], 0.0f) / mass, drag = 60 * (1 - static_cast<double>(std::clamp(p[P::damping], 0.0f, 1.0f)));
        const auto& env = input.environment;
        const double force_x = static_cast<double>(reference) * (static_cast<double>(p[P::wind]) * env.wind_x + static_cast<double>(p[P::gravity]) * env.gravity_x) * input.root_scale_x;
        const double force_y = -static_cast<double>(reference) * (static_cast<double>(p[P::wind]) * env.wind_y + static_cast<double>(p[P::gravity]) * env.gravity_y) * input.root_scale_y;
        const double ax = force_x / mass, ay = force_y / mass;
        const double angular = (axis_x * force_y - axis_y * force_x) / (static_cast<double>(reference) * reference * mass);
        const double axial = (axis_x * force_x + axis_y * force_y) / (static_cast<double>(reference) * reference * mass);
        const double interval = count > 0 ? static_cast<double>(delta) / count : 0;
        for (std::uint32_t step = 0; step < count; ++step) {
            if (xe) std::tie(state.x_offset, state.x_velocity) = advance_spring(state.x_offset, state.x_velocity, interval, ax, stiffness, drag);
            if (ye) std::tie(state.y_offset, state.y_velocity) = advance_spring(state.y_offset, state.y_velocity, interval, ay, stiffness, drag);
            if (re) std::tie(state.angle_offset, state.angle_velocity) = advance_spring(state.angle_offset, state.angle_velocity, interval, angular, stiffness, drag);
            if (se) std::tie(state.log_stretch, state.stretch_velocity) = advance_spring(state.log_stretch, state.stretch_velocity, interval, axial, stiffness, drag);
        }
    }
    state.reset = false; state.rest_x = current.tx; state.rest_y = current.ty; state.axis_x = axis_x; state.axis_y = axis_y;
    if (!xe) state.x_offset = state.x_velocity = 0;
    if (!ye) state.y_offset = state.y_velocity = 0;
    if (!re) state.angle_offset = state.angle_velocity = 0;
    if (!se) state.log_stretch = state.stretch_velocity = 0;
    matrix.tx = static_cast<float>(current.tx + state.x_offset * mix * p.physics.x);
    matrix.ty = static_cast<float>(current.ty + state.y_offset * mix * p.physics.y);
    if (p.physics.rotate > 0) matrix = rotate_basis(matrix, static_cast<float>(state.angle_offset * mix * p.physics.rotate), false);
    if (p.physics.shear_x > 0) matrix = rotate_basis(matrix, static_cast<float>(state.angle_offset * mix * p.physics.shear_x), true);
    if (se) {
        const double log = state.log_stretch * mix * p.physics.scale_x, factor = std::exp(log);
        const double y_factor = d.scale_y == detail::ScaleYMode::uniform ? factor : d.scale_y == detail::ScaleYMode::volume ? std::exp(-log) : 1;
        matrix.a = static_cast<float>(matrix.a * factor); matrix.b = static_cast<float>(matrix.b * factor);
        matrix.c = static_cast<float>(matrix.c * y_factor); matrix.d = static_cast<float>(matrix.d * y_factor);
    }
    if (!state.finite() || !matrix.finite()) throw failure();
    diagnostic.translation_offset = static_cast<float>(std::hypot(state.x_offset, state.y_offset));
    diagnostic.translation_speed = static_cast<float>(std::hypot(state.x_velocity, state.y_velocity));
    diagnostic.rotation_offset_degrees = static_cast<float>(state.angle_offset * (360 / tau));
    diagnostic.angular_speed_degrees = static_cast<float>(state.angle_velocity * (360 / tau));
    diagnostic.scale_offset = static_cast<float>(std::exp(state.log_stretch) - 1);
    diagnostic.scale_speed = static_cast<float>(std::exp(state.log_stretch) * state.stretch_velocity);
    for (const auto v : {diagnostic.translation_offset, diagnostic.translation_speed, diagnostic.rotation_offset_degrees,
        diagnostic.angular_speed_degrees, diagnostic.scale_offset, diagnostic.scale_speed, diagnostic.configured_limit}) if (!std::isfinite(v)) throw failure();
    try { context.write_world(d.bone, matrix); } catch (const Error& error) { if (error.code == ErrorCode::non_finite) throw failure(); throw; }
    store.commit(index, state); budget.consume(count); pose.diagnostics[index] = diagnostic;
}
}
