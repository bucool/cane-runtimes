using System;
using System.Collections.Generic;
using Cane.Format;

namespace Cane.Animation
{
    internal sealed class Layer
    {
        internal Clip Clip = null!;
        internal float Time, Alpha;
        internal bool Additive, Attachments = true, DrawOrder = true, Stepped;
        internal Func<string, float>? PropertyAlpha;
        internal float Weight(string property) => PropertyAlpha == null ? Alpha : PropertyAlpha(property);
    }

    internal static class Sampling
    {
        internal static void Apply(Pose pose, Layer layer) => EndpointPose.ApplyLayer(pose, layer);
        internal static bool SampledDarkExists(Json keys, float time)
        {
            IReadOnlyList<Json> rows = keys.ArrayOrEmpty; int low = 0, high = rows.Count;
            while (low < high) { int mid = low + (high - low) / 2; if (rows[mid].F("time") <= time) low = mid + 1; else high = mid; }
            if (low == 0) return false; Json current = rows[low - 1];
            return !current["darkColor"].IsNull || time != current.F("time") && low < rows.Count && !rows[low]["darkColor"].IsNull;
        }
        internal static float ConstraintValue(Json c, string field)
        {
            if (!c[field].IsNull) return c.F(field);
            if (field.StartsWith("mix", StringComparison.Ordinal) || field == "inertia" || field == "damping" || field == "mass" || field == "timeScale") return 1;
            if (field == "strength") return 100; if (field == "fps") return 60; if (field == "limit") return 5000; return 0;
        }
        internal static float[] CanonicalDeform(AttachmentData source, float[] positions)
            => Geometry.Deformation.PositionsToOffsets(source, positions, "apply");
        internal static Affine ReadAffine(Json m) => new Affine(m.F("a"), m.F("b"), m.F("c"), m.F("d"), m.F("tx"), m.F("ty"));
    }
}
