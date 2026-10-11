#include "solvers.hpp"
#include "ik_chain.hpp"
#include <unordered_map>
#include <unordered_set>

namespace cane::constraints {
void solve_ik(Context& context, std::size_t index) {
    using P = animation::ConstraintProperty;
    auto& pose = context.pose;
    const auto& definition = std::get<detail::IkDefinition>(pose.model().constraints.at(index).definition);
    const auto& values = pose.constraints[index];
    const auto target_bone = values.explicit_ik_target ? std::nullopt : definition.target;
    if (!pose.active_constraints[index] || (target_bone && !pose.active_bones[*target_bone])) return;
    const auto& chain = definition.bones; if (chain.empty()) return;
    const auto& bones = pose.data().bones(); std::unordered_set<std::size_t> seen;
    std::vector<BoneLocal> source; std::vector<float> lengths; std::vector<TransformMode> modes;
    for (std::size_t i = 0; i < chain.size(); ++i) {
        const auto bone = chain[i]; if (bone >= bones.size() || !seen.insert(bone).second || (i > 0 && bones[bone].parent_index != chain[i - 1])) return;
        source.push_back(pose.locals[bone]); lengths.push_back(bones[bone].length); modes.push_back(pose.modes[bone]);
    }
    const ik_geometry::Vector target = target_bone ? ik_geometry::Vector{pose.world[*target_bone].tx, pose.world[*target_bone].ty} : ik_geometry::Vector{values[P::target_x], values[P::target_y]};
    const auto mix = std::isfinite(values[P::mix]) ? std::clamp(values[P::mix], 0.0f, 1.0f) : 0;
    const auto threshold = std::isfinite(definition.threshold) ? std::max(0.0f, definition.threshold) : 0;
    const auto transverse = definition.uniform == detail::ScaleYMode::uniform ? ik_geometry::Transverse::uniform : definition.uniform == detail::ScaleYMode::volume ? ik_geometry::Transverse::area : ik_geometry::Transverse::fixed;
    const ik_solver::Options options{definition.iterations, threshold, mix, values[P::softness], values.booleans[0], values.booleans[1], values.booleans[2], transverse};
    const auto parent_index = bones[chain[0]].parent_index;
    const std::optional<Affine> parent = parent_index ? std::optional<Affine>(pose.world[*parent_index]) : std::nullopt;
    auto result = ik_solver::Chain(source, lengths, modes, parent, context.root, options).solve(target);
    const IkDiagnostic diagnostic{result.residual, threshold, mix, result.iterations, definition.iterations, mix >= .999f && result.residual > std::max(threshold, .0001f)};
    if (mix != 0) {
        std::unordered_map<std::size_t, BoneLocal> locals; std::unordered_map<std::size_t, Affine> worlds;
        auto affected = seen;
        for (std::size_t i = 0; i < chain.size(); ++i) locals.emplace(chain[i], result.poses[i]);
        for (std::size_t bone = 0; bone < bones.size(); ++bone) {
            const auto bone_parent = bones[bone].parent_index; if (bone_parent && affected.count(*bone_parent)) affected.insert(bone); if (!affected.count(bone)) continue;
            const auto local = locals.count(bone) ? locals.at(bone) : pose.locals[bone];
            const auto outer = bone_parent ? worlds.count(*bone_parent) ? worlds.at(*bone_parent) : pose.world[*bone_parent] : context.root;
            const auto world = Affine::child(outer, local, bone_parent ? pose.modes[bone] : TransformMode::inherit_all);
            if (!world.finite()) throw ik_geometry::failure(); worlds.emplace(bone, world);
        }
        for (const auto& [bone, local] : locals) pose.locals[bone] = local;
        for (const auto& [bone, world] : worlds) pose.world[bone] = world;
    }
    pose.diagnostics[index] = diagnostic;
}
}
