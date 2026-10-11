#pragma once
#include "cane/error.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <optional>
#include <vector>

namespace cane::animation::cubic_roots {
using Controls = std::array<double, 4>;
[[noreturn]] inline void fail() {
    throw Error(ErrorCode::non_finite, "sampleCurve", "Cubic intersection cannot be represented or resolved.");
}
inline double evaluate(const Controls& p, double u) {
    const auto pair = [u](double a, double b) { return a * (1 - u) + b * u; };
    const double a = pair(p[0], p[1]), b = pair(p[1], p[2]), c = pair(p[2], p[3]);
    return pair(pair(a, b), pair(b, c));
}
inline std::vector<double> boundaries(const Controls& p) {
    const double d0 = p[1] - p[0], d1 = p[2] - p[1], d2 = p[3] - p[2];
    double a = d0 - 2 * d1 + d2, b = 2 * (d1 - d0), c = d0;
    if (!std::isfinite(a) || !std::isfinite(b) || !std::isfinite(c)) fail();
    const double scale = std::max({std::abs(a), std::abs(b), std::abs(c)});
    std::vector<double> points{0, .5};
    const auto add = [&](double u) { if (u > 0 && u < .5) points.push_back(u); };
    if (scale != 0) {
        a /= scale; b /= scale; c /= scale;
        if (a == 0) { if (b != 0) add(-c / b); }
        else {
            const double discriminant = b * b - 4 * a * c;
            if (discriminant >= 0) {
                const double q = -.5 * (b + std::copysign(std::sqrt(discriminant), b));
                if (q == 0) add(-b / (2 * a));
                else { add(q / a); add(c / q); }
            }
        }
    }
    std::sort(points.begin(), points.end());
    points.erase(std::unique(points.begin(), points.end()), points.end());
    return points;
}
inline std::optional<double> first_half(const Controls& p, double target, bool reverse) {
    const auto points = boundaries(p);
    for (std::size_t index = 0; index + 1 < points.size(); ++index) {
        double a = points[reverse ? points.size() - 1 - index : index];
        double b = points[reverse ? points.size() - 2 - index : index + 1];
        double fa = evaluate(p, a);
        const double fb = evaluate(p, b);
        if (fa == target) return a;
        if (fb == target) return b;
        if ((fa < target) == (fb < target)) continue;
        for (unsigned step = 0; step < 384; ++step) {
            const double mid = a + (b - a) * .5;
            if (mid == a || mid == b) return std::abs(fa - target) <= std::abs(evaluate(p, b) - target) ? a : b;
            const double fm = evaluate(p, mid);
            if (fm == target) return mid;
            if ((fm < target) == (fa < target)) { a = mid; fa = fm; } else b = mid;
        }
        fail();
    }
    return {};
}
inline double sample(const Controls& x, const Controls& y, double target) {
    if (!std::isfinite(target)) fail();
    for (const auto v : x) if (!std::isfinite(v)) fail();
    for (const auto v : y) if (!std::isfinite(v)) fail();
    double value;
    if (const auto u = first_half(x, target, false)) value = evaluate(y, *u);
    else {
        const auto v = first_half({x[3], x[2], x[1], x[0]}, target, true);
        if (!v) fail();
        value = evaluate({y[3], y[2], y[1], y[0]}, *v);
    }
    if (!std::isfinite(value)) fail();
    return value;
}
}
