using System;
using System.Collections.Generic;

namespace Cane.Format
{
    // Run after normal shape/reference validation. The same feature inventory describes
    // effective resource documents; it does not certify an implementation or alter inputs.
    internal static class FeatureUsage
    {
        internal static SortedSet<string> Collect(Json document)
        {
            var features = RuntimeProfiles.Required(document);
            foreach (Json attachment in document["attachments"].Items)
            {
                string kind = attachment.S("type");
                features.Add("attachment." + (kind == "boundingbox" ? "bounding-box" : kind));
                if (!attachment["sequence"].IsNull) features.Add("attachment.sequence");
                if (attachment["weights"].ArrayOrEmpty.Count != 0) features.Add("mesh.weighted");
                if (kind == "mesh" && (attachment["placement"].F("x") != 0 || attachment["placement"].F("y") != 0)) features.Add("mesh.placement");
            }
            foreach (Json slot in document["slots"].Items)
            {
                string blend = slot.S("blendMode"); if (blend != "normal") features.Add("blend." + blend);
                if (!slot["darkColor"].IsNull) features.Add("tint.two-color");
            }
            foreach (Json constraint in document["constraints"].Items) features.Add(RuntimeProfiles.Constraint(constraint.S("type")));
            if (document["skins"].Items.Count != 0) features.Add("skin");
            if (document["events"].Items.Count != 0) features.Add("event");
            foreach (Json animation in document["animations"].Items)
            {
                foreach (Json timeline in animation["slotTimelines"].Items)
                    foreach (Json key in timeline["color"].ArrayOrEmpty)
                        if (!key["darkColor"].IsNull) features.Add("tint.two-color");
                foreach (Json timeline in animation["attachmentTimelines"].Items)
                    if (timeline["deform"].ArrayOrEmpty.Count != 0) features.Add("mesh.deform");
                if (animation["events"].Items.Count != 0) features.Add("event");
                if (animation["drawOrder"].Items.Count != 0 || animation["drawOrderFolders"].Items.Count != 0) features.Add("timeline.draw-order");
                if (animation["skins"].Items.Count != 0) features.Add("timeline.skin");
            }
            return features;
        }
        internal static void ValidateDeclared(Json document)
        {
            RuntimeProfiles.Validate(document);
            var declared = new HashSet<string>(document["requiredFeatures"].Strings(), StringComparer.Ordinal);
            SortedSet<string> used = Collect(document);
            if (used.Contains("mesh.placement") && document["runtimeApiVersion"].I("minor") < 13)
                throw new RuntimeException(RuntimeErrorCode.UnsupportedVersion, "loadJson", "Nonzero mesh placement requires Runtime API 1.13.", "runtimeApiVersion");
            if (used.Contains("mesh.placement") != declared.Contains("mesh.placement"))
                Validation.Fail("mesh.placement must be declared exactly when a mesh has nonzero placement.", "requiredFeatures");
            foreach (string feature in used)
                if (!declared.Contains(feature)) Validation.Fail("Document uses an undeclared feature: " + feature, "requiredFeatures");
        }
        internal static void IncludeOverlayRequirements(Json document)
        {
            SortedSet<string> features = Collect(document); bool placed = features.Contains("mesh.placement");
            features.UnionWith(document["requiredFeatures"].Strings());
            if (placed) document["runtimeApiVersion"].Members["minor"] = new Json((long)Math.Max(13, document["runtimeApiVersion"].I("minor")));
            else features.Remove("mesh.placement");
            var values = new List<Json>(features.Count); foreach (string feature in features) values.Add(new Json(feature));
            document.Members["requiredFeatures"] = new Json(values);
        }
    }
}
