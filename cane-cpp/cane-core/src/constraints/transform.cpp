#include "solvers.hpp"
#include "transform_geometry.hpp"
#include <unordered_map>

namespace cane::constraints {
namespace tg = transform_geometry;
namespace {
using Mixes = std::array<float, 6>;
void maximum(std::optional<float>& result, double value) {
    const auto narrowed = static_cast<float>(value); if (std::isfinite(narrowed)) result = std::max(result.value_or(0), narrowed);
}
}
void solve_transform(Context& context, std::size_t index) {
    auto& pose = context.pose;
    const auto& d = std::get<detail::TransformDefinition>(pose.model().constraints.at(index).definition);
    if (!pose.active_constraints[index] || !pose.active_bones[d.target]) return;
    std::size_t remaining = 1000000;
    const auto charge = [&](std::size_t n) { if (n > remaining) throw Error(ErrorCode::resource_limit, "apply", "Transform work budget is exhausted."); remaining -= n; };
    const auto source_world = pose.world[d.target];
    const bool local_source = d.mapping ? d.mapping->local_source : d.local, local_target = d.mapping ? d.mapping->local_target : d.local;
    auto source = local_source ? tg::local_channels(pose.locals[d.target]) : tg::world_channels(source_world);
    const auto& offsets = pose.constraints[index].transform_offsets;
    for (std::size_t i = 0; i < 6; ++i) source[i] += offsets[i];
    if (!local_source) {
        source[1] = static_cast<double>(source_world.a) * offsets[1] + static_cast<double>(source_world.c) * offsets[2] + source_world.tx;
        source[2] = static_cast<double>(source_world.b) * offsets[1] + static_cast<double>(source_world.d) * offsets[2] + source_world.ty;
    }
    tg::check(source); tg::check_world(context.root);
    Mixes mixes{}, full{}; std::vector<tg::Route> routes;
    for (std::size_t i = 0; i < 6; ++i) {
        mixes[i] = pose.constraints[index].scalars[static_cast<std::size_t>(animation::ConstraintProperty::mix_rotate) + i];
        full[i] = mixes[i] == 0 ? 0.0f : 1.0f;
    }
    tg::check(mixes);
    if (!d.mapping) for (std::size_t i = 0; i < 6; ++i) { charge(1); routes.push_back({i, i, 0, 0, 1, {}}); }
    else for (const auto& from : d.mapping->properties) for (const auto& to : from.targets) {
        charge(1); routes.push_back({static_cast<std::size_t>(from.property), static_cast<std::size_t>(to.property), from.offset, to.offset, to.scale, d.mapping->clamp ? std::optional<double>(to.max) : std::nullopt});
    }
    tg::evaluate(source, {0, 0, 0, 1, 1, 0}, routes, {}, d.relative);
    std::unordered_map<std::size_t, BoneLocal> local_writes;
    std::unordered_map<std::size_t, Affine> world_writes;
    std::vector<std::vector<std::size_t>> children(pose.world.size());
    const auto& bones = pose.data().bones();
    for (std::size_t i = 0; i < bones.size(); ++i) if (bones[i].parent_index) children[*bones[i].parent_index].push_back(i);
    const auto world_at = [&](std::size_t bone) { const auto found = world_writes.find(bone); return found == world_writes.end() ? pose.world[bone] : found->second; };
    const auto local_at = [&](std::size_t bone) { const auto found = local_writes.find(bone); return found == local_writes.end() ? pose.locals[bone] : found->second; };
    const auto parent_at = [&](std::size_t bone) { return bones[bone].parent_index ? world_at(*bones[bone].parent_index) : context.root; };
    const auto mode_at = [&](std::size_t bone) { return bones[bone].parent_index ? pose.modes[bone] : TransformMode::inherit_all; };
    const auto candidate = [&](const BoneLocal& local, const Affine& world, const Affine& parent, TransformMode mode, const Mixes& amounts) -> std::pair<Affine, std::optional<BoneLocal>> {
        const auto current = local_target ? tg::local_channels(local) : tg::world_channels(world);
        const auto next = tg::evaluate(source, current, routes, amounts, d.relative);
        if (current == next) return {world, local};
        if (!local_target) return {tg::to_world(next), {}};
        const auto result = tg::to_local(next, local); const auto matrix = Affine::child(parent, result, mode); tg::check_world(matrix); return {matrix, result};
    };
    TransformDiagnostic diagnostic; diagnostic.mixes = mixes;
    for (const auto bone : d.bones) {
        if (bone == d.target) continue;
        charge(1 + 2 * routes.size());
        const auto local = local_at(bone); const auto world = world_at(bone), parent = parent_at(bone); const auto mode = mode_at(bone);
        const auto [next_world, projected_local] = candidate(local, world, parent, mode, mixes);
        const auto next_local = projected_local ? *projected_local : tg::same(next_world, world) ? local : tg::recover(next_world, parent, mode, local);
        ++diagnostic.driven;
        try {
            const auto ideal = candidate(local, world, parent, mode, full).first;
            if (mixes[1] != 0 || mixes[2] != 0) maximum(diagnostic.translation_residual, std::hypot(mixes[1] == 0 ? 0 : static_cast<double>(next_world.tx) - ideal.tx, mixes[2] == 0 ? 0 : static_cast<double>(next_world.ty) - ideal.ty));
            if (mixes[0] != 0) maximum(diagnostic.rotation_residual, std::abs(tg::wrap(static_cast<double>(x_angle(ideal)) - x_angle(next_world))));
            if (mixes[3] != 0 || mixes[4] != 0) maximum(diagnostic.scale_residual, std::max(mixes[3] == 0 ? 0 : std::abs(static_cast<double>(x_scale(next_world)) - x_scale(ideal)), mixes[4] == 0 ? 0 : std::abs(static_cast<double>(y_scale(next_world)) - y_scale(ideal))));
            if (mixes[5] != 0) maximum(diagnostic.shear_residual, std::abs(tg::wrap(static_cast<double>(static_cast<float>(tg::world_channels(ideal)[5])) - static_cast<float>(tg::world_channels(next_world)[5]))));
        } catch (const Error&) { }
        if (tg::same(next_world, world) && tg::local_channels(next_local) == tg::local_channels(local)) continue;
        local_writes[bone] = next_local; world_writes[bone] = next_world;
        auto queue = children[bone];
        for (std::size_t cursor = 0; cursor < queue.size(); ++cursor) {
            charge(1); const auto child = queue[cursor]; const auto matrix = Affine::child(parent_at(child), local_at(child), mode_at(child)); tg::check_world(matrix);
            world_writes[child] = matrix; queue.insert(queue.end(), children[child].begin(), children[child].end());
        }
    }
    for (const auto& [bone, local] : local_writes) pose.locals[bone] = local;
    for (const auto& [bone, world] : world_writes) pose.world[bone] = world;
    pose.diagnostics[index] = diagnostic;
}
std::optional<std::array<float, 6>> matched_transform_offsets(const Context& context, std::size_t index, ErrorCode* failure_code) {
    if (failure_code) *failure_code = ErrorCode::invalid_state;
    const auto& pose = context.pose; const auto& d = std::get<detail::TransformDefinition>(pose.model().constraints.at(index).definition);
    bool local_source = d.local, local_target = d.local;
    if (d.mapping) {
        const auto& mapping = *d.mapping; if (mapping.clamp || mapping.properties.size() != 6) return {};
        std::array<bool, 6> seen{};
        for (const auto& source : mapping.properties) {
            const auto p = static_cast<std::size_t>(source.property);
            if (seen[p] || source.offset != 0 || source.targets.size() != 1) return {};
            const auto& target = source.targets[0]; if (target.property != source.property || target.offset != 0 || target.scale != 1) return {};
            seen[p] = true;
        }
        local_source = mapping.local_source; local_target = mapping.local_target;
    }
    try {
        const auto values = [&](std::size_t bone, bool local) { return local ? tg::local_channels(pose.locals[bone]) : tg::world_channels(pose.world[bone]); };
        const auto desired = d.relative ? tg::Channels{0, 0, 0, 1, 1, 0} : values(d.bones.front(), local_target);
        const auto source = values(d.target, local_source); tg::Channels offsets{};
        for (std::size_t i = 0; i < 6; ++i) offsets[i] = desired[i] - source[i];
        if (!d.relative) { offsets[0] = tg::wrap(offsets[0]); offsets[5] = tg::wrap(offsets[5]); }
        if (!local_source) { const auto point = tg::inverse_point(pose.world[d.target], {desired[1], desired[2]}); offsets[1] = point.x; offsets[2] = point.y; }
        std::array<float, 6> result{}; for (std::size_t i = 0; i < 6; ++i) result[i] = tg::narrow(offsets[i]); return result;
    } catch (const Error&) { if (failure_code) *failure_code = ErrorCode::non_finite; return {}; }
}
}
