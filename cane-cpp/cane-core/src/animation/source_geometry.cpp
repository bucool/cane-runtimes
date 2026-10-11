#include "pose.hpp"
#include "geometry/deformation.hpp"
#include "geometry/mesh_placement.hpp"

namespace cane::animation {
void Pose::source_vertices(std::size_t index, std::vector<float>& output) const {
    const auto& deform = deforms[model().attachments.at(index).deform_owner];
    source_vertices(index, output, deform.enabled ? &deform.values : nullptr);
}
void Pose::source_vertices(std::size_t index, std::vector<float>& output, const std::vector<float>* deform, bool include_placement) const {
    const auto& m = model(); const auto& a = m.attachments.at(index); const auto& geometry = m.attachments[a.geometry_owner].geometry;
    const auto placement = include_placement ? cane::geometry::mesh_placement_world(a.placement, host_root) : Point{};
    const auto finite = [&](Point point) {
        if (!std::isfinite(point.x) || !std::isfinite(point.y)) throw Error(ErrorCode::non_finite, "apply", "Attachment source geometry is non-finite.", "vertices", data_.catalog(RuntimeCatalogKind::attachment)[index].id);
        return point;
    };
    const auto store = [&](std::size_t point, Point value) {
        value = cane::geometry::place_mesh_point(finite(value), placement);
        output[2 * point] = value.x; output[2 * point + 1] = value.y;
    };
    if (a.kind == detail::AttachmentKind::region) {
        const auto image = selected_image(index); if (!image) throw Error(ErrorCode::missing_resource, "apply", "Region has no image.");
        const auto& dimensions = data_.images().at(*image); const auto matrix = attachment_affine(index);
        const auto half_width = static_cast<float>(dimensions.width) * .5f, half_height = static_cast<float>(dimensions.height) * .5f;
        output.resize(8);
        store(0, matrix.transform({-half_width, half_height})); store(1, matrix.transform({half_width, half_height}));
        store(2, matrix.transform({half_width, -half_height})); store(3, matrix.transform({-half_width, -half_height}));
        return;
    }
    if (deform && deform->size() != geometry.deform_components())
        throw Error(ErrorCode::invalid_state, "apply", "Canonical deform count differs from source geometry.", "values", data_.catalog(RuntimeCatalogKind::attachment)[index].id);
    output.resize(geometry.positions.size());
    if (!geometry.weighted()) {
        const auto& positions = deform ? *deform : geometry.positions;
        const auto& matrix = world[m.slots[a.slot].bone];
        for (std::size_t i = 0; i < positions.size() / 2; ++i) store(i, matrix.transform({positions[2 * i], positions[2 * i + 1]}));
        return;
    }
    for (std::size_t i = 0; i < geometry.positions.size() / 2; ++i) {
        Point sum{}; float total = 0;
        for (std::size_t j = geometry.influence_starts[i]; j < geometry.influence_starts[i + 1]; ++j) {
            const auto& influence = geometry.influences[j];
            if (influence.weight == 0) continue; // Its offset pair still occupies j * 2.
            auto local = influence.setup_local;
            if (deform) { local.x += (*deform)[2 * j]; local.y += (*deform)[2 * j + 1]; }
            const auto point = finite(world[influence.bone].transform(local));
            sum.x += point.x * influence.weight; sum.y += point.y * influence.weight; total += influence.weight;
        }
        if (total <= matrix_epsilon || !std::isfinite(total)) throw Error(ErrorCode::validation_failed, "apply", "Weighted vertex has no finite positive influence total.", "weights");
        store(i, {sum.x / total, sum.y / total});
    }
}
}
