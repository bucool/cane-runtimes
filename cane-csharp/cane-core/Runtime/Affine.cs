using System;

namespace Cane
{
    public enum Inherit { Normal, Translation, NoRotationOrReflection, NoScale, NoScaleOrReflection }
    public enum BoneChannel { X, Y, Rotation, ScaleX, ScaleY, ShearX, ShearY, Inherit }

    /// <summary>Column-major affine matrices: [a, b, c, d, x, y]. Angles are degrees.</summary>
    public static class Affine
    {
        internal const double Degrees = Math.PI / 180, Radians = 180 / Math.PI;
        internal static double WrapAngle(double value) => value - 360 * Math.Floor((value + 180) / 360);
        internal static double Hypot(double x, double y)
        {
            x = Math.Abs(x); y = Math.Abs(y);
            if (double.IsInfinity(x) || double.IsInfinity(y)) return double.PositiveInfinity;
            if (x < y) { double swap = x; x = y; y = swap; }
            if (x == 0) return 0;
            double ratio = y / x;
            return x * Math.Sqrt(1 + ratio * ratio);
        }
        public static (double x, double y) LocalToWorld(ReadOnlySpan<double> world, int bone, double x, double y)
        {
            int i = bone * 6;
            return (world[i] * x + world[i + 2] * y + world[i + 4], world[i + 1] * x + world[i + 3] * y + world[i + 5]);
        }
        public static bool WorldToLocal(ReadOnlySpan<double> world, int bone, double x, double y, out double localX, out double localY)
        {
            int i = bone * 6;
            double a = world[i], b = world[i + 1], c = world[i + 2], d = world[i + 3], determinant = a * d - b * c;
            localX = localY = 0;
            if (determinant == 0) return false;
            x -= world[i + 4]; y -= world[i + 5];
            localX = (d * x - c * y) / determinant;
            localY = (a * y - b * x) / determinant;
            return true;
        }
        /// <summary>Recover local channels without changing evaluated state. An empty parent span denotes a root.</summary>
        public static bool BoneLocalForWorld(ReadOnlySpan<double> parent, ReadOnlySpan<double> world, ReadOnlySpan<double> reference, Span<double> local,
            double scaleX = 1, double scaleY = 1, double x = 0, double y = 0)
        {
            if (scaleX == 0 || scaleY == 0) return false;
            bool hasParent = !parent.IsEmpty;
            if (hasParent && parent[0] * parent[3] - parent[1] * parent[2] == 0) return false;
            Span<double> matrices = stackalloc double[12];
            world.Slice(0, 6).CopyTo(matrices);
            if (hasParent) parent.Slice(0, 6).CopyTo(matrices.Slice(6));
            reference.Slice(0, 8).CopyTo(local);
            SyncBone(0, hasParent ? 1 : -1, local, matrices, scaleX, scaleY, x, y);
            return true;
        }

        internal static void UpdateBone(int bone, int parent, ReadOnlySpan<double> pose, Span<double> world, Span<double> bases,
            double scaleX, double scaleY, double x, double y)
        {
            int p = bone * 8, w = bone * 6, local = bone * 9;
            double rotation = pose[p + 2] * Degrees, sx = pose[p + 3], sy = pose[p + 4];
            double shearX = pose[p + 5] * Degrees, shearY = pose[p + 6] * Degrees, inherit = pose[p + 7];
            double a = 0, b = 0, c = 0, d = 0;
            if (parent < 0 || inherit < 3)
            {
                if (bases[local] != rotation || bases[local + 1] != sx || bases[local + 2] != sy || bases[local + 3] != shearX || bases[local + 4] != shearY)
                {
                    double ax = rotation + shearX, ay = rotation + Math.PI / 2 + shearY;
                    bases[local] = rotation; bases[local + 1] = sx; bases[local + 2] = sy;
                    bases[local + 3] = shearX; bases[local + 4] = shearY;
                    double cosine = Math.Cos(ax), sine = Math.Sin(ax);
                    bases[local + 5] = cosine * sx; bases[local + 6] = sine * sx;
                    bases[local + 7] = (shearX == shearY ? -sine : Math.Cos(ay)) * sy;
                    bases[local + 8] = (shearX == shearY ? cosine : Math.Sin(ay)) * sy;
                }
                a = bases[local + 5]; b = bases[local + 6]; c = bases[local + 7]; d = bases[local + 8];
            }
            if (parent < 0)
            {
                world[w] = a * scaleX; world[w + 1] = b * scaleY; world[w + 2] = c * scaleX; world[w + 3] = d * scaleY;
                world[w + 4] = pose[p] * scaleX + x; world[w + 5] = pose[p + 1] * scaleY + y;
                return;
            }
            int q = parent * 6;
            double pa = world[q], pb = world[q + 1], pc = world[q + 2], pd = world[q + 3];
            world[w + 4] = pa * pose[p] + pc * pose[p + 1] + world[q + 4];
            world[w + 5] = pb * pose[p] + pd * pose[p + 1] + world[q + 5];
            if (inherit == 0)
            {
                world[w] = pa * a + pc * b; world[w + 1] = pb * a + pd * b;
                world[w + 2] = pa * c + pc * d; world[w + 3] = pb * c + pd * d;
                return;
            }
            if (inherit != 1)
            {
                pa = scaleX == 0 ? 0 : pa / scaleX; pc = scaleX == 0 ? 0 : pc / scaleX;
                pb = scaleY == 0 ? 0 : pb / scaleY; pd = scaleY == 0 ? 0 : pd / scaleY;
                if (inherit == 2)
                {
                    double length = Hypot(pa, pb), h00 = 0, h01 = 0, h11 = Hypot(pc, pd);
                    if (length != 0)
                    {
                        double ux = pa / length, uy = pb / length, height = Math.Abs(pa * pd - pb * pc) / length;
                        h00 = length * ux * ux + height * uy * uy;
                        h01 = (length - height) * ux * uy;
                        h11 = length * uy * uy + height * ux * ux;
                    }
                    double na = h00 * a + h01 * b, nc = h00 * c + h01 * d;
                    b = h01 * a + h11 * b; d = h01 * c + h11 * d; a = na; c = nc;
                }
                else
                {
                    double cr = Math.Cos(rotation), sr = Math.Sin(rotation), ux = pa * cr + pc * sr, uy = pb * cr + pd * sr;
                    double length = Hypot(ux, uy);
                    if (length != 0) { ux /= length; uy /= length; }
                    double sign = inherit == 3 && pa * pd - pb * pc < 0 ? -1 : 1, vx = -uy * sign, vy = ux * sign;
                    a = (ux * Math.Cos(shearX) + vx * Math.Sin(shearX)) * sx;
                    b = (uy * Math.Cos(shearX) + vy * Math.Sin(shearX)) * sx;
                    c = (-ux * Math.Sin(shearY) + vx * Math.Cos(shearY)) * sy;
                    d = (-uy * Math.Sin(shearY) + vy * Math.Cos(shearY)) * sy;
                }
            }
            world[w] = a * scaleX; world[w + 1] = b * scaleY; world[w + 2] = c * scaleX; world[w + 3] = d * scaleY;
        }

