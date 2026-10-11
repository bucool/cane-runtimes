#pragma once
#include "cane/math.hpp"
#include "cubic_arc.hpp"
#include <vector>

namespace cane::constraints {
struct PathSample { Point position; float tangent = 0; };

class PathSampler {
    std::vector<path_geometry::CubicArc> curves_;
    std::vector<double> lengths_;
    double total_ = 0;
    bool closed_ = false, constant_speed_ = true;
public:
    void reset(const std::vector<float>& world, bool closed, bool constant_speed, const std::vector<float>& lengths = {});
    [[nodiscard]] float length() const noexcept { return static_cast<float>(total_); }
    [[nodiscard]] bool available() const noexcept { return !curves_.empty(); }
    [[nodiscard]] bool closed() const noexcept { return closed_; }
    [[nodiscard]] std::optional<PathSample> at(float distance) const;
    [[nodiscard]] std::optional<float> project_distance(Point target) const;
};
}
