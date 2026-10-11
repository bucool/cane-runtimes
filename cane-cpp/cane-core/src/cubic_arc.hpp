#pragma once
#include "transform_geometry.hpp"

namespace cane::path_geometry {
using Point = transform_geometry::Vector;
using Controls = std::array<Point, 4>;
inline Error failure() { return Error(ErrorCode::non_finite, "apply", "Path geometry cannot be represented."); }
inline Error limit() { return Error(ErrorCode::resource_limit, "apply", "Path subdivision budget is exhausted."); }
struct Budget { std::size_t remaining = 1000000; void visit() { if (remaining == 0) throw limit(); --remaining; } };
inline Point difference(Point a, Point b) { return {a.x - b.x, a.y - b.y}; }
inline Point lerp(Point a, Point b, double t) { return {a.x * (1 - t) + b.x * t, a.y * (1 - t) + b.y * t}; }
inline std::pair<Controls, Controls> split(const Controls& p, double t) {
    const auto a = lerp(p[0], p[1], t), b = lerp(p[1], p[2], t), c = lerp(p[2], p[3], t), d = lerp(a, b, t), e = lerp(b, c, t), f = lerp(d, e, t);
    return {{p[0], a, d, f}, {f, e, c, p[3]}};
}
inline std::pair<double, double> bounds(const Controls& p) {
    const auto lower = difference(p[3], p[0]).length(); double upper = 0; for (std::size_t i = 0; i < 3; ++i) upper += difference(p[i + 1], p[i]).length(); upper = std::max(upper, lower);
    if (!std::isfinite(lower) || !std::isfinite(upper)) throw failure(); return {lower, upper};
}
inline double partial_length(const Controls& p, double t) { const auto [low, high] = bounds(split(p, std::clamp(t, 0.0, 1.0)).first); return low + (high - low) * .5; }
inline double box_distance(const Controls& p, Point target) {
    auto x0 = std::numeric_limits<double>::infinity(), y0 = x0, x1 = -x0, y1 = -x0;
    for (const auto q : p) { x0 = std::min(x0, q.x); y0 = std::min(y0, q.y); x1 = std::max(x1, q.x); y1 = std::max(y1, q.y); }
    return Point{target.x < x0 ? x0 - target.x : target.x > x1 ? target.x - x1 : 0, target.y < y0 ? y0 - target.y : target.y > y1 ? target.y - y1 : 0}.length();
}
class CubicArc {
    struct Node { Controls controls; double start, end; unsigned depth; };
    struct Leaf { Controls controls; double start, end, from, to; };
    Controls controls_;
    std::vector<Leaf> leaves_;
    double length_ = 0, tolerance_;
public:
    CubicArc(const Controls& controls, Budget& budget) : controls_(controls), tolerance_(bounds(controls).second * 1e-6) {
        for (const auto p : controls) if (!std::isfinite(p.x) || !std::isfinite(p.y) || std::abs(p.x) > std::numeric_limits<float>::max() || std::abs(p.y) > std::numeric_limits<float>::max()) throw failure();
        std::vector<Node> pending{{controls, 0, 1, 0}};
        while (!pending.empty()) {
            const auto n = pending.back(); pending.pop_back(); budget.visit(); const auto [lower, upper] = bounds(n.controls);
            if (upper - lower <= tolerance_ * (n.end - n.start)) {
                const auto next = length_ + lower + (upper - lower) * .5; if (!std::isfinite(next)) throw failure();
                leaves_.push_back({n.controls, n.start, n.end, length_, next}); length_ = next;
            } else {
                if (n.depth == 32) throw limit(); const auto [left, right] = split(n.controls, .5); const auto middle = (n.start + n.end) * .5;
                pending.push_back({right, middle, n.end, n.depth + 1}); pending.push_back({left, n.start, middle, n.depth + 1});
            }
        }
    }
    const Controls& controls() const noexcept { return controls_; }
    double length() const noexcept { return length_; }
    Point point(double t) const { return split(controls_, std::clamp(t, 0.0, 1.0)).first[3]; }
    Point tangent(double t) const {
        t = std::clamp(t, 0.0, 1.0); const auto e0 = difference(controls_[1], controls_[0]), e1 = difference(controls_[2], controls_[1]), e2 = difference(controls_[3], controls_[2]);
        const auto first = lerp(lerp(e0, e1, t), lerp(e1, e2, t), t); if (first.length() > 0) return first;
        const auto a = difference(e1, e0), b = difference(e2, e1); auto second = lerp(a, b, t); if (t == 1) second = {-second.x, -second.y};
        if (second.length() > 0) return second; const auto third = difference(b, a); return third.length() > 0 ? third : Point{1, 0};
    }
    double parameter_at_distance(double distance) const {
        if (distance <= 0 || length_ == 0) return 0; if (distance >= length_) return 1;
        auto it = std::lower_bound(leaves_.begin(), leaves_.end(), distance, [](const Leaf& leaf, double v) { return leaf.to < v; }); if (it == leaves_.end()) --it;
        const auto requested = distance - it->from; double low = 0, high = 1;
        for (unsigned i = 0; i < 48; ++i) { const auto middle = (low + high) * .5; if (partial_length(it->controls, middle) < requested) low = middle; else high = middle; }
        return it->start + (it->end - it->start) * (low + high) * .5;
    }
    double distance_at_parameter(double t) const {
        if (t <= 0) return 0; if (t >= 1) return length_;
        auto it = std::lower_bound(leaves_.begin(), leaves_.end(), t, [](const Leaf& leaf, double v) { return leaf.end < v; }); if (it == leaves_.end()) --it;
        return it->from + std::clamp(partial_length(it->controls, (t - it->start) / (it->end - it->start)), 0.0, it->to - it->from);
    }
    double closest_parameter(Point target, Budget& budget) const {
        if (!std::isfinite(target.x) || !std::isfinite(target.y)) throw failure();
        double best_t = 0, best_distance = difference(controls_[0], target).length(); const auto end_distance = difference(controls_[3], target).length();
        if (end_distance < best_distance) { best_t = 1; best_distance = end_distance; } if (!std::isfinite(best_distance)) throw failure();
        std::vector<Node> pending{{controls_, 0, 1, 0}};
        while (!pending.empty()) {
            const auto n = pending.back(); pending.pop_back(); budget.visit(); if (best_distance - box_distance(n.controls, target) <= tolerance_) continue;
            const auto [left, right] = split(n.controls, .5); const auto middle = (n.start + n.end) * .5, distance = difference(left[3], target).length();
            if (distance < best_distance || (distance == best_distance && middle < best_t)) { best_t = middle; best_distance = distance; }
            if (best_distance - box_distance(n.controls, target) <= tolerance_) continue; if (n.depth == 32) throw limit();
            pending.push_back({right, middle, n.end, n.depth + 1}); pending.push_back({left, n.start, middle, n.depth + 1});
        }
        return best_t;
    }
};
}
