#pragma once
#include "cane/error.hpp"
#include <algorithm>
#include <cmath>
#include <utility>

namespace cane::constraints {
inline double spring_exprel(double x) { return x == 0 ? 1 : std::expm1(x) / x; }
inline std::pair<double, double> advance_spring(double position, double velocity, double seconds, double acceleration, double stiffness, double drag) {
    const auto fail = [] { throw Error(ErrorCode::non_finite, "apply", "Spring state cannot be represented."); };
    if (!std::isfinite(position) || !std::isfinite(velocity) || !std::isfinite(seconds) || !std::isfinite(acceleration)
        || !std::isfinite(stiffness) || !std::isfinite(drag) || seconds < 0 || stiffness < 0 || drag < 0) fail();
    if (seconds == 0 || (velocity == 0 && acceleration == stiffness * position)) return {position, velocity};
    double next_position, next_velocity;
    if (std::max(drag * seconds, std::sqrt(stiffness) * seconds) < .5) {
        double pt = velocity * seconds, vt = (acceleration - stiffness * position - drag * velocity) * seconds, dp = 0, dv = 0;
        for (int order = 1; order <= 24; ++order) {
            dp += pt; dv += vt; const double interval = seconds / (order + 1), next = vt * interval;
            vt = (-stiffness * pt - drag * vt) * interval; pt = next;
        }
        next_position = position + dp; next_velocity = velocity + dv;
    } else {
        double h00, h01, h10, h11, forced;
        if (stiffness == 0) {
            const double decay = std::exp(-drag * seconds), integral = -std::expm1(-drag * seconds) / drag;
            h00 = 1; h01 = integral; h10 = 0; h11 = decay; forced = (seconds - integral) / drag;
        } else {
            const double half = drag * .5, discriminant = half * half - stiffness;
            if (discriminant >= 0) {
                const double gap = std::sqrt(discriminant), fast = -half - gap, slow = -stiffness / (half + gap);
                const double decay = std::exp(slow * seconds), response = decay * seconds * spring_exprel((fast - slow) * seconds);
                h00 = decay - slow * response; h01 = response; h10 = -stiffness * response;
                h11 = std::exp(fast * seconds) + slow * response; forced = (seconds * spring_exprel(slow * seconds) - response) / -fast;
            } else {
                const double frequency = std::sqrt(-discriminant), phase = frequency * seconds, decay = std::exp(-half * seconds);
                const double response = decay * std::sin(phase) / frequency, cosine = decay * std::cos(phase), sine_half = std::sin(phase * .5);
                h00 = cosine + half * response; h01 = response; h10 = -stiffness * response; h11 = cosine - half * response;
                forced = (-std::expm1(-half * seconds) + decay * 2 * sine_half * sine_half - half * response) / stiffness;
            }
        }
        next_position = h00 * position + h01 * velocity + forced * acceleration;
        next_velocity = h10 * position + h11 * velocity + h01 * acceleration;
    }
    if (!std::isfinite(next_position) || !std::isfinite(next_velocity)) fail();
    return {next_position, next_velocity};
}
}
