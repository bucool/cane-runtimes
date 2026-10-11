#include "path_sampler.hpp"

namespace cane::constraints {
namespace pg = path_geometry;
namespace {
float finite_float(double value) { const auto result = static_cast<float>(value); if (!std::isfinite(result)) throw pg::failure(); return result; }
double modulo(double value, double span) { const auto r = std::fmod(value, span); return r < 0 ? r + span : r; }
PathSample output(pg::Point point, pg::Point tangent) { return {{finite_float(point.x), finite_float(point.y)}, finite_float(std::atan2(tangent.y, tangent.x) * (180 / 3.14159265358979323846))}; }
}
void PathSampler::reset(const std::vector<float>& world, bool closed, bool constant_speed, const std::vector<float>& stored) {
    curves_.clear(); lengths_.clear(); total_ = 0; closed_ = closed; constant_speed_ = constant_speed;
    if (world.size() < 12 || world.size() % 6 != 0) return;
    const auto knots = world.size() / 6, count = closed ? knots : knots - 1;
    const auto read = [&](std::size_t knot, std::size_t point) -> pg::Point { const auto i = knot * 6 + point * 2; return {world[i], world[i + 1]}; };
    pg::Budget budget;
    for (std::size_t i = 0; i < count; ++i) {
        const auto next = (i + 1) % knots; const pg::Controls controls{read(i, 1), read(i, 2), read(next, 0), read(next, 1)};
        curves_.emplace_back(controls, budget); total_ += curves_.back().length(); finite_float(total_); lengths_.push_back(total_);
    }
    if (!constant_speed) {
        bool authored = stored.size() == knots; float previous = 0;
        if (authored) for (std::size_t i = 0; i < count; ++i) { if (!std::isfinite(stored[i]) || stored[i] < previous) { authored = false; break; } previous = stored[i]; }
        if (authored) for (std::size_t i = 0; i < count; ++i) lengths_[i] = stored[i];
        else {
            double cumulative = 0;
            for (std::size_t i = 0; i < count; ++i) {
                const auto& controls = curves_[i].controls(); pg::Controls p;
                for (std::size_t j = 0; j < 4; ++j) p[j] = pg::difference(controls[j], controls[0]);
                pg::Point previous_point{0, 0};
                for (unsigned step = 1; step <= 24; ++step) {
                    const auto t = step / 24.0, u = 1 - t, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                    const pg::Point point{b * p[1].x + c * p[2].x + d * p[3].x, b * p[1].y + c * p[2].y + d * p[3].y};
                    cumulative += pg::difference(point, previous_point).length(); previous_point = point;
                }
                if (cumulative > std::numeric_limits<float>::max()) throw pg::failure(); lengths_[i] = finite_float(cumulative);
            }
        }
        total_ = lengths_.back();
    }
}
std::optional<PathSample> PathSampler::at(float distance) const {
    if (curves_.empty()) return {}; double value = finite_float(distance);
    if (closed_ && total_ > 0) value = modulo(value, total_);
    else if (!closed_ && (value < 0 || value > total_)) {
        const auto& curve = value < 0 ? curves_.front() : curves_.back(); const auto t = value < 0 ? 0.0 : 1.0, extra = value < 0 ? value : value - total_;
        const auto point = curve.point(t), tangent = curve.tangent(t); const auto norm = tangent.length();
        return output({point.x + extra * tangent.x / norm, point.y + extra * tangent.y / norm}, tangent);
    }
    const auto position = std::lower_bound(lengths_.begin(), lengths_.end(), value);
    const auto index = std::min(static_cast<std::size_t>(position - lengths_.begin()), curves_.size() - 1);
    const auto start = index == 0 ? 0 : lengths_[index - 1], span = lengths_[index] - start; const auto& curve = curves_[index];
    const auto t = span == 0 ? 0 : constant_speed_ ? curve.parameter_at_distance(value - start) : std::clamp((value - start) / span, 0.0, 1.0);
    return output(curve.point(t), curve.tangent(t));
}
std::optional<float> PathSampler::project_distance(Point point) const {
    if (curves_.empty()) return {}; const pg::Point target{point.x, point.y}; pg::Budget budget;
    double best_error = std::numeric_limits<double>::infinity(), best_distance = 0;
    const auto consider = [&](pg::Point p, double distance) {
        const auto error = pg::difference(target, p).length(); if (error < best_error || (error == best_error && distance < best_distance)) { best_error = error; best_distance = distance; }
    };
    for (std::size_t i = 0; i < curves_.size(); ++i) {
        const auto& curve = curves_[i]; const auto t = curve.closest_parameter(target, budget), start = i == 0 ? 0 : lengths_[i - 1];
        consider(curve.point(t), start + (constant_speed_ ? curve.distance_at_parameter(t) : t * (lengths_[i] - start)));
    }
    if (!closed_) for (unsigned end = 0; end < 2; ++end) {
        const auto& curve = end == 0 ? curves_.front() : curves_.back(); const auto p = curve.point(end), tangent = curve.tangent(end); const auto norm = tangent.length();
        const auto ux = tangent.x / norm, uy = tangent.y / norm, along = (target.x - p.x) * ux + (target.y - p.y) * uy;
        if ((end == 0 && along < 0) || (end == 1 && along > 0)) consider({p.x + along * ux, p.y + along * uy}, end == 0 ? along : total_ + along);
    }
    return finite_float(closed_ && total_ > 0 ? modulo(best_distance, total_) : best_distance);
}
}
