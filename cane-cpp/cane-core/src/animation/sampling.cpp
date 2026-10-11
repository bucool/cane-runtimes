#include "endpoint_pose.hpp"
#include "sequence_clock.hpp"
#include "geometry/deformation.hpp"

namespace cane::animation {
namespace {
float& component(BoneLocal& local, std::size_t i) {
    switch (i) {
        case 0: return local.x; case 1: return local.y; case 2: return local.rotation_degrees;
        case 3: return local.scale_x; case 4: return local.scale_y; case 5: return local.shear_x_degrees;
        default: return local.shear_y_degrees;
    }
}
std::uint8_t color_byte(float value) { return static_cast<std::uint8_t>(std::floor(std::clamp(value, 0.0f, 1.0f) * 255.0f + .5f)); }
bool sampled_dark(const DiscreteChannel<bool>& channel, float time) {
    const auto& keys = channel.keys();
    const auto next = std::upper_bound(keys.begin(), keys.end(), time, [](float t, const DiscreteKey<bool>& key) { return t < key.time; });
    if (next == keys.begin()) return false;
    const auto& current = *std::prev(next);
    return current.value || (time != current.time && next != keys.end() && next->value);
}
std::vector<ConstraintProperty> constraint_fields(ConstraintKind kind) {
    using P = ConstraintProperty;
    switch (kind) {
        case ConstraintKind::ik: return {P::target_x, P::target_y, P::mix, P::softness, P::bend_positive, P::compress, P::stretch};
        case ConstraintKind::transform: return {P::mix_rotate, P::mix_x, P::mix_y, P::mix_scale_x, P::mix_scale_y, P::mix_shear_y};
        case ConstraintKind::path: return {P::position, P::spacing, P::mix_rotate, P::mix_x, P::mix_y};
        case ConstraintKind::physics: return {P::inertia, P::strength, P::damping, P::mass, P::wind, P::gravity, P::mix};
        case ConstraintKind::slider: return {P::slider_time, P::mix};
    }
    return {};
}
}
std::size_t EndpointPose::add(PropertyKey key, PoseChannelKind kind, PoseValue setup, PoseValue current,
        std::function<void(const PoseValue&)> setter, float minimum, float maximum) {
    work_.use(1 + setup.components.size());
    const auto index = schema.size(); keys_.emplace(key, index);
    schema.push_back({kind, std::move(setup), minimum, maximum}); initial.push_back(std::move(current)); publish_.push_back(std::move(setter));
    return index;
}
EndpointPose::EndpointPose(Pose& pose, AnimationWork& work) : pose_(pose), work_(work) {
    using D = PropertyDomain; using K = PoseChannelKind; using V = PoseValue;
    const auto& m = pose.model();
    for (std::size_t i = 0; i < pose.locals.size(); ++i) {
        auto setup = pose.data().bones()[i].setup;
        for (std::size_t j = 0; j < 7; ++j) add({D::bone,i,j}, j == 2 || j >= 5 ? K::angle : K::scalar,
            V::numeric(component(setup,j)), V::numeric(component(pose.locals[i],j)), [&pose,i,j](const V& v) { component(pose.locals[i],j) = v.number; });
        add({D::bone,i,7}, K::choice, V::discrete(pose.data().bones()[i].transform_mode), V::discrete(pose.modes[i]), [&pose,i](const V& v) { pose.modes[i] = std::get<TransformMode>(v.choice); });
    }
    for (std::size_t i = 0; i < pose.slots.size(); ++i) {
        const auto& setup = m.slots[i]; const auto& current = pose.slots[i];
        for (std::size_t j = 0; j < 6; ++j) {
            const bool dark = j >= 3; const auto c = j % 3;
            const auto bas = dark ? setup.dark.value_or(detail::Rgb{}) : setup.color;
            const auto cur = dark ? current.dark.value_or(detail::Rgb{}) : current.color;
            add({D::slot,i,j}, K::bounded, V::numeric(static_cast<float>(bas[c])/255), V::numeric(static_cast<float>(cur[c])/255), [&pose,i,c,dark](const V& v) {
                auto& slot = pose.slots[i];
                if (dark) { if (!slot.dark) slot.dark = detail::Rgb{}; (*slot.dark)[c] = color_byte(v.number); }
                else slot.color[c] = color_byte(v.number);
            });
        }
        add({D::slot,i,6}, K::bounded, V::numeric(setup.alpha), V::numeric(current.alpha), [&pose,i](const V& v) { pose.slots[i].alpha = v.number; });
        add({D::slot,i,7}, K::choice, V::discrete(setup.attachment_key), V::discrete(current.key), [&pose,i](const V& v) { pose.slots[i].key = std::get<std::optional<std::string>>(v.choice); });
        add({D::slot,i,8}, K::choice, V::discrete(setup.dark.has_value()), V::discrete(current.dark.has_value()), [&pose,i](const V& v) {
            if (!std::get<bool>(v.choice)) pose.slots[i].dark.reset();
            else if (!pose.slots[i].dark) pose.slots[i].dark = detail::Rgb{};
        });
    }
    for (std::size_t i = 0; i < m.attachments.size(); ++i) {
        const auto& a = m.attachments[i]; auto setup = a.local;
        if (a.kind == detail::AttachmentKind::region) for (std::size_t j = 0; j < 5; ++j)
            add({D::attachment,i,j}, j == 2 ? K::angle : K::scalar, V::numeric(component(setup,j)), V::numeric(component(pose.regions[i],j)), [&pose,i,j](const V& v) { component(pose.regions[i],j) = v.number; });
        if (a.kind != detail::AttachmentKind::region && a.kind != detail::AttachmentKind::point && a.deform_owner == i) {
            const auto& source = m.attachments[a.geometry_owner].geometry;
            auto baseline = source.weighted() ? std::vector<float>(source.deform_components(),0) : source.positions;
            auto current = pose.deforms[i].enabled ? pose.deforms[i].values : baseline;
            add({D::attachment,i,5}, K::vector, V::vector(std::move(baseline)), V::vector(std::move(current)), [&pose,i](const V& v) { pose.deforms[i].values = v.components; pose.deforms[i].enabled = true; });
        }
        if (!a.sequence_images.empty()) {
            const auto setup_index = std::min(a.sequence_setup, static_cast<std::uint32_t>(a.sequence_images.size()-1));
            add({D::attachment,i,6}, K::choice, V::discrete(setup_index), V::discrete(pose.sequence_indices[i]), [&pose,i](const V& v) { pose.sequence_indices[i] = std::get<std::uint32_t>(v.choice); });
        }
    }
    for (std::size_t i = 0; i < m.constraints.size(); ++i) {
        const auto& definition = m.constraints[i];
        for (const auto property : constraint_fields(definition.kind)) {
            const auto field = static_cast<std::size_t>(property);
            if (field >= detail::scalar_parameter_count) {
                const auto boolean = field - detail::scalar_parameter_count;
                add({D::constraint,i,field}, K::choice, V::discrete(definition.setup.booleans.at(boolean)), V::discrete(pose.constraints[i].booleans.at(boolean)), [&pose,i,boolean](const V& v) { pose.constraints[i].booleans.at(boolean) = std::get<bool>(v.choice); });
            } else {
                using P = ConstraintProperty;
                const bool bounded = definition.kind == ConstraintKind::physics && (property == P::mix || property == P::inertia || property == P::damping || property == P::mass);
                const bool mass = property == P::mass;
                add({D::constraint,i,field}, bounded ? K::bounded : K::scalar, V::numeric(definition.setup[property]), V::numeric(pose.constraints[i][property]),
                    [&pose,i,property](const V& v) { pose.constraints[i][property] = v.number; }, mass ? std::numeric_limits<float>::denorm_min() : 0, mass ? std::numeric_limits<float>::max() : 1);
            }
        }
    }
    add({D::skin,0,0}, K::choice, V::discrete(std::vector<std::size_t>{}), V::discrete(pose.skins), [&pose](const V& v) { pose.skins = std::get<std::vector<std::size_t>>(v.choice); });
    order_index = add({D::draw_order,0,0}, K::order, V::discrete(m.setup_order), V::discrete(pose.order), [&pose](const V& v) { pose.order = std::get<std::vector<std::size_t>>(v.choice); });
    add({D::draw_order,0,1}, K::choice, V::discrete(false), V::discrete(pose.order_sampled), [&pose](const V& v) { pose.order_sampled = std::get<bool>(v.choice); });
}
ChannelSample EndpointPose::sample(const Layer* layer) {
    using D = PropertyDomain; using V = PoseValue;
    ChannelSample result;
    if (layer == nullptr) return result;
    const auto& clip = layer->clip; const auto& m = pose_.model();
    const float time = layer->time; const bool transparent = layer->alpha == 0;
    result.weight = layer->alpha; result.additive = layer->additive;
    const auto put = [&](PropertyKey key, PoseValue value) {
        work_.use(); const auto found = keys_.find(key);
        if (found == keys_.end()) throw Error(ErrorCode::invalid_argument, "apply", "Animation channel has no setup value.");
        result.values[found->second] = transparent ? schema[found->second].setup : std::move(value);
    };
    const auto scalar = [&](const ScalarChannel& channel, PropertyKey key, bool angular = false, bool color = false) {
        work_.use(channel.keys().size());
        if (channel.keys().empty() || channel.keys().front().time > time) return;
        if (transparent) { put(key, {}); return; }
        try {
            const auto value = channel.sample(time, angular, layer->stepped);
            if (value) put(key, V::numeric(color ? static_cast<float>(color_byte(*value))/255 : *value));
        } catch (Error& error) {
            if (error.operation == "sampleCurve") {
                const auto kind = key.domain == D::bone ? RuntimeCatalogKind::bone : key.domain == D::slot ? RuntimeCatalogKind::slot : key.domain == D::attachment ? RuntimeCatalogKind::attachment : RuntimeCatalogKind::constraint;
                error.operation = "apply"; error.field = "animationCurve"; error.entity_id = pose_.data().catalog(kind)[key.target].id;
            }
            throw;
        }
    };
    for (const auto& t : clip.bones) {
        for (std::size_t j = 0; j < t.channels.size(); ++j) scalar(t.channels[j], {D::bone,t.index,j}, j == 2 || j >= 5);
        work_.use(t.inherit.keys().size());
        if (const auto value = t.inherit.sample(time)) put({D::bone,t.index,7}, V::discrete(*value));
    }
    for (const auto& t : clip.slots) {
        if (layer->attachments) {
            work_.use(t.attachment.keys().size());
            if (const auto value = t.attachment.sample(time)) put({D::slot,t.index,7}, V::discrete(*value));
        }
        for (std::size_t j = 0; j < 6; ++j) scalar(t.color[j], {D::slot,t.index,j}, false, true);
        work_.use(t.dark_present.keys().size());
        if (!t.color[0].keys().empty() && t.color[0].keys().front().time <= time)
            put({D::slot,t.index,8}, V::discrete(!transparent && sampled_dark(t.dark_present,time)));
        scalar(t.alpha.keys().empty() ? t.color[6] : t.alpha, {D::slot,t.index,6});
    }
    for (const auto& t : clip.attachments) {
        const auto& a = m.attachments[t.index];
        for (std::size_t j = 0; j < t.region.size(); ++j) scalar(t.region[j], {D::attachment,t.index,j}, j == 2);
        for (const auto& key : t.deform.keys()) work_.use(1 + key.values.size());
        if (!t.deform.keys().empty() && t.deform.keys().front().time <= time) {
            if (transparent) put({D::attachment,t.index,5}, {});
            else {
                if (a.deform_owner != t.index) throw Error(ErrorCode::invalid_argument, "apply", "Invalid deformation owner.");
                std::vector<float> values(t.deform.components());
                if (t.deform.sample_into(time, values.data(), values.size(), layer->stepped)) {
                    const auto& geometry = m.attachments[a.geometry_owner].geometry;
                    if (geometry.weighted() && t.deform_space == DeformSpace::vertex_positions) {
                        std::vector<float> canonical(geometry.deform_components());
                        cane::geometry::positions_to_offsets(geometry, values.data(), canonical.data(), "apply", pose_.data().catalog(RuntimeCatalogKind::attachment)[t.index].id, "deform");
                        values = std::move(canonical);
                    }
                    put({D::attachment,t.index,5}, V::vector(std::move(values)));
                }
            }
        }
        if (layer->attachments) {
            work_.use(t.sequence.keys().size());
            if (const auto key = t.sequence.sample_key(time)) {
                std::uint32_t index = 0;
                if (!transparent) {
                    const auto sampled = sample_sequence_index(time,key->time,key->value.delay,a.sequence_images.size(),key->value.index,key->value.mode);
                    if (!sampled) throw Error(ErrorCode::invalid_argument, "apply", "Invalid sequence clock.");
                    index = *sampled;
                }
                put({D::attachment,t.index,6}, V::discrete(index));
            }
        }
    }
    for (const auto& t : clip.constraints) {
        const auto apply = [&](std::size_t index) {
            const auto& definition = m.constraints[index];
            for (const auto& channel : t.scalars) {
                const auto field = static_cast<std::size_t>(channel.property);
                if (!t.index && !std::get<detail::PhysicsDefinition>(definition.definition).globals[field]) continue;
                scalar(channel.values,{D::constraint,index,field});
            }
            for (const auto& channel : t.booleans) {
                work_.use(channel.values.keys().size());
                if (const auto value = channel.values.sample(time)) put({D::constraint,index,static_cast<std::size_t>(channel.property)},V::discrete(*value));
            }
        };
        if (t.index) apply(*t.index);
        else for (std::size_t i = 0; i < m.constraints.size(); ++i) if (m.constraints[i].kind == ConstraintKind::physics) apply(i);
    }
    if (layer->attachments) {
        work_.use(clip.skins.keys().size());
        if (const auto value = clip.skins.sample(time)) {
            std::vector<std::size_t> skins; if (*value) skins.push_back(**value);
            put({D::skin,0,0}, V::discrete(std::move(skins)));
        }
    }
    if (layer->draw_order) {
        const auto order = [&](const DiscreteChannel<std::vector<std::size_t>>& channel) {
            work_.use(channel.keys().size());
            if (const auto value = channel.sample(time)) {
                if (!transparent) result.order_patches.push_back(*value);
                put({D::draw_order,0,0},V::discrete(std::vector<std::size_t>{}));
                put({D::draw_order,0,1},V::discrete(true));
            }
        };
        order(clip.draw_order);
        for (const auto& folder : clip.draw_order_folders) order(folder.keys);
    }
    return result;
}
void EndpointPose::finish(const std::vector<PoseValue>& values) {
    work_.use(values.size());
    for (std::size_t i = 0; i < values.size(); ++i) publish_.at(i)(values[i]);
}
void Pose::apply(const Layer& layer) {
    if (!std::isfinite(layer.time) || !std::isfinite(layer.alpha)) throw Error(ErrorCode::non_finite, "apply", "Layer sample time and weight must be finite.");
    AnimationWork work; EndpointPose projection(*this,work);
    projection.finish(apply_channel_sample(projection.schema,projection.initial,projection.sample(&layer),projection.order_index,work));
}
}
