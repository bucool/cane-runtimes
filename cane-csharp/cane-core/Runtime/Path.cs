using System;
using static Cane.Affine;

namespace Cane
{
    internal sealed class PathState
    {
        private const int Steps = 16;
        private double[] world = Array.Empty<double>(), coefficients = Array.Empty<double>(), arcs = Array.Empty<double>(), lengths = Array.Empty<double>();
        private readonly double[] targets, spaces;
        private double[]? queried;
        private bool constant = true;
        public int PointCount;
        public bool Closed;
        public double Total;
        public PathState(int bones) { targets = new double[(bones + 1) * 4]; spaces = new double[bones]; }
        public ReadOnlySpan<double> Points()
        {
            var output = queried ??= new double[targets.Length / 4 * 3];
            for (int i = 0; i < PointCount; i++)
            { int p = i * 4, q = i * 3; output[q] = targets[p]; output[q + 1] = targets[p + 1]; output[q + 2] = Math.Atan2(targets[p + 3], targets[p + 2]); }
            return output.AsSpan(0, PointCount * 3);
        }
        public void SamplePoint(double distance, Span<double> output)
        {
            Span<double> value = stackalloc double[4]; Sample(distance, value);
            output[0] = value[0]; output[1] = value[1]; output[2] = Math.Atan2(value[3], value[2]);
        }
        private void Compile(PathAttachment path)
        {
            var p = world; int count = (p.Length / 2 - (path.Closed ? 0 : 1)) / 3;
            if (coefficients.Length != count * 8)
            { coefficients = new double[count * 8]; arcs = new double[count * (Steps + 1)]; lengths = new double[count + 1]; }
            constant = path.ConstantSpeed || path.Lengths.Length == 0; Closed = path.Closed; Total = 0; lengths[0] = 0;
            for (int curve = 0; curve < count; curve++)
            {
                int first = curve * 6, end = (first + 6) % p.Length, c = curve * 8;
                for (int axis = 0; axis < 2; axis++)
                {
                    double a = p[first + axis], b = p[first + 2 + axis], d = p[first + 4 + axis], e = p[end + axis]; int o = c + axis * 4;
                    coefficients[o] = a; coefficients[o + 1] = 3 * (b - a); coefficients[o + 2] = 3 * (d - 2 * b + a); coefficients[o + 3] = e - 3 * d + 3 * b - a;
                }
                if (constant)
                {
                    var k = coefficients; double h = 1.0 / Steps;
                    double dx = k[c + 1] * h + k[c + 2] * h * h + k[c + 3] * h * h * h;
                    double dy = k[c + 5] * h + k[c + 6] * h * h + k[c + 7] * h * h * h;
                    double ddx = 2 * k[c + 2] * h * h + 6 * k[c + 3] * h * h * h, ddy = 2 * k[c + 6] * h * h + 6 * k[c + 7] * h * h * h;
                    double dddx = 6 * k[c + 3] * h * h * h, dddy = 6 * k[c + 7] * h * h * h; int a = curve * (Steps + 1); arcs[a] = 0;
                    for (int step = 1; step <= Steps; step++)
                    { arcs[a + step] = arcs[a + step - 1] + Hypot(dx, dy); dx += ddx; dy += ddy; ddx += dddx; ddy += dddy; }
                    Total += arcs[a + Steps];
                }
                else Total = path.Lengths[curve];
                lengths[curve + 1] = Total;
            }
        }
        private void Sample(double distance, Span<double> output)
        {
            int count = lengths.Length - 1;
            if (count <= 0) { output[0] = output[1] = output[3] = 0; output[2] = 1; return; }
            if (Closed && Total > 0) distance = Animation.Modulo(distance, Total);
            int curve = 0; double t = 0, extension = 0;
            if (distance < 0) extension = distance;
            else if (distance > Total) { curve = count - 1; t = 1; extension = distance - Total; }
            else
            {
                while (curve + 1 < count && distance > lengths[curve + 1]) curve++;
                double local = distance - lengths[curve], length = lengths[curve + 1] - lengths[curve];
                if (constant)
                {
                    int a = curve * (Steps + 1), step = 0;
                    while (step + 1 < Steps && local > arcs[a + step + 1]) step++;
                    double span = arcs[a + step + 1] - arcs[a + step]; t = (step + (span == 0 ? 0 : (local - arcs[a + step]) / span)) / Steps;
                }
                else t = length == 0 ? 0 : local / length;
            }
            var k = coefficients; int c = curve * 8;
            double x = ((k[c + 3] * t + k[c + 2]) * t + k[c + 1]) * t + k[c];
            double y = ((k[c + 7] * t + k[c + 6]) * t + k[c + 5]) * t + k[c + 4];
            double dx = (3 * k[c + 3] * t + 2 * k[c + 2]) * t + k[c + 1], dy = (3 * k[c + 7] * t + 2 * k[c + 6]) * t + k[c + 5];
            if (dx == 0 && dy == 0)
            {
                double direction = t == 1 ? -1 : 1; dx = (2 * k[c + 2] + 6 * k[c + 3] * t) * direction; dy = (2 * k[c + 6] + 6 * k[c + 7] * t) * direction;
                if (dx == 0 && dy == 0) { dx = k[c + 3]; dy = k[c + 7]; }
            }
            if (dx == 0 && dy == 0) dx = 1;
            output[0] = x; output[1] = y; output[2] = dx; output[3] = dy;
            if (extension != 0) { double scale = extension / Hypot(dx, dy); output[0] = x + dx * scale; output[1] = y + dy * scale; }
        }
        public void Apply(Skeleton s, PathConstraint c, int offset)
        {
            PointCount = 0; var pose = s.Working; var m = s.World; var frame = s.Frame;
            double mixRotate = pose[offset + 2], mixX = pose[offset + 3], mixY = pose[offset + 4];
            if (frame == null || mixRotate == 0 && mixX == 0 && mixY == 0) return;
            if (!(frame.Resolve(c.Slot, (int)pose[s.Data.SlotOffset + c.Slot * 8]) is PathAttachment path)) return;
            var slot = frame.Data.Slots[c.Slot]; var geometry = path.Geometry;
            if (geometry.VertexCount == 0) return;
            if (world.Length != geometry.VertexCount * 2) world = new double[geometry.VertexCount * 2];
            geometry.WriteWorld(m, slot.Bone, frame.Slots[c.Slot].Deform, world); Compile(path);
            double spacing = pose[offset + 1]; var spacingMode = c.SpacingMode; var rotateMode = c.RotateMode;
            int count = c.Bones.Length - (rotateMode == PathRotate.Tangent ? 1 : 0); double sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (spacingMode == PathSpacing.Percent) { spaces[i] = spacing * Total; continue; }
                int bone = c.Bones[i], w = bone * 6; double scale = Hypot(m[w], m[w + 1]), length = s.Data.Lengths[bone] * scale;
                double space = spacingMode == PathSpacing.Proportional ? length : s.Data.Lengths[bone] == 0 ? spacing : spacingMode == PathSpacing.Length ? Math.Max(0, s.Data.Lengths[bone] + spacing) * scale : spacing * scale;
                spaces[i] = space; sum += space;
            }
            if (spacingMode == PathSpacing.Proportional) for (int i = 0; i < count; i++) spaces[i] = spacing * Total * (sum == 0 ? 1.0 / count : spaces[i] / sum);
            double distance = pose[offset] * (c.PositionMode == PathPosition.Percent ? Total : 1);
            for (int i = 0; i <= count; i++) { Sample(distance, targets.AsSpan(i * 4, 4)); if (i < count) distance += spaces[i]; }
            PointCount = count + 1;
            int source = slot.Bone * 6; double rotation = c.OffsetRotation * Degrees * (m[source] * m[source + 3] - m[source + 1] * m[source + 2] < 0 ? -1 : 1);
            for (int i = 0; i < c.Bones.Length; i++)
            {
                int bone = c.Bones[i], w = bone * 6, p = i * 4; double x = targets[p], y = targets[p + 1];
                double dx = targets[p + 4] - x, dy = targets[p + 5] - y;
                m[w + 4] += (x - m[w + 4]) * mixX; m[w + 5] += (y - m[w + 5]) * mixY;
                if (rotateMode == PathRotate.ChainScale)
                {
                    double length = s.Data.Lengths[bone] * Hypot(m[w], m[w + 1]);
                    if (length != 0) { double scale = 1 + (Hypot(dx, dy) / length - 1) * mixRotate; m[w] *= scale; m[w + 1] *= scale; }
                }
                double cosine, sine, turn = 0; int tangent = rotateMode == PathRotate.Tangent ? p : spaces[i] == 0 ? p + 4 : -1;
                double tx = tangent < 0 ? dx : targets[tangent + 2], ty = tangent < 0 ? dy : targets[tangent + 3];
                double a = m[w], b = m[w + 1], norm = mixRotate == 1 && rotation == 0 ? Math.Sqrt((a * a + b * b) * (tx * tx + ty * ty)) : 0;
                if (norm > 0 && !double.IsInfinity(norm)) { cosine = (a * tx + b * ty) / norm; sine = (a * ty - b * tx) / norm; }
                else { turn = WrapAngle((Math.Atan2(ty, tx) + rotation - Math.Atan2(m[w + 1], m[w])) * Radians) * Degrees; cosine = Math.Cos(turn); sine = Math.Sin(turn); }
                if (rotateMode == PathRotate.Chain && rotation == 0)
                {
                    double length = s.Data.Lengths[bone];
                    targets[p + 4] += (length * (m[w] * cosine - m[w + 1] * sine) - dx) * mixRotate;
                    targets[p + 5] += (length * (m[w] * sine + m[w + 1] * cosine) - dy) * mixRotate;
                }
                if (mixRotate != 1) { cosine = Math.Cos(turn * mixRotate); sine = Math.Sin(turn * mixRotate); }
                for (int column = 0; column < 4; column += 2)
                { a = m[w + column]; b = m[w + column + 1]; m[w + column] = a * cosine - b * sine; m[w + column + 1] = a * sine + b * cosine; }
            }
            foreach (int bone in c.Bones) s.InvalidateLocal(bone);
        }
    }
}
