#pragma once
#include "ik_geometry.hpp"
#include <functional>

namespace cane::ik_search {
using Vector = ik_geometry::Vector;
using Bound = std::pair<double, double>;
struct Observation { double residual; Vector error; bool bend_matches; };
struct Result { std::vector<double> parameters; Observation observation; std::uint32_t passes; };
using Evaluator = std::function<Observation(const std::vector<double>&)>;
template<typename F> auto feasible(F evaluate) -> std::optional<decltype(evaluate())> {
    try { return evaluate(); } catch (const Error& error) { if (error.code == ErrorCode::non_finite) return {}; throw; }
}
inline void coordinate(std::vector<double>& values, std::size_t coordinate, Bound bounds, const Evaluator& evaluate) {
    const auto [lower, upper] = bounds; if (lower == upper) return;
    auto best_x = values[coordinate], best_error = evaluate(values).residual; const auto step = (upper - lower) / 32;
    std::vector<std::pair<double, double>> probes{{best_x, best_error}};
    const auto sample = [&](double x) {
        values[coordinate] = x; const auto result = feasible([&] { return evaluate(values); });
        const auto error = result ? result->residual : std::numeric_limits<double>::infinity();
        if (error < best_error) { best_x = x; best_error = error; } return error;
    };
    for (unsigned i = 0; i <= 32; ++i) { const auto x = lower + i * step; probes.emplace_back(x, sample(x)); }
    std::sort(probes.begin(), probes.end(), [](auto a, auto b) { return std::tie(a.second, a.first) < std::tie(b.second, b.first); });
    const auto fraction = (std::sqrt(5.0) - 1) / 2;
    for (unsigned candidate = 0; candidate < 2; ++candidate) {
        const auto x = std::clamp(probes[candidate].first, lower, upper); auto low = std::max(x - step, lower), high = std::min(x + step, upper);
        auto left = high - fraction * (high - low), right = low + fraction * (high - low), fl = sample(left), fr = sample(right);
        for (unsigned i = 0; i < 40; ++i) {
            if (fl <= fr) { high = right; right = left; fr = fl; left = high - fraction * (high - low); fl = sample(left); }
            else { low = left; left = right; fl = fr; right = low + fraction * (high - low); fr = sample(right); }
        }
    }
    values[coordinate] = best_x;
}
inline Observation coupled(std::vector<double>& values, const std::vector<Bound>& bounds, std::size_t angular_count, Observation baseline, const Evaluator& evaluate) {
    std::vector<Vector> jacobian(values.size(), Vector{0, 0});
    for (std::size_t i = 0; i < values.size(); ++i) {
        const auto original = values[i]; const auto [low, high] = bounds[i]; if (low == high || original < low || original > high) continue;
        const auto step = i < angular_count ? .05 : .001, left = std::max(original - step, low), right = std::min(original + step, high);
        values[i] = left; const auto a = feasible([&] { return evaluate(values); }); values[i] = right; const auto b = feasible([&] { return evaluate(values); }); values[i] = original;
        if (a && b) jacobian[i] = {(b->error.x - a->error.x) / (right - left), (b->error.y - a->error.y) / (right - left)};
    }
    double a = 0, b = 0, d = 0; for (const auto j : jacobian) { a += j.x * j.x; b += j.x * j.y; d += j.y * j.y; }
    const auto damping = (a + d) * 1e-6; if (damping == 0 || !std::isfinite(damping)) return baseline;
    a += damping; d += damping; const auto determinant = a * d - b * b; if (determinant <= 0 || !std::isfinite(determinant)) return baseline;
    const Vector v{(d * baseline.error.x - b * baseline.error.y) / determinant, (a * baseline.error.y - b * baseline.error.x) / determinant};
    std::vector<double> delta(values.size()); double maximum = 1;
    for (std::size_t i = 0; i < values.size(); ++i) { delta[i] = -jacobian[i].x * v.x - jacobian[i].y * v.y; maximum = std::max(maximum, std::abs(delta[i]) / (i < angular_count ? 30 : 1)); }
    if (!std::isfinite(maximum)) return baseline;
    const auto original = values; auto factor = 1 / maximum;
    for (unsigned attempt = 0; attempt < 10; ++attempt) {
        for (std::size_t i = 0; i < values.size(); ++i) values[i] = std::clamp(original[i] + delta[i] * factor, bounds[i].first, bounds[i].second);
        const auto candidate = feasible([&] { return evaluate(values); }); if (candidate && candidate->residual < baseline.residual) return *candidate; factor *= .5;
    }
    values = original; return baseline;
}
inline Result minimize(const std::vector<double>& initial, const std::vector<Bound>& bounds, std::size_t angular_count, std::uint32_t iterations, double threshold, const Evaluator& evaluate) {
    if (initial.empty() || initial.size() != bounds.size() || angular_count > initial.size() || threshold < 0) throw ik_geometry::failure();
    transform_geometry::check(initial); transform_geometry::check({threshold});
    for (const auto [low, high] : bounds) { transform_geometry::check({low, high}); if (low > high) throw ik_geometry::failure(); }
    Result winner{initial, evaluate(initial), 0}; if (winner.observation.residual <= threshold && winner.observation.bend_matches) return winner;
    const auto offsets = angular_count == 2 ? std::vector<double>{0, 90, -90, 180} : std::vector<double>{0};
    for (const auto offset : offsets) {
        auto values = initial; if (angular_count == 2) values[1] = transform_geometry::wrap(values[1] + offset);
        const auto possible = feasible([&] { return evaluate(values); }); if (!possible) continue; auto current = *possible; std::uint32_t passes = 0;
        for (std::uint32_t pass = 0; pass < iterations; ++pass) {
            if (current.residual <= threshold && current.bend_matches) break; const auto before = current.residual;
            for (std::size_t i = values.size(); i-- > 0;) { coordinate(values, i, bounds[i], evaluate); current = evaluate(values); }
            current = coupled(values, bounds, angular_count, current, evaluate); passes = pass + 1; if (current.residual >= before) break;
        }
        const auto old = winner.observation;
        const auto better = current.residual <= threshold && old.residual <= threshold && current.bend_matches != old.bend_matches ? current.bend_matches : current.residual < old.residual;
        if (better) winner = {values, current, passes};
        if (winner.observation.residual <= threshold && winner.observation.bend_matches) break;
    }
    return winner;
}
}
