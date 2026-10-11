#pragma once
#include "clip.hpp"
#include "cane/error.hpp"
#include <array>
#include <cmath>
#include <cstring>
#include <limits>

namespace cane::animation {
class SequenceUnits {
    std::array<std::uint32_t, 9> words_{};
public:
    static std::pair<std::uint32_t, unsigned> parts(float value) {
        std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); bits &= 0x7fffffff;
        const auto exponent = bits >> 23, fraction = bits & 0x7fffff;
        return exponent == 0 ? std::pair<std::uint32_t, unsigned>{fraction, 0}
            : std::pair<std::uint32_t, unsigned>{fraction | 0x800000, exponent - 1};
    }
    static SequenceUnits from(float value) {
        const auto [significand, shift] = parts(value); SequenceUnits result;
        const auto index = shift / 32; const auto wide = std::uint64_t(significand) << (shift % 32);
        result.words_[index] = static_cast<std::uint32_t>(wide);
        if (index + 1 < result.words_.size()) result.words_[index + 1] = static_cast<std::uint32_t>(wide >> 32);
        return result;
    }
    SequenceUnits subtract(const SequenceUnits& other) const {
        SequenceUnits result; std::uint64_t borrow = 0;
        for (std::size_t i = 0; i < words_.size(); ++i) {
            const std::uint64_t left = words_[i], right = std::uint64_t(other.words_[i]) + borrow;
            result.words_[i] = static_cast<std::uint32_t>(left - right); borrow = left < right ? 1 : 0;
        }
        return result;
    }
    SequenceUnits shift_right(unsigned shift) const {
        SequenceUnits result; const auto whole = shift / 32, bits = shift % 32;
        for (std::size_t i = 0; i < words_.size(); ++i) {
            const auto source = i + whole;
            if (source < words_.size()) {
                result.words_[i] = words_[source] >> bits;
                if (bits != 0 && source + 1 < words_.size()) result.words_[i] |= words_[source + 1] << (32 - bits);
            }
        }
        return result;
    }
    SequenceUnits divide(std::uint32_t divisor) const {
        SequenceUnits result; std::uint64_t remainder = 0;
        for (std::size_t i = words_.size(); i-- > 0;) {
            const auto value = (remainder << 32) | words_[i];
            result.words_[i] = static_cast<std::uint32_t>(value / divisor); remainder = value % divisor;
        }
        return result;
    }
    int compare(const SequenceUnits& other) const {
        for (std::size_t i = words_.size(); i-- > 0;) {
            if (words_[i] < other.words_[i]) return -1;
            if (words_[i] > other.words_[i]) return 1;
        }
        return 0;
    }
    std::optional<std::uint64_t> narrow() const {
        for (std::size_t i = 2; i < words_.size(); ++i) if (words_[i] != 0) return {};
        return std::uint64_t(words_[0]) | (std::uint64_t(words_[1]) << 32);
    }
    std::uint64_t modulo(std::uint64_t divisor) const {
        std::uint64_t remainder = 0;
        for (std::size_t i = words_.size(); i-- > 0;)
            for (unsigned bit = 32; bit-- > 0;) remainder = (remainder * 2 + ((words_[i] >> bit) & 1)) % divisor;
        return remainder;
    }
};

inline bool periodic_reset_crossed(float from, float to, float offset, float period) {
    if (!(to > from) || !(period > 0) || to < offset) return false;
    if (from < offset) return true;
    const auto origin = SequenceUnits::from(offset);
    const auto [significand, shift] = SequenceUnits::parts(period);
    const auto first = SequenceUnits::from(from).subtract(origin).shift_right(shift).divide(significand);
    const auto last = SequenceUnits::from(to).subtract(origin).shift_right(shift).divide(significand);
    return last.compare(first) > 0;
}

inline std::optional<std::uint32_t> sample_sequence_index(float time, float start, float interval,
    std::size_t count, std::uint32_t initial, SequenceMode mode) {
    if (!std::isfinite(time) || !std::isfinite(start) || !std::isfinite(interval) || time < 0 || start < 0 || interval < 0
        || (interval == 0 && mode != SequenceMode::fixed_frame) || count == 0 || initial >= count
        || count > std::numeric_limits<std::uint32_t>::max())
        throw Error(ErrorCode::invalid_argument, "sampleSequence", "Invalid sequence interval, index, or time.");
    if (time < start) return {};
    if (mode == SequenceMode::fixed_frame || count == 1) return initial;
    const auto [significand, shift] = SequenceUnits::parts(interval);
    const auto steps = SequenceUnits::from(time).subtract(SequenceUnits::from(start)).shift_right(shift).divide(significand);
    const auto last = std::uint64_t(count - 1); std::uint64_t forward;
    switch (mode) {
        case SequenceMode::play_once: case SequenceMode::play_once_reverse: {
            const auto value = steps.narrow(); forward = value && *value < last - initial ? initial + *value : last; break;
        }
        case SequenceMode::repeat: case SequenceMode::repeat_reverse: forward = (steps.modulo(count) + initial) % count; break;
        case SequenceMode::back_and_forth_loop: case SequenceMode::back_and_forth_loop_reverse: {
            const auto period = 2 * last, position = (steps.modulo(period) + initial) % period;
            forward = std::min(position, period - position); break;
        }
        default: throw Error(ErrorCode::invalid_argument, "sampleSequence", "Unknown sequence mode.");
    }
    const bool reverse = mode == SequenceMode::play_once_reverse || mode == SequenceMode::repeat_reverse || mode == SequenceMode::back_and_forth_loop_reverse;
    return static_cast<std::uint32_t>(reverse ? last - forward : forward);
}
}
