#include "cane/math.hpp"
#include "inherited_basis.hpp"
#include "cane/error.hpp"
#include <cmath>

namespace cane {
namespace {
float cosine(float value) noexcept { return static_cast<float>(std::cos(static_cast<double>(value))); }
float sine(float value) noexcept { return static_cast<float>(std::sin(static_cast<double>(value))); }
float remainder(float value, float period) noexcept {
    float result = std::fmod(value, period);
    if (result < 0) result += period;
    return result;
}
}
float hypot(float x, float y) noexcept { return static_cast<float>(std::sqrt(static_cast<double>(x) * x + static_cast<double>(y) * y)); }
float wrap_degrees(float value) noexcept { return remainder(value + 180.0f, 360.0f) - 180.0f; }
float constraint_degrees(float value) noexcept { const float result = remainder(value, 360.0f); return result > 180.0f ? result - 360.0f : result; }
float Affine::determinant() const noexcept { return a * d - c * b; }
bool Affine::finite() const noexcept { return std::isfinite(a) && std::isfinite(b) && std::isfinite(c) && std::isfinite(d) && std::isfinite(tx) && std::isfinite(ty); }
Point Affine::transform(Point p) const noexcept { return {a * p.x + c * p.y + tx, b * p.x + d * p.y + ty}; }
Point Affine::transform_direction(Point p) const noexcept { return {a * p.x + c * p.y, b * p.x + d * p.y}; }
Affine operator*(const Affine& l, const Affine& r) noexcept {
    return {l.a * r.a + l.c * r.b, l.b * r.a + l.d * r.b, l.a * r.c + l.c * r.d, l.b * r.c + l.d * r.d,
            l.a * r.tx + l.c * r.ty + l.tx, l.b * r.tx + l.d * r.ty + l.ty};
}
std::optional<Affine> Affine::inverse(float threshold) const noexcept {
    const float det = determinant();
    if (!finite() || !std::isfinite(det) || !std::isfinite(threshold) || threshold < 0 || std::abs(det) <= threshold) return {};
    const Affine result{d / det, -b / det, -c / det, a / det, (c * ty - d * tx) / det, (b * tx - a * ty) / det};
    return result.finite() ? std::optional<Affine>(result) : std::nullopt;
}
Affine Affine::from_local(const BoneLocal& local) noexcept {
    const float rx = (local.rotation_degrees + local.shear_x_degrees) * degrees_to_radians;
    const float ry = (local.rotation_degrees + local.shear_y_degrees) * degrees_to_radians;
    return {cosine(rx) * local.scale_x, sine(rx) * local.scale_x, -sine(ry) * local.scale_y,
            cosine(ry) * local.scale_y, local.x, local.y};
}
Affine Affine::child(const Affine& parent, const BoneLocal& local, TransformMode mode) {
    if (mode == TransformMode::inherit_all) return parent * from_local(local);
    if (mode == TransformMode::translation_only) {
        const Point origin = parent.transform({local.x, local.y});
        const float rx = (local.rotation_degrees + local.shear_x_degrees) * degrees_to_radians;
        const float ry = (local.rotation_degrees + 90.0f + local.shear_y_degrees) * degrees_to_radians;
        return {cosine(rx) * local.scale_x, sine(rx) * local.scale_x,
                cosine(ry) * local.scale_y, sine(ry) * local.scale_y, origin.x, origin.y};
    }
    const auto basis = InheritedBasis::from(parent, mode);
    const auto axes = from_local(local);
    return {static_cast<float>(basis.a * axes.a + basis.c * axes.b), static_cast<float>(basis.b * axes.a + basis.d * axes.b),
            static_cast<float>(basis.a * axes.c + basis.c * axes.d), static_cast<float>(basis.b * axes.c + basis.d * axes.d),
            static_cast<float>(static_cast<double>(parent.a) * local.x + static_cast<double>(parent.c) * local.y + parent.tx),
            static_cast<float>(static_cast<double>(parent.b) * local.x + static_cast<double>(parent.d) * local.y + parent.ty)};
}
}
