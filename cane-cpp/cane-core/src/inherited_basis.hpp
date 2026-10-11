#pragma once
#include "cane/math.hpp"
#include "cane/error.hpp"
#include <cmath>
#include <utility>

namespace cane {
struct InheritedBasis {
    double a, b, c, d;
    static InheritedBasis from(const Affine& parent, TransformMode mode) {
        if (!parent.finite()) throw Error(ErrorCode::non_finite, "composeBoneWorld", "Parent basis must be finite.");
        const double a = parent.a, b = parent.b, c = parent.c, d = parent.d;
        if (mode == TransformMode::inherit_all) return {a, b, c, d};
        if (mode == TransformMode::translation_only) return {1, 0, 0, 1};
        const auto unit = [](double x, double y) -> std::pair<double, double> {
            const auto length = std::hypot(x, y);
            return length == 0 ? std::pair<double, double>{1, 0} : std::pair<double, double>{x / length, y / length};
        };
        const auto [rx, ry] = unit(a + d, b - c);
        if (mode == TransformMode::ignore_scale_and_reflection) return {rx, ry, -ry, rx};
        double qa = rx, qb = ry, qc = -ry, qd = rx;
        if (a * d - b * c < 0) {
            const auto [fx, fy] = unit(a - d, b + c); qa = fx; qb = fy; qc = fy; qd = -fx;
        }
        if (mode == TransformMode::ignore_scale) return {qa, qb, qc, qd};
        if (mode != TransformMode::ignore_rotation_and_reflection)
            throw Error(ErrorCode::invalid_argument, "composeBoneWorld", "Unknown bone inheritance mode.");
        const double xy = ((a * qb + c * qd) + (b * qa + d * qc)) * .5;
        return {a * qa + c * qc, xy, xy, b * qb + d * qd};
    }
};
}