        internal static void SyncBone(int bone, int parent, Span<double> pose, ReadOnlySpan<double> world,
            double scaleX, double scaleY, double x, double y)
        {
            if (scaleX == 0 || scaleY == 0) return;
            int p = bone * 8, w = bone * 6, q = parent * 6;
            double mode = pose[p + 7], a = world[w], b = world[w + 1], c = world[w + 2], d = world[w + 3];
            double sx = pose[p + 3] < 0 ? -1 : 1, sy = pose[p + 4] < 0 ? -1 : 1;
            if (parent >= 0 && mode == 0)
            {
                double pa = world[q], pb = world[q + 1], pc = world[q + 2], pd = world[q + 3], det = pa * pd - pb * pc;
                if (det == 0) return;
                double dx = world[w + 4] - world[q + 4], dy = world[w + 5] - world[q + 5];
                pose[p] = (pd * dx - pc * dy) / det; pose[p + 1] = (pa * dy - pb * dx) / det;
                double na = (pd * a - pc * b) / det, nc = (pd * c - pc * d) / det;
                b = (pa * b - pb * a) / det; d = (pa * d - pb * c) / det; a = na; c = nc;
            }
            else
            {
                a /= scaleX; b /= scaleY; c /= scaleX; d /= scaleY;
                double pa = 1, pb = 0, pc = 0, pd = 1;
                if (parent < 0) { pose[p] = (world[w + 4] - x) / scaleX; pose[p + 1] = (world[w + 5] - y) / scaleY; }
                else
                {
                    pa = world[q] / scaleX; pb = world[q + 1] / scaleY; pc = world[q + 2] / scaleX; pd = world[q + 3] / scaleY;
                    double det = pa * pd - pb * pc;
                    if (det == 0) return;
                    double dx = (world[w + 4] - world[q + 4]) / scaleX, dy = (world[w + 5] - world[q + 5]) / scaleY;
                    pose[p] = (pd * dx - pc * dy) / det; pose[p + 1] = (pa * dy - pb * dx) / det;
                }
                if (parent >= 0 && mode >= 3)
                {
                    double det = pa * pd - pb * pc;
                    if (det == 0) return;
                    double handed = mode == 3 && det < 0 ? -1 : 1;
                    double direction = Math.Atan2(b * sx, a * sx) - handed * pose[p + 5] * Degrees;
                    double ux = Math.Cos(direction), uy = Math.Sin(direction);
                    pose[p + 2] = Math.Atan2((pa * uy - pb * ux) / det, (pd * ux - pc * uy) / det) * Radians;
                    pose[p + 3] = Hypot(a, b) * sx; pose[p + 4] = Hypot(c, d) * sy;
                    pose[p + 6] = WrapAngle(handed * (Math.Atan2(d * sy, c * sy) - direction) * Radians - 90);
                    return;
                }
                if (parent >= 0 && mode != 1)
                {
                    if (mode == 2)
                    {
                        double length = Hypot(pa, pb);
                        if (length == 0) return;
                        double ux = pa / length, uy = pb / length, height = Math.Abs(pa * pd - pb * pc) / length;
                        pa = length * ux * ux + height * uy * uy; pc = pb = (length - height) * ux * uy;
                        pd = length * uy * uy + height * ux * ux;
                    }
                    double det = pa * pd - pb * pc;
                    if (det == 0) return;
                    double na = (pd * a - pc * b) / det, nc = (pd * c - pc * d) / det;
                    b = (pa * b - pb * a) / det; d = (pa * d - pb * c) / det; a = na; c = nc;
                }
            }
            pose[p + 2] = Math.Atan2(b * sx, a * sx) * Radians - pose[p + 5];
            pose[p + 3] = Hypot(a, b) * sx; pose[p + 4] = Hypot(c, d) * sy;
            pose[p + 6] = WrapAngle(Math.Atan2(d * sy, c * sy) * Radians - pose[p + 2] - 90);
        }
    }
}
