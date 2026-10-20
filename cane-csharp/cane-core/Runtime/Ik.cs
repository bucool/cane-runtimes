using System;
using static Cane.Affine;

namespace Cane
{
    internal static class IkSolver
    {
        private static void ScaleBone(double[] pose, int p, double factor, double mode)
        {
            pose[p + 3] *= factor;
            if (mode == 1) pose[p + 4] *= factor;
            else if (mode == 2 && factor != 0) pose[p + 4] /= factor;
        }
        private static bool TargetInParent(Skeleton s, int bone, double tx, double ty, out double x, out double y)
        {
            int p = bone * 8, w = bone * 6; var pose = s.Working; var world = s.World;
            double a = world[w], b = world[w + 1], c = world[w + 2], d = world[w + 3], det = a * d - b * c;
            x = y = 0; if (det == 0) return false;
            double dx = tx - world[w + 4], dy = ty - world[w + 5]; int parent = s.Data.Parents[bone]; double inherit = pose[p + 7];
            if (parent < 0 || inherit == 1) { x = dx / s.ScaleX; y = dy / s.ScaleY; return true; }
            if (inherit == 0)
            {
                int q = parent * 6; double pa = world[q], pb = world[q + 1], pc = world[q + 2], pd = world[q + 3], parentDet = pa * pd - pb * pc;
                if (parentDet == 0) return false;
                x = (pd * dx - pc * dy) / parentDet; y = (pa * dy - pb * dx) / parentDet; return true;
            }
            double lx = (d * dx - c * dy) / det, ly = (a * dy - b * dx) / det;
            double ax = (pose[p + 2] + pose[p + 5]) * Degrees, ay = (pose[p + 2] + pose[p + 6] + 90) * Degrees;
            x = Math.Cos(ax) * pose[p + 3] * lx + Math.Cos(ay) * pose[p + 4] * ly;
            y = Math.Sin(ax) * pose[p + 3] * lx + Math.Sin(ay) * pose[p + 4] * ly; return true;
        }
        private static void One(Skeleton s, int bone, double tx, double ty, double mix, bool compress, bool stretch, double scaleY)
        {
            int p = bone * 8; var pose = s.Working;
            if (!TargetInParent(s, bone, tx, ty, out double x, out double y)) return;
            double angle = Math.Atan2(y, x) * Radians - pose[p + 5] - (pose[p + 3] < 0 ? 180 : 0);
            if (pose[p + 7] >= 3)
            {
                int parent = s.Data.Parents[bone], w = bone * 6, q = parent * 6;
                if (parent >= 0 && s.ScaleX != 0 && s.ScaleY != 0)
                {
                    double a = s.World[q] / s.ScaleX, b = s.World[q + 1] / s.ScaleY, c = s.World[q + 2] / s.ScaleX, d = s.World[q + 3] / s.ScaleY, det = a * d - b * c;
                    if (det == 0) return;
                    x = (tx - s.World[w + 4]) / s.ScaleX; y = (ty - s.World[w + 5]) / s.ScaleY;
                    double sign = pose[p + 7] == 3 && det < 0 ? -1 : 1;
                    double direction = Math.Atan2(y, x) - sign * pose[p + 5] * Degrees - (pose[p + 3] < 0 ? Math.PI : 0), ux = Math.Cos(direction), uy = Math.Sin(direction);
                    angle = Math.Atan2((a * uy - b * ux) / det, (d * ux - c * uy) / det) * Radians;
                }
            }
            pose[p + 2] += WrapAngle(angle - pose[p + 2]) * mix;
            if (compress || stretch)
            {
                double length = s.Data.Lengths[bone] * Math.Abs(pose[p + 3]), distance = Hypot(x, y);
                if (length > 0 && (compress && distance < length || stretch && distance > length)) ScaleBone(pose, p, 1 + (distance / length - 1) * mix, scaleY);
            }
            s.UpdateBone(bone);
        }
        private static void Two(Skeleton s, int bone, int child, double tx, double ty, double mix, double bend, double softness, bool stretch, double scaleY)
        {
            var pose = s.Working; int p = bone * 8, c = child * 8;
            if (!TargetInParent(s, bone, tx, ty, out double x, out double y)) return;
            double sx = pose[p + 3], sy = pose[p + 4], childLength = s.Data.Lengths[child] * pose[c + 3];
            bool uniform = Math.Abs(Math.Abs(sx) - Math.Abs(sy)) < 1e-8;
            if (!uniform || stretch) pose[c + 1] = 0;
            double ox = sx * pose[c], oy = sy * pose[c + 1], length1 = oy == 0 ? Math.Abs(ox) : Hypot(ox, oy), length2 = Math.Abs(sx * childLength);
            if (length1 == 0) { s.UpdateBone(child); One(s, child, tx, ty, mix, false, stretch, scaleY); return; }
            double distance = Hypot(x, y);
            if (childLength == 0)
            {
                double angle = (Math.Atan2(y, x) - Math.Atan2(oy, ox)) * Radians;
                pose[p + 2] += WrapAngle(angle - pose[p + 2]) * mix; pose[p + 5] = pose[p + 6] = 0;
                if (stretch && uniform && distance > length1) ScaleBone(pose, p, 1 + (distance / length1 - 1) * mix, scaleY);
                s.UpdateBone(bone); s.UpdateBone(child); return;
            }
            softness *= Math.Abs(sx) * (1 + Math.Abs(pose[c + 3])) / 2;
            if (softness > 0 && distance > length1 + length2 - softness)
            {
                double u = (distance - length1 - length2 + softness) / (2 * softness), softened = u >= 1 ? length1 + length2 : distance - softness * u * u;
                x *= softened / distance; y *= softened / distance; distance = softened;
            }
            double vx, vy, childAngle;
            if (uniform)
            {
                double cosine = Math.Max(-1, Math.Min(1, (distance * distance - length1 * length1 - length2 * length2) / (2 * length1 * length2)));
                if (Math.Abs(bend) == 1 && cosine > -1 && cosine < 1)
                {
                    double sine = Math.Sqrt(1 - cosine * cosine) * bend;
                    vx = (ox * cosine - oy * sine) * length2 / length1; vy = (oy * cosine + ox * sine) * length2 / length1;
                }
                else { double direction = Math.Atan2(oy, ox) + Math.Acos(cosine) * bend; vx = Math.Cos(direction) * length2; vy = Math.Sin(direction) * length2; }
                childAngle = Math.Atan2(vy / (sy * childLength), vx / (sx * childLength));
                if (stretch && softness == 0 && distance > length1 + length2) ScaleBone(pose, p, 1 + (distance / (length1 + length2) - 1) * mix, scaleY);
            }
            else
            {
                double a = sx * childLength, b = sy * childLength, qa = a * a - b * b, qb = 2 * ox * a, qc = ox * ox + b * b - distance * distance;
                double discriminant = qb * qb - 4 * qa * qc, cosine = double.NaN;
                if (discriminant >= 0)
                {
                    double root = Math.Sqrt(discriminant), q = -.5 * (qb + (qb < 0 ? -root : root)), c1 = q / qa, c2 = q == 0 ? -qb / (2 * qa) : qc / q;
                    if (Math.Abs(c1) <= 1) cosine = c1;
                    if (Math.Abs(c2) <= 1 && (double.IsNaN(cosine) || double.IsInfinity(cosine) || Math.Abs(ox + a * c2) < Math.Abs(ox + a * cosine))) cosine = c2;
                }
                if (double.IsNaN(cosine) || double.IsInfinity(cosine))
                {
                    Span<double> candidates = stackalloc double[3] { -1, 1, Math.Max(-1, Math.Min(1, -qb / (2 * qa))) }; double error = double.PositiveInfinity;
                    foreach (double candidate in candidates)
                    {
                        double radius = Hypot(ox + a * candidate, b * Math.Sqrt(1 - candidate * candidate)), e = Math.Abs(radius - distance);
                        if (e < error) { error = e; cosine = candidate; }
                    }
                }
                double sine = Math.Sqrt(Math.Max(0, 1 - cosine * cosine)) * bend * Math.Sign(ox * b);
                vx = a * cosine; vy = b * sine; childAngle = Math.Atan2(sine, cosine);
            }
            double parentAngle = (Math.Atan2(y, x) - Math.Atan2(oy + vy, ox + vx)) * Radians;
            pose[p + 2] += WrapAngle(parentAngle - pose[p + 2]) * mix; pose[p + 5] = pose[p + 6] = 0;
            pose[c + 2] += WrapAngle(childAngle * Radians - pose[c + 5] - pose[c + 2]) * mix;
            s.UpdateBone(bone); s.UpdateBone(child);
        }
        private static (double X, double Y) Tip(Skeleton s, int bone)
        { int w = bone * 6; double length = s.Data.Lengths[bone]; return (s.World[w + 4] + s.World[w] * length, s.World[w + 5] + s.World[w + 1] * length); }
        private static double Error(Skeleton s, int bone, double tx, double ty)
        { var end = Tip(s, bone); double x = end.X - tx, y = end.Y - ty; return x * x + y * y; }
        private static void Chain(Skeleton s, IkConstraint ik, int offset, double tx, double ty)
        {
            var bones = ik.Bones; int last = bones[bones.Length - 1], root = bones[0]; double rx = s.World[root * 6 + 4], ry = s.World[root * 6 + 5], reach = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                int b = bones[i], p = b * 8; s.IkPose[i * 3] = s.Working[p + 2]; s.IkPose[i * 3 + 1] = s.Working[p + 3]; s.IkPose[i * 3 + 2] = s.Working[p + 4];
                var point = i + 1 == bones.Length ? Tip(s, last) : (s.World[bones[i + 1] * 6 + 4], s.World[bones[i + 1] * 6 + 5]);
                reach += Hypot(point.Item1 - s.World[b * 6 + 4], point.Item2 - s.World[b * 6 + 5]);
            }
            double distance = Hypot(tx - rx, ty - ry), softness = s.Working[offset + 2];
            if (softness > 0 && distance > reach - softness && distance > 0)
            {
                double u = (distance - reach + softness) / (2 * softness), falloff = Math.Max(0, 1 - u), softDistance = distance - softness * (u - (1 - falloff * falloff) * .5);
                tx = rx + (tx - rx) * softDistance / distance; ty = ry + (ty - ry) * softDistance / distance;
            }
            if (reach > 0 && (s.Working[offset + 3] != 0 && distance < reach || s.Working[offset + 4] != 0 && distance > reach))
            { ScaleBone(s.Working, root * 8, distance / reach, s.Working[offset + 5]); foreach (int b in bones) s.UpdateBone(b); }
            double epsilon = Math.Max(reach * 1e-5, 1e-6), tolerance = epsilon * epsilon; var end = Tip(s, last); bool collinear = true;
            for (int i = 1; i < bones.Length; i++)
                if (Math.Abs((s.World[bones[i] * 6 + 4] - rx) * (end.Y - ry) - (s.World[bones[i] * 6 + 5] - ry) * (end.X - rx)) > reach * reach * 1e-10) { collinear = false; break; }
            if (collinear && distance < reach && Error(s, last, tx, ty) > tolerance)
            { s.Working[bones[1] * 8 + 2] += 5 * Math.Sign(s.Working[offset + 1]); for (int i = 1; i < bones.Length; i++) s.UpdateBone(bones[i]); }
            for (int iteration = 0; iteration < 128; iteration++)
            {
                double before = Error(s, last, tx, ty); if (before <= tolerance) break;
                for (int i = bones.Length - 1; i >= 0; i--)
                {
                    int b = bones[i], p = b * 8 + 2; end = Tip(s, last); double original = s.Working[p], oldError = Error(s, last, tx, ty);
                    s.Working[p] = original + .001; for (int j = i; j < bones.Length; j++) s.UpdateBone(bones[j]);
                    var moved = Tip(s, last); double dx = (moved.X - end.X) / .001, dy = (moved.Y - end.Y) / .001, length = dx * dx + dy * dy;
                    double delta = length == 0 ? 0 : Math.Max(-45, Math.Min(45, ((tx - end.X) * dx + (ty - end.Y) * dy) / length)); bool accepted = false;
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        s.Working[p] = original + delta; for (int j = i; j < bones.Length; j++) s.UpdateBone(bones[j]);
                        if (Error(s, last, tx, ty) < oldError) { accepted = true; break; } delta *= .5;
                    }
                    if (!accepted) { s.Working[p] = original; for (int j = i; j < bones.Length; j++) s.UpdateBone(bones[j]); }
                }
                if (before - Error(s, last, tx, ty) <= tolerance * 1e-4) break;
            }
            double mix = s.Working[offset];
            if (mix != 1) for (int i = 0; i < bones.Length; i++)
            {
                int b = bones[i], p = b * 8; double original = s.IkPose[i * 3]; s.Working[p + 2] = original + WrapAngle(s.Working[p + 2] - original) * mix;
                for (int axis = 0; axis < 2; axis++) { double initial = s.IkPose[i * 3 + 1 + axis]; s.Working[p + 3 + axis] = initial + (s.Working[p + 3 + axis] - initial) * mix; }
                s.UpdateBone(b);
            }
        }
        public static void Apply(Skeleton s, IkConstraint ik, int offset)
        {
            double mix = s.Working[offset]; if (mix == 0) return;
            int target = ik.Target * 6; double tx = s.World[target + 4], ty = s.World[target + 5], scaleY = s.Working[offset + 5]; bool stretch = s.Working[offset + 4] != 0;
            if (ik.Bones.Length > 2) Chain(s, ik, offset, tx, ty);
            else if (ik.Bones.Length == 1) One(s, ik.Bones[0], tx, ty, mix, s.Working[offset + 3] != 0, stretch, scaleY);
            else Two(s, ik.Bones[0], ik.Bones[1], tx, ty, mix, s.Working[offset + 1], s.Working[offset + 2], stretch, scaleY);
        }
    }
}
