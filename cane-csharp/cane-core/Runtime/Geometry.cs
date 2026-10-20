using System;

namespace Cane
{
    /// <summary>Unweighted points are attachment-local; weighted points are bone-local,
    /// one XY pair per influence. Deformation uses those same binding spaces.</summary>
    public sealed class Geometry
    {
        internal readonly double[] Positions, Weights;
        internal readonly uint[] Bones, Starts;
        private readonly int rigidBone = -1;
        public int VertexCount { get; }
        public ReadOnlySpan<double> BindPositions => Positions;
        public ReadOnlySpan<uint> InfluenceBones => Bones;
        public ReadOnlySpan<double> InfluenceWeights => Weights;
        public ReadOnlySpan<uint> InfluenceStarts => Starts;

        public Geometry(double[] positions, uint[]? bones = null, double[]? weights = null, uint[]? starts = null)
        {
            Positions = positions; Bones = bones ?? Array.Empty<uint>();
            Weights = weights ?? Array.Empty<double>(); Starts = starts ?? Array.Empty<uint>();
            VertexCount = Starts.Length == 0 ? positions.Length / 2 : Starts.Length - 1;
            if (Bones.Length == 0 || Bones.Length != VertexCount) return;
            for (int i = 0; i < Starts.Length; i++) if (Starts[i] != i) return;
            for (int i = 0; i < Bones.Length; i++) if (Weights[i] != 1 || Bones[i] != Bones[0]) return;
            rigidBone = (int)Bones[0];
        }

        public void WriteWorld(ReadOnlySpan<double> world, int slotBone, ReadOnlySpan<double> deform, Span<float> output)
        {
            if (Starts.Length == 0 || rigidBone >= 0)
            {
                int m = (rigidBone < 0 ? slotBone : rigidBone) * 6;
                double a = world[m], b = world[m + 1], c = world[m + 2], d = world[m + 3], tx = world[m + 4], ty = world[m + 5];
                for (int i = 0; i < Positions.Length; i += 2)
                {
                    double x = Positions[i] + (deform.IsEmpty ? 0 : deform[i]), y = Positions[i + 1] + (deform.IsEmpty ? 0 : deform[i + 1]);
                    output[i] = (float)(a * x + c * y + tx); output[i + 1] = (float)(b * x + d * y + ty);
                }
                return;
            }
            for (int i = 0; i < VertexCount; i++)
            {
                WeightedVertex(world, deform, i, out double x, out double y);
                output[i * 2] = (float)x; output[i * 2 + 1] = (float)y;
            }
        }

        public void WriteWorld(ReadOnlySpan<double> world, int slotBone, ReadOnlySpan<double> deform, Span<double> output)
        {
            if (Starts.Length == 0 || rigidBone >= 0)
            {
                int m = (rigidBone < 0 ? slotBone : rigidBone) * 6;
                double a = world[m], b = world[m + 1], c = world[m + 2], d = world[m + 3], tx = world[m + 4], ty = world[m + 5];
                for (int i = 0; i < Positions.Length; i += 2)
                {
                    double x = Positions[i] + (deform.IsEmpty ? 0 : deform[i]), y = Positions[i + 1] + (deform.IsEmpty ? 0 : deform[i + 1]);
                    output[i] = a * x + c * y + tx; output[i + 1] = b * x + d * y + ty;
                }
                return;
            }
            for (int i = 0; i < VertexCount; i++) WeightedVertex(world, deform, i, out output[i * 2], out output[i * 2 + 1]);
        }

        private void WeightedVertex(ReadOnlySpan<double> world, ReadOnlySpan<double> deform, int vertex, out double x, out double y)
        {
            x = y = 0;
            for (int i = (int)Starts[vertex], end = (int)Starts[vertex + 1]; i < end; i++)
            {
                int m = (int)Bones[i] * 6, p = i * 2;
                double px = Positions[p] + (deform.IsEmpty ? 0 : deform[p]), py = Positions[p + 1] + (deform.IsEmpty ? 0 : deform[p + 1]);
                x += (world[m] * px + world[m + 2] * py + world[m + 4]) * Weights[i];
                y += (world[m + 1] * px + world[m + 3] * py + world[m + 5]) * Weights[i];
            }
        }
    }

    public readonly struct Bounds
    {
        public readonly double MinX, MinY, MaxX, MaxY;
        public Bounds(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public static Bounds? FromVertices(ReadOnlySpan<float> vertices)
        {
            if (vertices.IsEmpty) return null;
            double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
            for (int i = 0; i < vertices.Length; i += 2)
            {
                minX = Math.Min(minX, vertices[i]); maxX = Math.Max(maxX, vertices[i]);
                minY = Math.Min(minY, vertices[i + 1]); maxY = Math.Max(maxY, vertices[i + 1]);
            }
            return new Bounds(minX, minY, maxX, maxY);
        }
    }

    public static class GeometryQueries
    {
        internal static double Cross(double ax, double ay, double bx, double by, double cx, double cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        public static bool ContainsPoint(ReadOnlySpan<float> vertices, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = vertices.Length - 2; i < vertices.Length; j = i, i += 2)
            {
                double ax = vertices[j], ay = vertices[j + 1], bx = vertices[i], by = vertices[i + 1];
                if ((x - ax) * (by - ay) - (y - ay) * (bx - ax) == 0 && x >= Math.Min(ax, bx) && x <= Math.Max(ax, bx) && y >= Math.Min(ay, by) && y <= Math.Max(ay, by)) return true;
                if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
            }
            return inside;
        }

        /// <summary>Earliest fraction along AB, including endpoints and collinear overlap.</summary>
        public static double? SegmentIntersection(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double rx = bx - ax, ry = by - ay, sx = dx - cx, sy = dy - cy, qx = cx - ax, qy = cy - ay, det = rx * sy - ry * sx;
            if (det != 0)
            {
                double t = (qx * sy - qy * sx) / det, u = (qx * ry - qy * rx) / det;
                return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? t : (double?)null;
            }
            if (qx * ry - qy * rx != 0) return null;
            double length = rx * rx + ry * ry;
            if (length == 0) return (ax - cx) * sy - (ay - cy) * sx == 0 && ax >= Math.Min(cx, dx) && ax <= Math.Max(cx, dx) && ay >= Math.Min(cy, dy) && ay <= Math.Max(cy, dy) ? 0 : (double?)null;
            double first = (qx * rx + qy * ry) / length, last = ((dx - ax) * rx + (dy - ay) * ry) / length;
            return Math.Max(first, last) < 0 || Math.Min(first, last) > 1 ? null : (double?)Math.Max(0, Math.Min(first, last));
        }
    }

    public readonly struct SegmentHit
    {
        public readonly double Time, X, Y, NormalX, NormalY;
        public SegmentHit(double time, double x, double y, double normalX, double normalY)
        { Time = time; X = x; Y = y; NormalX = normalX; NormalY = normalY; }
    }
}
