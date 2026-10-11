#pragma once
#include "cane/math.hpp"
#include "cane/error.hpp"
#include <cmath>

namespace cane::geometry {
inline Point finite_placement_point(double x, double y) {
    const Point result{static_cast<float>(x), static_cast<float>(y)};
    if (!std::isfinite(result.x) || !std::isfinite(result.y))
        throw Error(ErrorCode::non_finite, "apply", "Mesh placement produces non-finite coordinates.", "placement");
    return result;
}
inline Point mesh_placement_world(Point placement, const Affine& root) {
    if (placement.x == 0 && placement.y == 0) return placement;
    return finite_placement_point(static_cast<double>(root.a) * placement.x + static_cast<double>(root.c) * placement.y,
        static_cast<double>(root.b) * placement.x + static_cast<double>(root.d) * placement.y);
}
inline Point place_mesh_point(Point point, Point delta) {
    if (delta.x == 0 && delta.y == 0) return point;
    return finite_placement_point(static_cast<double>(point.x) + delta.x, static_cast<double>(point.y) + delta.y);
}
inline Point unplace_mesh_point(Point point, Point delta) {
    if (delta.x == 0 && delta.y == 0) return point;
    return finite_placement_point(static_cast<double>(point.x) - delta.x, static_cast<double>(point.y) - delta.y);
}
inline Affine place_mesh_affine(Affine matrix, Point delta) {
    if (delta.x == 0 && delta.y == 0) return matrix;
    const auto translation = finite_placement_point(static_cast<double>(matrix.tx) + delta.x, static_cast<double>(matrix.ty) + delta.y);
    matrix.tx = translation.x; matrix.ty = translation.y; return matrix;
}
}
