#pragma once
#include "format.hpp"
#include "cane/error.hpp"
#include <map>
#include <set>

namespace cane::format {
inline const std::map<std::string, unsigned>& profile_versions() {
    static const std::map<std::string, unsigned> values{
        {"constraint.physics.spring-v2",4},{"constraint.path.geometry-v2",5},{"constraint.ik.affine-v2",6},
        {"constraint.transform.channels-v2",7},{"bone.inheritance.polar-v2",8},{"animation.curve.roots-v2",9},
        {"animation.track.pose-v2",10},{"animation.sequence.clock-v2",11},{"mesh.placement",13}
    };
    return values;
}
inline std::string constraint_profile(std::string_view kind) {
    if (kind == "physics") return "constraint.physics.spring-v2";
    if (kind == "path") return "constraint.path.geometry-v2";
    if (kind == "ik") return "constraint.ik.affine-v2";
    if (kind == "transform") return "constraint.transform.channels-v2";
    return "constraint." + std::string(kind);
}
inline const Json& profile_member(const Json& value, const char* key) {
    static const Json absent;
    if (!value.is_object()) return absent;
    const auto at = value.find(key); return at == value.end() ? absent : *at;
}
inline bool special_inheritance(const Json& value) {
    return value == "ignoreRotationAndReflection" || value == "ignoreScale" || value == "ignoreScaleAndReflection";
}
inline bool profile_cubic(const Json& curve) {
    if (!curve.is_object()) return false;
    const auto& type = profile_member(curve,"type");
    if (type == "bezier" || type == "bezier-value") return true;
    if (type != "properties") return false;
    if (profile_cubic(profile_member(curve,"default"))) return true;
    for (const auto& value : profile_member(curve,"properties")) if (profile_cubic(value)) return true;
    return false;
}
inline std::set<std::string> required_profiles(const Json& document) {
    std::set<std::string> result;
    for (const auto& c : document.at("constraints")) if (c.at("type") != "slider") result.insert(constraint_profile(c.at("type").get<std::string>()));
    for (const auto& b : document.at("bones")) if (special_inheritance(b.at("transformMode"))) result.insert("bone.inheritance.polar-v2");
    static const std::map<std::string,std::vector<const char*>> families{
        {"boneTimelines",{"translate","translateX","translateY","rotate","scale","scaleX","scaleY","shear","shearX","shearY"}},
        {"slotTimelines",{"color","alpha"}},{"attachmentTimelines",{"region","deform"}},{"constraintTimelines",{"keys"}}
    };
    for (const auto& a : document.at("animations")) {
        bool pose = !a.at("skins").empty() || !a.at("drawOrder").empty();
        for (const auto& folder : a.at("drawOrderFolders")) pose = pose || !folder.at("keys").empty();
        for (const auto& family : families) for (const auto& t : a.at(family.first)) for (const auto* field : family.second) {
            const auto& keys = profile_member(t,field); pose = pose || !keys.empty();
            for (const auto& key : keys) if (profile_cubic(profile_member(key,"curve"))) result.insert("animation.curve.roots-v2");
        }
        for (const auto& t : a.at("boneTimelines")) {
            const auto& keys = profile_member(t,"inherit"); pose = pose || !keys.empty();
            for (const auto& key : keys) if (special_inheritance(key.at("inherit"))) result.insert("bone.inheritance.polar-v2");
        }
        for (const auto& t : a.at("slotTimelines")) pose = pose || !profile_member(t,"attachment").empty();
        for (const auto& t : a.at("attachmentTimelines")) {
            const auto& keys = profile_member(t,"sequence"); pose = pose || !keys.empty();
            for (const auto& key : keys) if (key.at("mode") != "fixedFrame") result.insert("animation.sequence.clock-v2");
        }
        if (pose) result.insert("animation.track.pose-v2");
    }
    return result;
}
inline std::set<std::string> validate_profiles(const Json& document, const std::string& operation) {
    const auto minor = document.at("runtimeApiVersion").at("minor").get<unsigned>();
    if (minor != 0 && (minor < 4 || minor > 13)) throw Error(ErrorCode::unsupported_version,operation,"Unsupported Runtime API document version.","runtimeApiVersion");
    auto required = required_profiles(document);
    for (const auto& name : required) if (minor < profile_versions().at(name))
        throw Error(ErrorCode::unsupported_feature,"load","Playback requires " + name + " and its API version.","runtimeApiVersion");
    for (const auto& feature : document.at("requiredFeatures")) {
        const auto at = profile_versions().find(feature.get<std::string>());
        if (at != profile_versions().end() && minor < at->second) throw Error(ErrorCode::validation_failed,operation,"Required profile is newer than the declared API.","runtimeApiVersion");
    }
    return required;
}
}
