using System;
using System.Collections.Generic;

namespace Cane.Format
{
    internal static class RuntimeProfiles
    {
        internal static readonly Dictionary<string,int> Minimum = new Dictionary<string,int> {
            ["constraint.physics.spring-v2"]=4,["constraint.path.geometry-v2"]=5,["constraint.ik.affine-v2"]=6,
            ["constraint.transform.channels-v2"]=7,["bone.inheritance.polar-v2"]=8,["animation.curve.roots-v2"]=9,
            ["animation.track.pose-v2"]=10,["animation.sequence.clock-v2"]=11,["mesh.placement"]=13,
        };
        private static readonly Dictionary<string,string[]> Families = new Dictionary<string,string[]> {
            ["boneTimelines"]=new[]{"translate","translateX","translateY","rotate","scale","scaleX","scaleY","shear","shearX","shearY"},
            ["slotTimelines"]=new[]{"color","alpha"},["attachmentTimelines"]=new[]{"region","deform"},["constraintTimelines"]=new[]{"keys"},
        };
        internal static string Constraint(string kind)
        {
            switch(kind){case "physics":return "constraint.physics.spring-v2";case "path":return "constraint.path.geometry-v2";
                case "ik":return "constraint.ik.affine-v2";case "transform":return "constraint.transform.channels-v2";default:return "constraint."+kind;}
        }
        private static bool Special(string mode) => mode=="ignoreRotationAndReflection" || mode=="ignoreScale" || mode=="ignoreScaleAndReflection";
        private static bool Cubic(Json curve)
        {
            if(!curve.IsObject)return false;string type=curve.S("type");
            if(type=="bezier" || type=="bezier-value")return true;
            if(type!="properties")return false;
            if(Cubic(curve["default"]))return true;
            if(curve["properties"].IsObject)foreach(var p in curve["properties"].Members)if(Cubic(p.Value))return true;
            return false;
        }
        internal static SortedSet<string> Required(Json document)
        {
            var result=new SortedSet<string>(StringComparer.Ordinal);
            foreach(Json c in document["constraints"].Items)if(c.S("type")!="slider")result.Add(Constraint(c.S("type")));
            foreach(Json b in document["bones"].Items)if(Special(b.S("transformMode")))result.Add("bone.inheritance.polar-v2");
            foreach(Json a in document["animations"].Items)
            {
                bool pose=a["skins"].Items.Count!=0 || a["drawOrder"].Items.Count!=0;
                foreach(Json folder in a["drawOrderFolders"].Items)pose |= folder["keys"].Items.Count!=0;
                foreach(var family in Families)foreach(Json t in a[family.Key].Items)foreach(string field in family.Value)
                {
                    var keys=t[field].ArrayOrEmpty;pose |= keys.Count!=0;
                    foreach(Json key in keys)if(Cubic(key["curve"]))result.Add("animation.curve.roots-v2");
                }
                foreach(Json t in a["boneTimelines"].Items)
                {pose |= t["inherit"].ArrayOrEmpty.Count!=0;foreach(Json key in t["inherit"].ArrayOrEmpty)if(Special(key.S("inherit")))result.Add("bone.inheritance.polar-v2");}
                foreach(Json t in a["slotTimelines"].Items)pose |= t["attachment"].ArrayOrEmpty.Count!=0;
                foreach(Json t in a["attachmentTimelines"].Items)
                {pose |= t["sequence"].ArrayOrEmpty.Count!=0;foreach(Json key in t["sequence"].ArrayOrEmpty)if(key.S("mode")!="fixedFrame")result.Add("animation.sequence.clock-v2");}
                if(pose)result.Add("animation.track.pose-v2");
            }
            return result;
        }
        internal static void Validate(Json document)
        {
            int minor=document["runtimeApiVersion"].I("minor");
            if(minor!=0 && (minor<4 || minor>13))throw new RuntimeException(RuntimeErrorCode.UnsupportedVersion,"loadJson","Unsupported Runtime API document version.","runtimeApiVersion");
            foreach(string name in Required(document))if(minor<Minimum[name])throw new RuntimeException(RuntimeErrorCode.UnsupportedFeature,"load","Playback requires "+name+" and its API version.","runtimeApiVersion");
            foreach(string name in document["requiredFeatures"].Strings())if(Minimum.TryGetValue(name,out int required) && minor<required)Validation.Fail("Required profile is newer than the declared API.","runtimeApiVersion");
        }
    }
}
