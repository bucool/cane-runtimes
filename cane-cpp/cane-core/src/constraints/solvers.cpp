#include "solvers.hpp"
#include "transform_geometry.hpp"

namespace cane::constraints {
float x_angle(const Affine& m) noexcept { return static_cast<float>(std::atan2(static_cast<double>(m.b), m.a)) * radians_to_degrees; }
float y_angle(const Affine& m) noexcept { return static_cast<float>(std::atan2(static_cast<double>(m.d), m.c)) * radians_to_degrees; }
float x_scale(const Affine& m) noexcept { return cane::hypot(m.a, m.b); }
float y_scale(const Affine& m) noexcept { return cane::hypot(m.c, m.d); }
float world_shear_y(const Affine& m) noexcept { return constraint_degrees(static_cast<float>(std::atan2(static_cast<double>(-m.c), m.d)) * radians_to_degrees - x_angle(m)); }
Affine rotate_axes(const Affine& m, float degrees) noexcept {
    const auto radians = degrees * degrees_to_radians;
    const auto co = static_cast<float>(std::cos(static_cast<double>(radians))), si = static_cast<float>(std::sin(static_cast<double>(radians)));
    return {co * m.a - si * m.b, si * m.a + co * m.b, co * m.c - si * m.d, si * m.c + co * m.d, m.tx, m.ty};
}
std::optional<BoneLocal> reconstruct_local(const BoneLocal& previous, const Affine& world, const Affine& parent, TransformMode mode) {
    try { return transform_geometry::recover(world, parent, mode, previous); } catch (const Error&) { return {}; }
}
Affine Context::parent(std::size_t bone) const {
    const auto index = pose.data().bones().at(bone).parent_index; return index ? pose.world[*index] : root;
}
Affine Context::compose(std::size_t bone, const BoneLocal& local) const {
    return pose.data().bones()[bone].parent_index ? Affine::child(parent(bone), local, pose.modes[bone]) : root * Affine::from_local(local);
}
void Context::refresh_descendants(std::size_t bone) {
    auto& affected = scratch.affected; std::fill(affected.begin(), affected.end(), 0); affected.at(bone) = 1;
    const auto& bones = pose.data().bones();
    for (std::size_t i = bone; i < bones.size(); ++i) {
        if (i != bone && bones[i].parent_index && affected[*bones[i].parent_index]) {
            affected[i] = 1; pose.world[i] = Affine::child(pose.world[*bones[i].parent_index], pose.locals[i], pose.modes[i]);
        }
        if (affected[i] && !pose.world[i].finite()) throw Error(ErrorCode::non_finite, "apply", "Constraint hierarchy is non-finite.", "bones", bones[i].id);
    }
}
void Context::write_local(std::size_t bone, const BoneLocal& local) { pose.locals[bone] = local; pose.world[bone] = compose(bone, local); refresh_descendants(bone); }
void Context::set_world(std::size_t bone, const Affine& world) {
    if (!world.finite()) throw Error(ErrorCode::non_finite, "apply", "Constraint world matrix is non-finite.", "bones", pose.data().bones()[bone].id);
    pose.world[bone] = world;
    const auto local = reconstruct_local(pose.locals[bone], world, parent(bone), pose.data().bones()[bone].parent_index ? pose.modes[bone] : TransformMode::inherit_all);
    if (local) pose.locals[bone] = *local;
}
void Context::write_world(std::size_t bone, const Affine& world) { set_world(bone, world); refresh_descendants(bone); }
}
