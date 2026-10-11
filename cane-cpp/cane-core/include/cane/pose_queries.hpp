#pragma once
#include "runtime_frame.hpp"

namespace cane {
struct SetupBone {
    std::string id;
    std::optional<std::string> parent_id;
    BoneLocal local;
    TransformMode transform_mode = TransformMode::inherit_all;
};
struct BoneLocalState {
    std::string bone_id;
    BoneLocal local;
    TransformMode transform_mode = TransformMode::inherit_all;
};
struct SlotState {
    std::string slot_id;
    std::optional<std::string> attachment_key, attachment_id;
    FinalTint tint;
    // Position in sampled slot order, including non-renderable and hidden slots.
    std::uint32_t draw_index = 0;
};
struct PointAttachmentPose {
    std::string attachment_id;
    Affine matrix;
    float x = 0, y = 0, rotation_degrees = 0;
    // SDK metadata from the same publication, not extra normalized wire fields.
    std::string slot_id;
    bool active = true, selected = false;
};
enum class AttachmentGeometryKind { path, bounding_box, clipping };
struct AttachmentGeometry {
    std::string attachment_id;
    AttachmentGeometryKind kind = AttachmentGeometryKind::path;
    std::vector<float> world_vertices_xy;
    std::optional<bool> closed;
};
}
