#pragma once
#include <cane/render_packet.hpp>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <string>
#include <type_traits>
#include <vector>

namespace cane_unreal {
// Identity is independent of the file name: distinct atlases may reuse page ids.
inline std::string resource_key(const std::string& image) { return "i" + image; }
inline std::string resource_key(const std::string& atlas, const std::string& page) {
    return "a" + std::to_string(atlas.size()) + ":" + atlas + page;
}
struct SurfaceRect { float left = -512, bottom = -512, width = 1024, height = 1024; };
struct Vertex { float x, y, u, v; };
struct Draw {
    std::string texture_key;
    std::uint32_t width = 0, height = 0;
    cane::RenderBlendMode blend = cane::RenderBlendMode::normal;
    cane::FinalTint tint;
    cane::ColorSpace color_space = cane::ColorSpace::srgb;
    cane::AlphaMode alpha_mode = cane::AlphaMode::straight;
    cane::TextureFilter min_filter = cane::TextureFilter::linear, mag_filter = cane::TextureFilter::linear;
    cane::TextureWrap wrap_u = cane::TextureWrap::clamp, wrap_v = cane::TextureWrap::clamp;
    std::vector<Vertex> vertices;
};
inline void validate_surface(const SurfaceRect& r) {
    if (!std::isfinite(r.left) || !std::isfinite(r.bottom) || !std::isfinite(r.width) ||
        !std::isfinite(r.height) || r.width <= 0 || r.height <= 0 ||
        !std::isfinite(r.left + r.width) || !std::isfinite(r.bottom + r.height))
        throw std::invalid_argument("Cane surface bounds must be finite and have positive extent.");
}
inline std::vector<Draw> project(const cane::RenderPacket& packet) {
    std::vector<Draw> result;
    result.reserve(packet.attachments().size());
    for (const auto& attachment : packet.attachments()) {
        if (attachment.world_vertices_xy.size() % 2 != 0 ||
            attachment.uvs.size() != attachment.world_vertices_xy.size() ||
            attachment.indices.size() % 3 != 0 ||
            attachment.indices.size() > std::numeric_limits<std::uint32_t>::max())
            throw std::invalid_argument("Invalid final Cane packet geometry.");
        Draw draw;
        draw.blend = attachment.blend;
        draw.tint = attachment.tint;
        std::visit([&](const auto& texture) {
            draw.width = texture.width; draw.height = texture.height;
            draw.color_space = texture.color_space; draw.alpha_mode = texture.alpha_mode;
            if constexpr (std::is_same_v<std::decay_t<decltype(texture)>, cane::AtlasTexture>) {
                draw.texture_key = resource_key(texture.atlas_id, texture.page_id);
                draw.min_filter = texture.min_filter; draw.mag_filter = texture.mag_filter;
                draw.wrap_u = texture.wrap_u; draw.wrap_v = texture.wrap_v;
            } else { draw.texture_key = resource_key(texture.image_id); }
        }, attachment.texture);
        draw.vertices.reserve(attachment.indices.size());
        for (const auto index : attachment.indices) {
            const auto p = static_cast<std::size_t>(index) * 2;
            if (p + 1 >= attachment.world_vertices_xy.size())
                throw std::out_of_range("Cane packet index is outside the final vertex buffer.");
            // The Core has already clipped, skinned and transformed these vertices.
            draw.vertices.push_back({attachment.world_vertices_xy[p], attachment.world_vertices_xy[p + 1],
                attachment.uvs[p], attachment.uvs[p + 1]});
        }
        if (!draw.vertices.empty()) result.push_back(std::move(draw));
    }
    // Preserve packet order. source_z_index and source_affine are diagnostic only.
    return result;
}
}
