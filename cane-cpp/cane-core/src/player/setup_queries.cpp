#include "state.hpp"
#include "transform_geometry.hpp"
#include <unordered_map>

namespace cane {
std::vector<BonePose> RuntimePlayer::setup_bone_frame(const std::vector<SetupBone>& bones) {
    constexpr auto op = "querySetupBones";
    std::unordered_map<std::string, std::size_t> ids;
    std::vector<BonePose> output(bones.size()); std::vector<unsigned char> state(bones.size());
    for (std::size_t i = 0; i < bones.size(); ++i) {
        const auto& b = bones[i]; detail::validate_local(b.local, op, "local");
        if (b.id.empty() || !ids.emplace(b.id, i).second) throw Error(ErrorCode::validation_failed, op, "Invalid or duplicate setup bone.");
    }
    for (std::size_t start = 0; start < bones.size(); ++start) {
        if (state[start] == 2) continue;
        std::vector<std::size_t> path; auto cursor = start;
        while (cursor < bones.size() && state[cursor] != 2) {
            const auto& b = bones[cursor];
            if (state[cursor] == 1) throw Error(ErrorCode::validation_failed, op, "Setup hierarchy contains a cycle.");
            state[cursor] = 1; path.push_back(cursor);
            if (!b.parent_id) cursor = bones.size();
            else { const auto parent = ids.find(*b.parent_id); if (parent == ids.end()) throw Error(ErrorCode::validation_failed, op, "Missing setup parent."); cursor = parent->second; }
        }
        for (auto it = path.rbegin(); it != path.rend(); ++it) {
            const auto index = *it; const auto& b = bones[index];
            const auto world = b.parent_id ? Affine::child(output[ids.at(*b.parent_id)].matrix, b.local, b.transform_mode) : Affine::from_local(b.local);
            transform_geometry::check_world(world); output[index] = {b.id, world, b.local, b.transform_mode, true}; state[index] = 2;
        }
    }
    return output;
}
BoneLocal RuntimePlayer::solve_bone_local_for_world(std::optional<Affine> parent, const Affine& world, TransformMode mode, const BoneLocal& reference) {
    constexpr auto op = "solveBoneLocalForWorld"; detail::validate_local(reference, op, "local");
    if (!world.finite() || (parent && !parent->finite())) throw Error(ErrorCode::invalid_argument, op, "Affine components must be finite.");
    try { return transform_geometry::recover(world, parent.value_or(Affine{}), parent ? mode : TransformMode::inherit_all, reference); }
    catch (const Error&) { throw Error(ErrorCode::invalid_state, op, "Requested world pose is unreachable under the selected parent and inheritance mode."); }
}
}
