#include "cane/runtime_capabilities.hpp"
#include <algorithm>
#include <array>

namespace cane {
namespace {
constexpr std::array<std::string_view, 27> features{{
    "animation.curve.roots-v2",
    "animation.sequence.clock-v2",
    "animation.track.pose-v2",
    "attachment.bounding-box",
    "attachment.clipping",
    "attachment.mesh",
    "attachment.path",
    "attachment.point",
    "attachment.region",
    "attachment.sequence",
    "blend.add",
    "blend.multiply",
    "blend.screen",
    "bone.inheritance.polar-v2",
    "constraint.ik.affine-v2",
    "constraint.path.geometry-v2",
    "constraint.physics.spring-v2",
    "constraint.slider",
    "constraint.transform.channels-v2",
    "event",
    "mesh.deform",
    "mesh.placement",
    "mesh.weighted",
    "skin",
    "timeline.draw-order",
    "timeline.skin",
    "tint.two-color",
}};
static_assert([] {
    for (std::size_t i = 1; i < features.size(); ++i) if (!(features[i - 1] < features[i])) return false;
    return true;
}(), "Supported feature names must be sorted and unique.");
}
bool RuntimeVersionRange::contains(std::uint32_t major, std::uint32_t minor) const noexcept {
    return (major > minimum.major || (major == minimum.major && minor >= minimum.minor))
        && (major < maximum.major || (major == maximum.major && minor <= maximum.minor));
}
bool supports_runtime_feature(std::string_view name) noexcept { return std::binary_search(features.begin(), features.end(), name); }
RuntimeCapabilities runtime_capabilities() {
    RuntimeCapabilities result;
    result.implementation_name = "cane-cpp-runtime"; result.implementation_version = "0.1.0"; result.numeric_precision = "binary32";
    result.runtime_json = {{1, 1}, {1, 1}}; result.caneb = result.runtime_json; result.atlas = {{1, 0}, {1, 0}};
    result.runtime_api = {{1, 0}, {1, 13}};
    result.supported_runtime_features.assign(features.begin(), features.end());
    result.api_feature_bits = (std::uint64_t{1} << 53) - 1;
    result.last_passed_conformance = {"1.17.0", "f462e113115ecd60322f6f5d9df451e05d82649d0ded3278137f4b873e3ce0f9", 391};
    return result;
}
}
