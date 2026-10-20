using System;
using static Cane.Affine;

namespace Cane
{
    internal static class TransformSolver
    {
        private static readonly int[] channels = { 2, 0, 1, 3, 4, 6 };
        private static readonly PropertyMapping[] identity = {
            new PropertyMapping(TransformProperty.Rotation, TransformProperty.Rotation), new PropertyMapping(TransformProperty.X, TransformProperty.X),
            new PropertyMapping(TransformProperty.Y, TransformProperty.Y), new PropertyMapping(TransformProperty.ScaleX, TransformProperty.ScaleX),
            new PropertyMapping(TransformProperty.ScaleY, TransformProperty.ScaleY), new PropertyMapping(TransformProperty.ShearY, TransformProperty.ShearY) };
        private static void Read(Skeleton s, int bone, bool local, Span<double> output)
        {
            if (local) { s.EnsureLocal(bone); for (int i = 0; i < 6; i++) output[i] = s.Working[bone * 8 + channels[i]]; return; }
            int w = bone * 6; var m = s.World;
            double a = m[w] / s.ScaleX, b = m[w + 1] / s.ScaleY, c = m[w + 2] / s.ScaleX, d = m[w + 3] / s.ScaleY, angle = Math.Atan2(b, a) * Radians;
            output[0] = angle; output[1] = m[w + 4] / s.ScaleX; output[2] = m[w + 5] / s.ScaleY;
            output[3] = Hypot(a, b); output[4] = Hypot(c, d); output[5] = Math.Atan2(d, c) * Radians - angle - 90;
        }
        public static double ReadProperty(Skeleton s, int bone, TransformProperty property, bool local)
        {
            if (local) { s.EnsureLocal(bone); return s.Working[bone * 8 + channels[(int)property]]; }
            int w = bone * 6; var m = s.World;
            if (property == TransformProperty.X) return m[w + 4] / s.ScaleX;
            if (property == TransformProperty.Y) return m[w + 5] / s.ScaleY;
            if (property == TransformProperty.ScaleX) return Hypot(m[w] / s.ScaleX, m[w + 1] / s.ScaleY);
            if (property == TransformProperty.ScaleY) return Hypot(m[w + 2] / s.ScaleX, m[w + 3] / s.ScaleY);
            double angle = Math.Atan2(m[w + 1] / s.ScaleY, m[w] / s.ScaleX) * Radians;
            return property == TransformProperty.Rotation ? angle : Math.Atan2(m[w + 3] / s.ScaleY, m[w + 2] / s.ScaleX) * Radians - angle - 90;
        }
        private static void ApplyProperty(Skeleton s, int bone, int property, double value, double mix, bool local, bool additive)
        {
            if (mix == 0) return;
            if (local)
            {
                int p = bone * 8 + channels[property]; double previous = s.Working[p];
                if (property == 3 || property == 4)
                {
                    if (additive) s.Working[p] = previous * (1 + (value - 1) * mix);
                    else if (previous != 0) s.Working[p] = previous + (value - previous) * mix;
                }
                else s.Working[p] = previous + (additive ? value : value - previous) * mix;
                return;
            }
            int w = bone * 6; var m = s.World;
            if (property == 1 || property == 2)
            { int p = w + property + 3; double scale = property == 1 ? s.ScaleX : s.ScaleY; m[p] += (value * scale - (additive ? 0 : m[p])) * mix; return; }
            if (property == 3 || property == 4)
            {
                int p = w + (property - 3) * 2; double length = Hypot(m[p] / s.ScaleX, m[p + 1] / s.ScaleY);
                double factor = additive ? 1 + (value - 1) * mix : length == 0 ? 1 : 1 + (value / length - 1) * mix;
                m[p] *= factor; m[p + 1] *= factor; return;
            }
            double angleX = Math.Atan2(m[w + 1] / s.ScaleY, m[w] / s.ScaleX) * Radians;
            int first = property == 0 ? 0 : 2;
            double current = property == 0 ? angleX : Math.Atan2(m[w + 3] / s.ScaleY, m[w + 2] / s.ScaleX) * Radians - angleX - 90;
            double delta = value - (additive ? 0 : current), angle = (property == 5 && additive ? delta : WrapAngle(delta)) * mix * Degrees, cosine = Math.Cos(angle), sine = Math.Sin(angle);
            for (int column = first; column <= 2; column += 2)
            {
                double x = m[w + column] / s.ScaleX, y = m[w + column + 1] / s.ScaleY;
                m[w + column] = (cosine * x - sine * y) * s.ScaleX; m[w + column + 1] = (sine * x + cosine * y) * s.ScaleY;
            }
        }
        private static double RotationSign(Skeleton s, TransformConstraint c)
        { int w = c.Source * 6; var m = s.World; return c.LocalSource || (m[w] * m[w + 3] - m[w + 1] * m[w + 2]) * s.ScaleX * s.ScaleY > 0 ? 1 : -1; }
        private static void Source(Skeleton s, TransformConstraint c, Span<double> source)
        {
            Read(s, c.Source, c.LocalSource, source); var o = c.Offsets; var m = s.World; int w = c.Source * 6;
            source[0] += o[0] * RotationSign(s, c);
            if (c.LocalSource) { source[1] += o[1]; source[2] += o[2]; }
            else { source[1] += (m[w] * o[1] + m[w + 2] * o[2]) / s.ScaleX; source[2] += (m[w + 1] * o[1] + m[w + 3] * o[2]) / s.ScaleY; }
            for (int i = 3; i < 6; i++) source[i] += o[i];
            if (!c.LocalSource && source[0] < 0) source[0] += 360;
        }
        private static bool Restrict(Span<double> range, double basis, double scale, double low, double high)
        {
            if (scale == 0) return basis >= low - 1e-9 && basis <= high + 1e-9;
            double a = (low - basis) / scale, b = (high - basis) / scale;
            range[0] = Math.Max(range[0], Math.Min(a, b)); range[1] = Math.Min(range[1], Math.Max(a, b));
            if (range[0] > range[1])
            { if (range[0] - range[1] > 1e-9) return false; range[0] = range[1] = (range[0] + range[1]) * .5; }
            return true;
        }
        public static double[]? MatchedOffsets(Skeleton s, TransformConstraint c)
        {
            if (c.Bones.Length == 0 || (!c.LocalSource || !c.LocalTarget) && (s.ScaleX == 0 || s.ScaleY == 0)) return null;
            Span<double> target = stackalloc double[6], current = stackalloc double[6], ranges = stackalloc double[12];
            Read(s, c.Bones[0], c.LocalTarget, target); Source(s, c, current);
            for (int i = 0; i < 6; i++) { ranges[i * 2] = double.NegativeInfinity; ranges[i * 2 + 1] = double.PositiveInfinity; }
            foreach (var map in c.Mappings ?? identity)
            {
                int from = (int)map.From, to = (int)map.To; bool scale = to == 3 || to == 4;
                if (scale && target[to] == 0) continue;
                double desired = c.Additive ? (scale ? 1 : 0) : target[to];
                if (!c.Additive && !c.LocalTarget && (to == 0 || to == 5))
                {
                    double value = (current[from] - map.FromOffset) * map.Scale + map.Offset, turns = Math.Floor((value - desired) / 360 + .5);
                    if (map.Limit.HasValue)
                    {
                        double low = Math.Ceiling((Math.Min(map.Offset, map.Limit.Value) - desired) / 360), high = Math.Floor((Math.Max(map.Offset, map.Limit.Value) - desired) / 360);
                        if (low > high) return null; turns = Math.Max(low, Math.Min(high, turns));
                    }
                    desired += turns * 360;
                }
                double lower = desired, upper = desired;
                if (map.Limit.HasValue)
                {
                    double min = Math.Min(map.Offset, map.Limit.Value), max = Math.Max(map.Offset, map.Limit.Value);
                    if (desired < min - 1e-9 || desired > max + 1e-9) return null;
                    if (desired <= min + 1e-9) lower = double.NegativeInfinity;
                    if (desired >= max - 1e-9) upper = double.PositiveInfinity;
                }
                if (!Restrict(ranges.Slice(from * 2, 2), map.Offset - map.FromOffset * map.Scale, map.Scale, lower, upper)) return null;
            }
            Span<double> desiredValues = stackalloc double[6], source = stackalloc double[6];
            for (int i = 0; i < 6; i++) desiredValues[i] = Math.Max(ranges[i * 2], Math.Min(ranges[i * 2 + 1], current[i]));
            var offsets = (double[])c.Offsets.Clone(); Read(s, c.Source, c.LocalSource, source);
            if (desiredValues[0] != current[0])
            {
                double angle = desiredValues[0];
                if (!c.LocalSource)
                {
                    double previous = source[0] + offsets[0] * RotationSign(s, c);
                    if (angle < 0 || angle < 360 && Math.Abs(angle - 360 - previous) < Math.Abs(angle - previous)) angle -= 360;
                }
                offsets[0] = (angle - source[0]) / RotationSign(s, c);
            }
            if (c.LocalSource) { offsets[1] = desiredValues[1] - source[1]; offsets[2] = desiredValues[2] - source[2]; }
            else
            {
                int w = c.Source * 6; var m = s.World;
                double a = m[w] / s.ScaleX, b = m[w + 1] / s.ScaleY, cc = m[w + 2] / s.ScaleX, d = m[w + 3] / s.ScaleY, det = a * d - b * cc;
                if (det != 0)
                { double x = desiredValues[1] - source[1], y = desiredValues[2] - source[2]; offsets[1] = (d * x - cc * y) / det; offsets[2] = (a * y - b * x) / det; }
                else
                {
                    bool first = a * a + b * b >= cc * cc + d * d; int axis = first ? 1 : 2, other = first ? 2 : 1;
                    double x = first ? a : cc, y = first ? b : d, ox = first ? cc : a, oy = first ? d : b;
                    Span<double> range = stackalloc double[2] { double.NegativeInfinity, double.PositiveInfinity };
                    for (int pass = 0; pass < 2; pass++)
                    {
                        int i = Math.Abs(x) >= Math.Abs(y) ? pass + 1 : 2 - pass;
                        double basis = source[i] + (i == 1 ? ox : oy) * offsets[other], scale = i == 1 ? x : y;
                        double value = basis + Math.Max(range[0], Math.Min(range[1], offsets[axis])) * scale;
                        if ((value < ranges[i * 2] - 1e-9 || value > ranges[i * 2 + 1] + 1e-9) && !Restrict(range, basis, scale, ranges[i * 2], ranges[i * 2 + 1])) return null;
                    }
                    offsets[axis] = Math.Max(range[0], Math.Min(range[1], offsets[axis]));
                }
            }
            for (int i = 3; i < 6; i++) offsets[i] = desiredValues[i] - source[i]; return offsets;
        }
        public static void Apply(Skeleton s, TransformConstraint c, int offset)
        {
            bool active = false; for (int i = 0; i < 6; i++) active |= s.Working[offset + i] != 0;
            if (!active || (!c.LocalSource || !c.LocalTarget) && (s.ScaleX == 0 || s.ScaleY == 0)) return;
            var source = s.SourceTransform; Source(s, c, source);
            foreach (int bone in c.Bones)
            {
                foreach (var map in c.Mappings ?? identity)
                {
                    double value = (source[(int)map.From] - map.FromOffset) * map.Scale + map.Offset;
                    if (map.Limit.HasValue) value = Math.Max(Math.Min(map.Offset, map.Limit.Value), Math.Min(Math.Max(map.Offset, map.Limit.Value), value));
                    ApplyProperty(s, bone, (int)map.To, value, s.Working[offset + (int)map.To], c.LocalTarget, c.Additive);
                }
                if (c.LocalTarget) s.UpdateBone(bone); else s.InvalidateLocal(bone);
            }
        }
    }
}
