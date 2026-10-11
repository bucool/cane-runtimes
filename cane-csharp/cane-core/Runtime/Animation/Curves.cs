using System;
using System.Collections.Generic;
using Cane.Format;

namespace Cane.Animation
{
    internal readonly struct Curve
    {
        private readonly int kind;
        private readonly float x1, y1, x2, y2;
        internal Curve(Json source, string property, bool bone = false)
        {
            if (source.S("type") == "properties") { string axis = property.EndsWith("_x", StringComparison.Ordinal) ? "x" : property.EndsWith("_y", StringComparison.Ordinal) ? "y" : property; if(bone && !source["properties"].Has(property) && source["properties"].Has(axis)) property=axis; source = source["properties"].Has(property) ? source["properties"][property] : source["default"]; }
            kind = source.IsNull || source.Value is string s && s == "linear" ? 0 : source.Value is string ? 1 : source.S("type") == "bezier" ? 2 : 3;
            x1 = source.F("cx1"); x2 = source.F("cx2"); y1 = source.F(kind == 3 ? "dy1" : "cy1"); y2 = source.F(kind == 3 ? "dy2" : "cy2");
        }
        internal float Sample(float from, float to, float progress, bool angular, bool stepped)
        {
            if (angular) to = from + Numeric.Wrap(to - from);
            if (!Numeric.Finite(from) || !Numeric.Finite(to) || !Numeric.Finite(progress))
                throw new RuntimeException(RuntimeErrorCode.NonFinite, "sampleCurve", "Animation input must be finite.");
            float p = Numeric.Clamp(progress, 0, 1);
            if (p <= 0) return from; if (p >= 1) return to; if (stepped || kind == 1) return from;
            float result;
            if (kind == 0) result = from + (to - from) * p;
            else
            {
                double start = from, end = to;
                double c1 = kind == 2 ? start + (end - start) * y1 : start + y1;
                double c2 = kind == 2 ? start + (end - start) * y2 : end + y2;
                result = (float)CubicRoots.Sample(new[] { 0.0, (double)x1, x2, 1.0 }, new[] { start, c1, c2, end }, p);
            }
            if (!Numeric.Finite(result)) throw new RuntimeException(RuntimeErrorCode.NonFinite, "sampleCurve", "Animation result cannot be represented as binary32.");
            return result;
        }
    }

    internal sealed class Channel
    {
        internal readonly struct Key
        {
            internal readonly float Time, Value;
            internal readonly Curve Curve;
            internal Key(float time, float value, Curve curve) { Time = time; Value = value; Curve = curve; }
        }
        internal readonly Key[] Keys;
        internal bool HasKeys => Keys.Length != 0;
        internal Channel(SortedDictionary<float, Key> groups)
        { Keys = new Key[groups.Count]; int i = 0; foreach (var pair in groups) Keys[i++] = pair.Value; }
        internal static Channel From(Json keys, string field, string property, Func<Json, float>? getter = null, bool bone = false)
        {
            var groups = new SortedDictionary<float, Key>();
            foreach (var key in keys.ArrayOrEmpty)
            {
                if (getter == null && key[field].IsNull) continue;
                float time = key.F("time"); groups[time] = new Key(time, getter == null ? key.F(field) : getter(key), new Curve(key["curve"], property, bone));
            }
            return new Channel(groups);
        }
        internal static Channel Merged(Json pair, Json scalar, string axis, string? property = null)
        {
            var paired = From(pair, axis, axis).Keys;
            var single = From(scalar, "value", property ?? axis, bone: true).Keys;
            var keys = new Key[paired.Length + single.Length]; int p = 0, q = 0, i = 0;
            while (p < paired.Length || q < single.Length)
                keys[i++] = q == single.Length || p < paired.Length && paired[p].Time <= single[q].Time ? paired[p++] : single[q++];
            return new Channel(keys);
        }
        private Channel(Key[] keys) { Keys = keys; }
        internal float? Sample(float time, bool angular = false, bool stepped = false)
        {
            if (Keys.Length == 0 || time < Keys[0].Time) return null;
            int low = 0, high = Keys.Length;
            while (low < high) { int mid = low + (high - low) / 2; if (Keys[mid].Time <= time) low = mid + 1; else high = mid; }
            Key current = Keys[low - 1]; if (low == Keys.Length || time == current.Time) return current.Value;
            Key next = Keys[low]; float progress = (time - current.Time) / (next.Time - current.Time);
            return current.Curve.Sample(current.Value, next.Value, progress, angular, stepped);
        }
    }
    internal static class KeySampling
    {
        internal static Json Discrete(Json keys, float time, string? requiredField = null)
        {
            Json value = Json.Null;
            foreach (Json k in keys.ArrayOrEmpty) { if (k.F("time") > time) break; if (requiredField == null || !k[requiredField].IsNull) value = k; }
            return value;
        }
        internal static float Blend(float current, float setup, float sample, float alpha, bool additive, bool angular = false)
        { if (alpha == 0) return current; float delta = sample - (additive ? setup : current); return current + (angular ? Numeric.Wrap(delta) : delta) * alpha; }
        internal static int BackAndForthLoop(int index, int count)
        { if (count <= 1) return 0; int cycle = count * 2 - 2, phase = index % cycle; return phase < count ? phase : cycle - phase; }
    }
}
