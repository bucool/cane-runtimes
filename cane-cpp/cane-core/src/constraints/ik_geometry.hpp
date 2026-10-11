#pragma once
#include "transform_geometry.hpp"
#include <tuple>

namespace cane::ik_geometry {
namespace tg = transform_geometry;
using Vector = tg::Vector;
enum class Transverse { fixed, uniform, area };
inline Error failure() { return Error(ErrorCode::non_finite, "apply", "IK geometry cannot be represented."); }
inline Vector add(Vector a, Vector b) { return {a.x + b.x, a.y + b.y}; }
inline Vector sub(Vector a, Vector b) { return {a.x - b.x, a.y - b.y}; }
inline Vector scale(Vector a, double s) { return {a.x * s, a.y * s}; }
inline double dot(Vector a, Vector b) { return a.x * b.x + a.y * b.y; }
inline double cross(Vector a, Vector b) { return a.x * b.y - a.y * b.x; }
inline Vector rotate(Vector a, double angle) { return {std::cos(angle) * a.x - std::sin(angle) * a.y, std::sin(angle) * a.x + std::cos(angle) * a.y}; }
constexpr double pi = 3.14159265358979323846;
inline double radians(double a) { auto r = std::fmod(a, 2 * pi); if (r < 0) r += 2 * pi; return r >= pi ? r - 2 * pi : r; }
inline BoneLocal scaled(BoneLocal pose, double multiplier, double mix, Transverse mode) {
    tg::check({pose.scale_x, pose.scale_y, multiplier, mix}); if (multiplier < 0 || mix < 0 || mix > 1) throw failure();
    const auto factor = (1 - mix) + mix * multiplier; if (mode == Transverse::area && factor == 0) throw failure();
    const auto fy = mode == Transverse::uniform ? factor : mode == Transverse::area ? 1 / factor : 1;
    pose.scale_x = tg::narrow(pose.scale_x * factor, true); pose.scale_y = tg::narrow(pose.scale_y * fy, true); return pose;
}
inline double soft_distance(double distance, double reach, double softness) {
    tg::check({distance, reach, softness}); if (distance < 0 || reach < 0 || softness < 0) throw failure();
    const auto width = std::min(softness, reach), start = reach - width;
    return width == 0 || distance <= start ? distance : start - width * std::expm1(-(distance - start) / width);
}
inline void unique(std::vector<double>& values, double tolerance) {
    std::sort(values.begin(), values.end()); values.erase(std::unique(values.begin(), values.end(), [=](double a, double b) { return std::abs(a - b) <= tolerance; }), values.end());
}
inline std::vector<double> polynomial_roots(std::vector<double> c) {
    while (c.size() > 1 && c.back() == 0) c.pop_back(); if (c.size() <= 1) return {};
    double scale = 0; for (const auto v : c) scale = std::max(scale, std::abs(v)); for (auto& v : c) v /= scale;
    const auto evaluate = [&](double x) { double sum = 0; for (auto i = c.rbegin(); i != c.rend(); ++i) sum = sum * x + *i; return sum; };
    std::vector<double> derivative; for (std::size_t i = 1; i < c.size(); ++i) derivative.push_back(c[i] * static_cast<double>(i));
    auto intervals = polynomial_roots(derivative); intervals.insert(intervals.end(), {-1, 1}); unique(intervals, 1e-14);
    double magnitude = 0; for (const auto v : c) magnitude += std::abs(v);
    const auto tolerance = 64 * std::numeric_limits<double>::epsilon() * magnitude;
    std::vector<double> roots; for (const auto x : intervals) if (std::abs(evaluate(x)) <= tolerance) roots.push_back(x);
    for (std::size_t i = 1; i < intervals.size(); ++i) {
        auto low = intervals[i - 1], high = intervals[i]; const auto left = evaluate(low), right = evaluate(high);
        if (left == 0 || right == 0 || std::signbit(left) == std::signbit(right)) continue;
        for (unsigned j = 0; j < 80; ++j) {
            const auto middle = (low + high) / 2; if (middle == low || middle == high) break;
            const auto value = evaluate(middle); if (value == 0) { low = high = middle; break; }
            if (std::signbit(value) == std::signbit(left)) low = middle; else high = middle;
        }
        roots.push_back((low + high) / 2);
    }
    unique(roots, 1e-12); return roots;
}
struct ReachCurve {
    Vector h, u, z;
    double extent, minimum = std::numeric_limits<double>::infinity(), maximum = 0;
    std::vector<double> stationary{-pi, -pi / 2, 0, pi / 2};
    ReachCurve(Vector origin, Vector cosine, Vector sine) {
        tg::check({origin.x, origin.y, cosine.x, cosine.y, sine.x, sine.y}); extent = std::max({origin.length(), cosine.length(), sine.length()}); tg::check({extent});
        const auto normalize = [&](Vector p) { return extent == 0 ? p : Vector{p.x / extent, p.y / extent}; };
        h = normalize(origin); u = normalize(cosine); z = normalize(sine);
        for (const auto chart : {1.0, -1.0}) {
            const auto cu = scale(u, chart), cz = scale(z, chart);
            const std::array<Vector, 3> e{add(h, cu), scale(cz, 2), sub(h, cu)}, derivative{cz, scale(cu, -2), scale(cz, -1)};
            std::vector<double> p(5); for (std::size_t i = 0; i < 3; ++i) for (std::size_t j = 0; j < 3; ++j) p[i + j] += dot(e[i], derivative[j]);
            for (const auto t : polynomial_roots(p)) stationary.push_back(radians(2 * std::atan(t) + (chart < 0 ? pi : 0)));
        }
        for (const auto beta : stationary) { const auto radius = vector(beta).length(); minimum = std::min(minimum, radius); maximum = std::max(maximum, radius); }
        tg::check({maximum * extent});
    }
    Vector vector(double beta) const { return add(h, add(scale(u, std::cos(beta)), scale(z, std::sin(beta)))); }
    struct Solution { Vector endpoint; double parent, child; };
    Solution solve(Vector target, Vector preferred, bool positive, bool reflected) const {
        const auto distance = target.length(), effective = std::clamp(distance, minimum * extent, maximum * extent); tg::check({distance, preferred.x, preferred.y});
        const auto radius = extent == 0 ? 0 : effective / extent; auto angles = stationary; angles.push_back(radians(preferred.y));
        for (const auto chart : {1.0, -1.0}) {
            const auto cu = scale(u, chart), cz = scale(z, chart); const std::array<Vector, 3> e{add(h, cu), scale(cz, 2), sub(h, cu)};
            std::vector<double> p(5); for (std::size_t i = 0; i < 3; ++i) for (std::size_t j = 0; j < 3; ++j) p[i + j] += dot(e[i], e[j]);
            p[0] -= radius * radius; p[2] -= 2 * radius * radius; p[4] -= radius * radius;
            for (const auto t : polynomial_roots(p)) angles.push_back(radians(2 * std::atan(t) + (chart < 0 ? pi : 0)));
        }
        angles.erase(std::remove_if(angles.begin(), angles.end(), [&](double beta) { return std::abs(vector(beta).length() - radius) > 2e-7; }), angles.end()); unique(angles, 1e-12);
        std::optional<std::tuple<bool, double, double, double>> best;
        for (const auto beta : angles) {
            const auto v = vector(beta); const auto alpha = distance == 0 || v.length() == 0 ? radians(preferred.x) : radians(std::atan2(target.y, target.x) - std::atan2(v.y, v.x));
            auto bend = cross(h, sub(v, h)); if (reflected) bend = -bend;
            const auto da = radians(alpha - radians(preferred.x)), db = radians(beta - radians(preferred.y));
            const auto candidate = std::make_tuple(bend != 0 && (bend > 0) != positive, da * da + db * db, beta, alpha);
            if (!best || candidate < *best) best = candidate;
        }
        if (!best) throw failure(); const auto beta = std::get<2>(*best), alpha = std::get<3>(*best);
        const auto endpoint = scale(rotate(vector(beta), alpha), extent); tg::check({endpoint.x, endpoint.y, sub(endpoint, target).length()});
        return {endpoint, alpha, beta};
    }
};
inline std::optional<Vector> inverse_target(const Affine& outer, const BoneLocal& pose, Vector target) {
    const double a = outer.a, b = outer.b, c = outer.c, d = outer.d, determinant = a * d - b * c; if (determinant == 0) return {};
    const auto dx = target.x - outer.tx, dy = target.y - outer.ty;
    const Vector result{(d * dx - c * dy) / determinant - pose.x, (a * dy - b * dx) / determinant - pose.y};
    return std::isfinite(result.x) && std::isfinite(result.y) ? std::optional<Vector>(result) : std::nullopt;
}
inline Vector transform(const Affine& m, Vector p) { return {m.a * p.x + m.c * p.y + m.tx, m.b * p.x + m.d * p.y + m.ty}; }
struct PairCandidate { std::vector<BoneLocal> poses; Vector expected; };
inline std::optional<PairCandidate> affine_pair(BoneLocal parent, BoneLocal child, float child_length, const Affine& outer, Vector target, float softness, bool positive) {
    const auto converted = inverse_target(outer, parent, target); if (!converted) return {};
    auto shape_pose = parent; shape_pose.rotation_degrees = 0; const auto shape = Affine::from_local(shape_pose);
    const double ell = static_cast<double>(child_length) * child.scale_x; const Vector hx{shape.a, shape.b}, hy{shape.c, shape.d};
    const ReachCurve curve(add(scale(hx, child.x), scale(hy, child.y)), scale(hx, ell), scale(hy, ell));
    const auto distance = converted->length(), desired = soft_distance(distance, curve.maximum * curve.extent, softness), ratio = distance == 0 ? 1 : desired / distance;
    const auto solution = curve.solve(scale(*converted, ratio), {parent.rotation_degrees * (pi / 180), (static_cast<double>(child.rotation_degrees) + child.shear_x_degrees) * (pi / 180)}, positive, static_cast<double>(outer.a) * outer.d - static_cast<double>(outer.b) * outer.c < 0);
    const auto expected = transform(outer, {parent.x + solution.endpoint.x, parent.y + solution.endpoint.y});
    const auto angle = [](float before, double after) { return static_cast<float>(tg::wrap(tg::wrap(before) + tg::wrap(tg::wrap(after) - tg::wrap(before)))); };
    parent.rotation_degrees = angle(parent.rotation_degrees, solution.parent * (180 / pi)); child.rotation_degrees = angle(child.rotation_degrees, solution.child * (180 / pi) - child.shear_x_degrees);
    return PairCandidate{{parent, child}, expected};
}
}
