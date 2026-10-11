#pragma once
#include "cane/math.hpp"
#include "cane/error.hpp"
#include "inherited_basis.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <initializer_list>
#include <limits>
#include <vector>

namespace cane::transform_geometry {
using Channels = std::array<double, 6>;
struct Vector { double x, y; double length() const { return std::hypot(x, y); } };
struct Route { std::size_t from, to; double source_origin, target_origin, gain; std::optional<double> limit; };
constexpr double roundoff = 64 * 1.1920928955078125e-7, minimum = 1.401298464324817e-45;
constexpr double to_degrees = 180 / 3.14159265358979323846, to_radians = 3.14159265358979323846 / 180;
inline Error failure() { return Error(ErrorCode::invalid_state, "apply", "Transform geometry cannot be represented through the parent basis."); }
template<typename Range> inline void check(const Range& values) { for (double value : values) if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, "apply", "Transform arithmetic must be finite."); }
inline void check(std::initializer_list<double> values) { for (double value : values) if (!std::isfinite(value)) throw Error(ErrorCode::non_finite, "apply", "Transform arithmetic must be finite."); }
inline float narrow(double value, bool scale = false) { check({value}); const auto result = static_cast<float>(value); if (!std::isfinite(result) || (scale && value != 0 && result == 0)) throw failure(); return result; }
inline double wrap(double value) { auto r = std::fmod(value, 360); if (r < 0) r += 360; return r >= 180 ? r - 360 : r; }
inline Channels local_channels(const BoneLocal& local) {
    const Channels result{local.rotation_degrees, local.x, local.y, local.scale_x, local.scale_y, local.shear_y_degrees}; check(result); check({local.shear_x_degrees}); return result;
}
inline BoneLocal to_local(const Channels& v, const BoneLocal& previous) {
    check(v); return {narrow(v[1]), narrow(v[2]), narrow(v[0]), narrow(v[3], true), narrow(v[4], true), previous.shear_x_degrees, narrow(v[5])};
}
inline Channels world_channels(const Affine& m) {
    const double a = m.a, b = m.b, c = m.c, d = m.d; check({a, b, c, d, m.tx, m.ty});
    const auto sx = std::hypot(a, b), sy = std::hypot(c, d);
    if (sx == 0) return {sy == 0 ? 0 : wrap(std::atan2(d, c) * to_degrees - 90), m.tx, m.ty, 0, sy, 0};
    const auto sign = a * d - b * c < 0 ? -1 : 1;
    const auto along = a / sx * c + b / sx * d, normal = a / sx * d - b / sx * c;
    return {wrap(std::atan2(b, a) * to_degrees), m.tx, m.ty, sx, sign * sy, sy == 0 ? 0 : std::atan2(-sign * along, sign * normal) * to_degrees};
}
inline void check_world(const Affine& m) { if (!m.finite() || !std::isfinite(cane::hypot(m.a, m.b)) || !std::isfinite(cane::hypot(m.c, m.d))) throw failure(); }
inline Affine to_world(const Channels& v) {
    check(v); const auto t = wrap(v[0]) * to_radians, p = wrap(v[5]) * to_radians;
    const auto si = std::sin(t), co = std::cos(t), sp = std::sin(p), cp = std::cos(p);
    const Affine result{narrow(v[3] * co), narrow(v[3] * si), narrow(-v[4] * (si * cp + co * sp)), narrow(v[4] * (co * cp - si * sp)), narrow(v[1]), narrow(v[2])};
    if ((v[3] != 0 && result.a == 0 && result.b == 0) || (v[4] != 0 && result.c == 0 && result.d == 0)) throw failure();
    check_world(result); return result;
}
inline bool same(const Affine& a, const Affine& b) { return a.a == b.a && a.b == b.b && a.c == b.c && a.d == b.d && a.tx == b.tx && a.ty == b.ty; }
inline Vector inverse_vector(const Affine& m, Vector rhs, bool allow_roundoff = false, double scale = 0) {
    const double a = m.a, b = m.b, c = m.c, d = m.d, determinant = a * d - b * c; check({a, b, c, d, rhs.x, rhs.y});
    Vector result;
    if (determinant != 0) result = {(d * rhs.x - c * rhs.y) / determinant, (a * rhs.y - b * rhs.x) / determinant};
    else {
        const auto squared = a * a + b * b + c * c + d * d;
        if (squared == 0) { if (rhs.x == 0 && rhs.y == 0) return {0, 0}; throw failure(); }
        result = {(a * rhs.x + b * rhs.y) / squared, (c * rhs.x + d * rhs.y) / squared};
    }
    check({result.x, result.y}); const auto x1 = a * result.x, x2 = c * result.y, y1 = b * result.x, y2 = d * result.y;
    auto tx = 64 * std::numeric_limits<double>::epsilon() * (std::abs(x1) + std::abs(x2) + std::abs(rhs.x));
    auto ty = 64 * std::numeric_limits<double>::epsilon() * (std::abs(y1) + std::abs(y2) + std::abs(rhs.y));
    if (allow_roundoff && determinant == 0) { tx = std::max(tx, std::max(roundoff * scale, minimum)); ty = std::max(ty, std::max(roundoff * scale, minimum)); }
    if (std::abs(x1 + x2 - rhs.x) > tx || std::abs(y1 + y2 - rhs.y) > ty) throw failure(); return result;
}
inline Vector inverse_point(const Affine& m, Vector point) { return inverse_vector(m, {point.x - m.tx, point.y - m.ty}); }
inline Vector nearest_point(const Affine& m, Vector wanted, Vector reference) {
    if (static_cast<double>(m.a) * m.d != static_cast<double>(m.b) * m.c) return inverse_point(m, wanted);
    const Vector image{m.a * reference.x + m.c * reference.y + m.tx, m.b * reference.x + m.d * reference.y + m.ty};
    const auto delta = inverse_vector(m, {wanted.x - image.x, wanted.y - image.y}, true, std::max(wanted.length(), image.length()));
    return {reference.x + delta.x, reference.y + delta.y};
}
inline Vector nearest_axis(const Affine& parent, TransformMode mode, Vector wanted, Vector reference) {
    if (mode == TransformMode::inherit_all) return nearest_point({parent.a, parent.b, parent.c, parent.d, 0, 0}, wanted, reference);
    if (mode == TransformMode::translation_only) return wanted;
    const auto basis = InheritedBasis::from(parent, mode);
    if (mode != TransformMode::ignore_rotation_and_reflection) return {basis.a * wanted.x + basis.b * wanted.y, basis.c * wanted.x + basis.d * wanted.y};
    const bool full = static_cast<double>(parent.a) * parent.d != static_cast<double>(parent.b) * parent.c;
    const Vector image{basis.a * reference.x + basis.c * reference.y, basis.b * reference.x + basis.d * reference.y};
    const auto rhs = full ? wanted : Vector{wanted.x - image.x, wanted.y - image.y};
    const auto preimage = inverse_vector(parent, rhs, true, std::max(wanted.length(), image.length()));
    const auto q = InheritedBasis::from(parent, TransformMode::ignore_scale);
    const Vector delta{q.a * preimage.x + q.c * preimage.y, q.b * preimage.x + q.d * preimage.y};
    return full ? delta : Vector{reference.x + delta.x, reference.y + delta.y};
}
inline Vector local_axis(const BoneLocal& p, bool y) {
    const auto angle = (static_cast<double>(p.rotation_degrees) + (y ? p.shear_y_degrees : p.shear_x_degrees) + (y ? 90 : 0)) * to_radians;
    const auto scale = y ? p.scale_y : p.scale_x; return {scale * std::cos(angle), scale * std::sin(angle)};
}
inline void check_axis(Vector a, Vector b) {
    if ((b.x != 0 || b.y != 0) && a.x == 0 && a.y == 0) throw failure();
    const auto tolerance = std::max(roundoff * std::max(a.length(), b.length()), minimum);
    if (std::abs(a.x - b.x) > tolerance || std::abs(a.y - b.y) > tolerance) throw failure();
}
inline BoneLocal recover(const Affine& requested, const Affine& parent, TransformMode mode, const BoneLocal& previous) {
    local_channels(previous); world_channels(requested);
    const auto origin = nearest_point(parent, {requested.tx, requested.ty}, {previous.x, previous.y});
    const auto x = nearest_axis(parent, mode, {requested.a, requested.b}, local_axis(previous, false));
    const auto y = nearest_axis(parent, mode, {requested.c, requested.d}, local_axis(previous, true));
    const auto sx = previous.scale_x < 0 ? -1 : 1, sy = previous.scale_y < 0 ? -1 : 1;
    auto result = previous; result.x = narrow(origin.x); result.y = narrow(origin.y);
    result.scale_x = narrow(sx * x.length(), true); result.scale_y = narrow(sy * y.length(), true);
    if (x.x != 0 || x.y != 0) result.rotation_degrees = narrow(wrap(std::atan2(sx * x.y, sx * x.x) * to_degrees - previous.shear_x_degrees));
    if (y.x != 0 || y.y != 0) result.shear_y_degrees = narrow(wrap(std::atan2(sy * y.y, sy * y.x) * to_degrees - result.rotation_degrees - 90));
    const auto actual = Affine::child(parent, result, mode); check_world(actual);
    check_axis({actual.a, actual.b}, {requested.a, requested.b}); check_axis({actual.c, actual.d}, {requested.c, requested.d});
    const auto ex = std::abs(static_cast<double>(parent.a) * result.x) + std::abs(static_cast<double>(parent.c) * result.y) + std::abs(static_cast<double>(parent.tx));
    const auto ey = std::abs(static_cast<double>(parent.b) * result.x) + std::abs(static_cast<double>(parent.d) * result.y) + std::abs(static_cast<double>(parent.ty));
    if (std::abs(static_cast<double>(actual.tx) - requested.tx) > std::max(roundoff * std::max(ex, std::abs(static_cast<double>(requested.tx))), minimum)
        || std::abs(static_cast<double>(actual.ty) - requested.ty) > std::max(roundoff * std::max(ey, std::abs(static_cast<double>(requested.ty))), minimum)) throw failure();
    return result;
}
inline Channels evaluate(const Channels& source, const Channels& current, const std::vector<Route>& routes, const std::array<float, 6>& mixes, bool relative) {
    check(source); check(current); check(mixes); auto output = current;
    for (const auto& route : routes) {
        check({route.source_origin, route.target_origin, route.gain}); if (route.limit) check({*route.limit});
        const auto i = route.to; const double m = mixes[i]; if (m == 0) continue;
        auto z = route.target_origin + (source[route.from] - route.source_origin) * route.gain; check({z});
        if (route.limit) z = std::clamp(z, std::min(route.target_origin, *route.limit), std::max(route.target_origin, *route.limit));
        const auto q = output[i]; double next;
        if (i == 0 || i == 5) { const auto delta = relative ? z : wrap(z - q); next = delta == 0 ? q : wrap(wrap(q) + m * delta); }
        else if (relative && (i == 3 || i == 4)) next = z == 1 ? q : q * ((1 - m) + m * z);
        else if (relative) next = z == 0 ? q : q + m * z;
        else next = q == z ? q : (1 - m) * q + m * z;
        check({next}); output[i] = next;
    }
    return output;
}
}
