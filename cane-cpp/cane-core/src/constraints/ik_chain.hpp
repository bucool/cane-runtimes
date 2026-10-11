#pragma once
#include "ik_search.hpp"

namespace cane::ik_solver {
namespace ig = ik_geometry;
namespace tg = transform_geometry;
struct Options {
    std::uint32_t iterations;
    float threshold, mix;
    double softness;
    bool bend_positive, compress, stretch;
    ig::Transverse transverse;
};
struct Outcome { std::vector<BoneLocal> poses; std::uint32_t iterations; float residual; };
struct Evaluation { ig::Vector tip, pivot; double reach, bend; };
class Chain {
    std::vector<BoneLocal> source_;
    std::vector<float> lengths_;
    std::vector<TransformMode> modes_;
    std::optional<Affine> parent_;
    Affine root_;
    Options options_;
    std::size_t remaining_ = 1000000;
    Affine outer() const { return parent_.value_or(root_); }
    bool ordinary() const {
        for (std::size_t i = 0; i < modes_.size(); ++i) if (!(i == 0 && !parent_) && modes_[i] != TransformMode::inherit_all) return false; return true;
    }
    Evaluation evaluate(const std::vector<BoneLocal>& poses) {
        if (poses.empty() || poses.size() != lengths_.size() || poses.size() != modes_.size()) throw ig::failure();
        auto parent = parent_; std::vector<ig::Vector> origins; origins.reserve(poses.size());
        for (std::size_t i = 0; i < poses.size(); ++i) {
            if (remaining_ == 0) throw Error(ErrorCode::resource_limit, "apply", "IK bone evaluation budget is exhausted."); --remaining_;
            const auto world = Affine::child(parent.value_or(root_), poses[i], parent ? modes_[i] : TransformMode::inherit_all);
            if (!world.finite()) throw ig::failure(); origins.push_back({world.tx, world.ty}); parent = world;
        }
        const auto point = parent->transform({lengths_.back(), 0}); tg::check({point.x, point.y}); const ig::Vector tip{point.x, point.y};
        double reach = 0; for (std::size_t i = 1; i < origins.size(); ++i) reach += ig::sub(origins[i], origins[i - 1]).length();
        reach += ig::sub(tip, origins.back()).length();
        const auto bend = origins.size() == 2 ? ig::cross(ig::sub(origins[1], origins[0]), ig::sub(tip, origins[1])) : 0;
        return {tip, origins[0], reach, bend};
    }
    ik_search::Observation observe(const Evaluation& value, ig::Vector target) const {
        const auto error = ig::sub(value.tip, target); return {error.length(), error, value.bend == 0 || (value.bend > 0) == options_.bend_positive};
    }
    static bool agrees(ig::Vector expected, const Evaluation& observed) {
        const auto extent = std::max({std::abs(expected.x), std::abs(expected.y), observed.reach});
        return ig::sub(expected, observed.tip).length() <= 64 * static_cast<double>(std::numeric_limits<float>::epsilon()) * extent + 2 * static_cast<double>(std::numeric_limits<float>::denorm_min());
    }
    struct Parameters { std::vector<BoneLocal> poses; double factor; };
    Parameters parameters(const std::vector<BoneLocal>& basis, const std::vector<double>& values) const {
        auto poses = basis; for (std::size_t i = 0; i < poses.size(); ++i) poses[i].rotation_degrees = tg::narrow(values[i]);
        const auto factor = values.size() > basis.size() ? std::exp(values[basis.size()]) : 1;
        const auto scale = ig::scaled(basis[0], factor, 1, options_.transverse); poses[0].scale_x = scale.scale_x; poses[0].scale_y = scale.scale_y; return {poses, factor};
    }
    ik_search::Bound scale_bounds(BoneLocal pose) const {
        const auto min = std::log(static_cast<double>(std::numeric_limits<float>::denorm_min())), max = std::log(static_cast<double>(std::numeric_limits<float>::max()));
        auto low = min - max, high = max - min;
        for (unsigned i = 0; i < 2; ++i) {
            const auto axis = i == 0 ? pose.scale_x : pose.scale_y; if (axis == 0 || (i == 1 && options_.transverse == ig::Transverse::fixed)) continue;
            const auto log = std::log(std::abs(static_cast<double>(axis))); const auto inverse = i == 1 && options_.transverse == ig::Transverse::area;
            low = std::max(low, inverse ? log - max : min - log); high = std::min(high, inverse ? log - min : max - log);
        }
        return {options_.compress ? std::min(low, 0.0) : 0, options_.stretch ? std::max(high, 0.0) : 0};
    }
    struct Search { std::vector<BoneLocal> poses; double factor; std::uint32_t passes; };
    Search search(const std::vector<BoneLocal>& basis, const std::vector<BoneLocal>& seed, ig::Vector target, bool scale) {
        const auto count = basis.size(); std::vector<double> values; std::vector<ik_search::Bound> bounds(count, {-180, 180});
        for (const auto& pose : seed) values.push_back(pose.rotation_degrees);
        if (scale) { values.push_back(0); bounds.push_back(scale_bounds(basis[0])); }
        const auto result = ik_search::minimize(values, bounds, count, count <= 2 ? std::max(options_.iterations, std::uint32_t{1}) : options_.iterations, options_.threshold,
            [&](const std::vector<double>& values) { return observe(evaluate(parameters(basis, values).poses), target); });
        auto prepared = parameters(basis, result.parameters); return {std::move(prepared.poses), prepared.factor, result.passes};
    }
    std::optional<Parameters> single(BoneLocal basis, ig::Vector target) {
        const auto v = ig::inverse_target(outer(), basis, target); if (!v) return {};
        const auto length = static_cast<double>(lengths_[0]) * basis.scale_x, requested = v->length(); if (length == 0) return Parameters{{basis}, 1};
        const auto ratio = requested / std::abs(length), factor = (ratio < 1 && options_.compress) || (ratio > 1 && options_.stretch) ? ratio : 1;
        auto pose = ig::scaled(basis, factor, 1, options_.transverse);
        if (requested != 0) pose.rotation_degrees = tg::narrow(tg::wrap(std::atan2(v->y, v->x) * (180 / ig::pi) - basis.shear_x_degrees - (length < 0 ? 180 : 0)));
        const auto observed = evaluate({pose}); if (requested == 0) return Parameters{{pose}, factor};
        const auto unit = std::abs(length) * factor / requested;
        const auto expected = ig::transform(outer(), {basis.x + v->x * unit, basis.y + v->y * unit});
        return agrees(expected, observed) ? std::optional<Parameters>(Parameters{{pose}, factor}) : std::nullopt;
    }
    struct Rotation { std::vector<BoneLocal> poses; std::uint32_t passes; };
    Rotation rotate(const std::vector<BoneLocal>& basis, ig::Vector target, double softness) {
        if (basis.size() == 2 && ordinary()) {
            try {
                const auto candidate = ig::affine_pair(basis[0], basis[1], lengths_[1], outer(), target, static_cast<float>(softness), options_.bend_positive);
                if (candidate) {
                    const auto observed = ik_search::feasible([&] { return evaluate(candidate->poses); });
                    if (observed && agrees(candidate->expected, *observed)) return {candidate->poses, 1};
                }
            } catch (const Error& error) { if (error.code != ErrorCode::non_finite) throw; }
        }
        const auto before = evaluate(basis); const auto requested = ig::sub(target, before.pivot).length();
        const auto distance = ig::soft_distance(requested, before.reach, softness), factor = requested == 0 ? 1 : distance / requested;
        auto result = search(basis, basis, ig::add(before.pivot, ig::scale(ig::sub(target, before.pivot), factor)), false); return {std::move(result.poses), result.passes};
    }
public:
    Chain(std::vector<BoneLocal> source, std::vector<float> lengths, std::vector<TransformMode> modes, std::optional<Affine> parent, Affine root, Options options)
        : source_(std::move(source)), lengths_(std::move(lengths)), modes_(std::move(modes)), parent_(parent), root_(root), options_(options) {}
    Outcome solve(ig::Vector target) {
        tg::check({target.x, target.y, options_.threshold, options_.softness, options_.mix});
        if (options_.threshold < 0 || options_.softness < 0 || options_.mix < 0 || options_.mix > 1) throw ig::failure();
        const auto baseline = evaluate(source_);
        if (options_.mix == 0) return {source_, 0, tg::narrow(observe(baseline, target).residual)};
        auto full = source_; double factor = 1; std::uint32_t iterations = 0;
        if (source_.size() == 1 && ordinary()) {
            const auto candidate = single(source_[0], target); if (candidate) { full = candidate->poses; factor = candidate->factor; iterations = 1; }
        }
        if (iterations == 0) {
            auto rotated = rotate(full, target, 0); full = std::move(rotated.poses); iterations = rotated.passes;
            if (source_.size() <= 2 && (options_.compress || options_.stretch) && observe(evaluate(full), target).residual > options_.threshold) {
                auto scaled = search(source_, full, target, true); full = std::move(scaled.poses); factor = scaled.factor; iterations = std::max(iterations, scaled.passes);
                if (options_.compress && options_.transverse != ig::Transverse::area) {
                    auto collapsed = source_; collapsed[0] = ig::scaled(source_[0], 0, 1, options_.transverse);
                    const auto candidate = ik_search::feasible([&] { return rotate(collapsed, target, 0); });
                    if (candidate) {
                        const auto before = observe(evaluate(full), target).residual, after = observe(evaluate(candidate->poses), target).residual;
                        if (after < before) { full = candidate->poses; factor = 0; iterations = std::max(iterations, candidate->passes); }
                    }
                }
            }
        }
        if (options_.softness > 0 && source_.size() > 1) { auto softened = rotate(full, target, options_.softness); full = std::move(softened.poses); iterations = std::max(iterations, softened.passes); }
        auto mixed = source_;
        for (std::size_t i = 0; i < mixed.size(); ++i) {
            if (mixed[i].rotation_degrees == full[i].rotation_degrees) continue;
            const auto original = tg::wrap(mixed[i].rotation_degrees);
            mixed[i].rotation_degrees = tg::narrow(tg::wrap(original + options_.mix * tg::wrap(full[i].rotation_degrees - original)));
        }
        const auto axes = ig::scaled(source_[0], factor, options_.mix, options_.transverse); mixed[0].scale_x = axes.scale_x; mixed[0].scale_y = axes.scale_y;
        const auto residual = tg::narrow(observe(evaluate(mixed), target).residual); return {std::move(mixed), iterations, residual};
    }
};
}
