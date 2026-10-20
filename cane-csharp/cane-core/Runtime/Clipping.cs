using System;
using System.Collections.Generic;
using static Cane.GeometryQueries;

namespace Cane
{
    public sealed class MeshBuffer
    {
        public float[] Vertices = Array.Empty<float>(), Uvs = Array.Empty<float>();
        public uint[] Indices = Array.Empty<uint>();
        public int VertexCount, IndexCount;
        public void Reserve(int vertices, int indices)
        {
            if (vertices * 2 > Vertices.Length)
            {
                int size = Math.Max(vertices * 2, Math.Max(Vertices.Length * 2, 32));
                Array.Resize(ref Vertices, size); Array.Resize(ref Uvs, size);
            }
            if (indices > Indices.Length) Array.Resize(ref Indices, Math.Max(indices, Math.Max(Indices.Length * 2, 24)));
        }
    }

    // Partitions a concave polygon into disjoint triangles. The per-instance workspace
    // is also used for convex/inverse clips; capacity is retained across frames.
    internal sealed class Clipper
    {
        public bool Active;
        public int EndSlot = -1;
        private ReadOnlyMemory<float> polygon;
        private double orientation = 1;
        private bool convex, inverse;
        private readonly List<int> remaining = new List<int>(), triangles = new List<int>(), hullOrder = new List<int>();
        private List<double> a = new List<double>(), b = new List<double>();
        private readonly List<double> outside = new List<double>();
        private float[] hullPoints = Array.Empty<float>();
        private int[] boundaryIndices = Array.Empty<int>();
        public ReadOnlySpan<float> Boundary => polygon.Span;
        public int PolygonCount => convex ? 1 : triangles.Count / 3;
        public ReadOnlySpan<int> BoundaryTriangles()
        {
            int count = convex ? Math.Max(0, polygon.Length / 2 - 2) * 3 : triangles.Count;
            if (boundaryIndices.Length < count) boundaryIndices = new int[Math.Max(count, boundaryIndices.Length * 2)];
            if (convex) for (int i = 0; i < count; i += 3)
            { boundaryIndices[i] = 0; boundaryIndices[i + 1] = i / 3 + 1; boundaryIndices[i + 2] = i / 3 + 2; }
            else triangles.CopyTo(boundaryIndices);
            return boundaryIndices.AsSpan(0, count);
        }

        public void Start(ReadOnlyMemory<float> points, int endSlot, bool forceConvex = false, bool inverse = false)
        {
            if (Active) return;
            Active = true; EndSlot = endSlot; this.inverse = inverse;
            polygon = forceConvex || inverse ? Hull(points) : points;
            triangles.Clear(); remaining.Clear(); convex = false;
            ReadOnlySpan<float> p = polygon.Span;
            int count = p.Length / 2;
            if (count < 3) return;
            double area = 0;
            for (int i = 1; i + 1 < count; i++) area += Cross(p[0], p[1], p[i * 2], p[i * 2 + 1], p[i * 2 + 2], p[i * 2 + 3]);
            if (area == 0) return;
            orientation = area > 0 ? 1 : -1; convex = true;
            for (int i = 0; i < count; i++)
            {
                if (Turn((i + count - 1) % count, i, (i + 1) % count) < 0) convex = false;
                remaining.Add(i);
            }
            if (convex) return;
            while (remaining.Count > 2)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count], current = remaining[i], next = remaining[(i + 1) % remaining.Count];
                    double turn = Turn(previous, current, next);
                    if (turn < 0) continue;
                    bool occupied = false;
                    if (turn > 0) foreach (int point in remaining)
                    {
                        if (point != previous && point != current && point != next && Turn(previous, current, point) >= 0 && Turn(current, next, point) >= 0 && Turn(next, previous, point) >= 0)
                        { occupied = true; break; }
                    }
                    if (occupied) continue;
                    if (turn > 0) { triangles.Add(previous); triangles.Add(current); triangles.Add(next); }
                    remaining.RemoveAt(i); found = true; break;
                }
                if (!found) break; // No ear remains after collapse or self-intersection.
            }
        }

        private static int Compare(ReadOnlySpan<float> p, int a, int b)
        {
            int x = p[a * 2].CompareTo(p[b * 2]);
            return x != 0 ? x : p[a * 2 + 1].CompareTo(p[b * 2 + 1]);
        }

        // Heap-sort the retained index buffer. List.Sort's comparer wrapper allocates
        // on supported runtimes, including when the caller reuses its comparer.
        private void Sift(ReadOnlySpan<float> points, int root, int count)
        {
            int value = remaining[root];
            for (int child = root * 2 + 1; child < count; child = root * 2 + 1)
            {
                if (child + 1 < count && Compare(points, remaining[child], remaining[child + 1]) < 0) child++;
                if (Compare(points, value, remaining[child]) >= 0) break;
                remaining[root] = remaining[child]; root = child;
            }
            remaining[root] = value;
        }

        private ReadOnlyMemory<float> Hull(ReadOnlyMemory<float> points)
        {
            remaining.Clear(); hullOrder.Clear();
            ReadOnlySpan<float> p = points.Span;
            for (int i = 0; i < p.Length / 2; i++) remaining.Add(i);
            for (int i = remaining.Count / 2 - 1; i >= 0; i--) Sift(p, i, remaining.Count);
            for (int end = remaining.Count - 1; end > 0; end--)
            {
                int last = remaining[end]; remaining[end] = remaining[0]; remaining[0] = last;
                Sift(p, 0, end);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hullOrder.Count;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int c = remaining[pass == 0 ? i : remaining.Count - i - 1];
                    while (hullOrder.Count >= start + 2)
                    {
                        int a = hullOrder[hullOrder.Count - 2], b = hullOrder[hullOrder.Count - 1];
                        if (Cross(p[a * 2], p[a * 2 + 1], p[b * 2], p[b * 2 + 1], p[c * 2], p[c * 2 + 1]) > 0) break;
                        hullOrder.RemoveAt(hullOrder.Count - 1);
                    }
                    hullOrder.Add(c);
                }
                if (hullOrder.Count > start) hullOrder.RemoveAt(hullOrder.Count - 1);
            }
            int length = hullOrder.Count * 2;
            if (hullPoints.Length < length) Array.Resize(ref hullPoints, Math.Max(length, hullPoints.Length * 2));
            for (int i = 0; i < hullOrder.Count; i++) { hullPoints[i * 2] = p[hullOrder[i] * 2]; hullPoints[i * 2 + 1] = p[hullOrder[i] * 2 + 1]; }
            return hullPoints.AsMemory(0, length);
        }

        private double Turn(int a, int b, int c)
        {
            var p = polygon.Span;
            return Cross(p[a * 2], p[a * 2 + 1], p[b * 2], p[b * 2 + 1], p[c * 2], p[c * 2 + 1]) * orientation;
        }

        public void Finish(int slot) { if (slot == EndSlot) Active = false; }

        public void Clip(ReadOnlySpan<float> vertices, ReadOnlySpan<float> uvs, ReadOnlySpan<uint> indices, MeshBuffer output)
        {
            output.VertexCount = output.IndexCount = 0;
            int pieces = inverse || convex ? 1 : triangles.Count / 3;
            var p = polygon.Span;
            for (int t = 0; t < indices.Length; t += 3) for (int piece = 0; piece < pieces; piece++)
            {
                a.Clear();
                for (int i = 0; i < 3; i++) { int v = (int)indices[t + i] * 2; a.Add(vertices[v]); a.Add(vertices[v + 1]); a.Add(uvs[v]); a.Add(uvs[v + 1]); }
                if (inverse && !convex) { Append(output, a); continue; }
                int edges = convex ? p.Length / 2 : 3;
                for (int edge = 0; edge < edges && a.Count != 0; edge++)
                {
                    int ia = convex ? edge : triangles[piece * 3 + edge], ib = convex ? (edge + 1) % edges : triangles[piece * 3 + (edge + 1) % 3];
                    double ax = p[ia * 2], ay = p[ia * 2 + 1], bx = p[ib * 2], by = p[ib * 2 + 1];
                    b.Clear(); outside.Clear();
                    int previous = a.Count - 4;
                    double distance = Cross(ax, ay, bx, by, a[previous], a[previous + 1]) * orientation;
                    for (int current = 0; current < a.Count; current += 4)
                    {
                        double next = Cross(ax, ay, bx, by, a[current], a[current + 1]) * orientation;
                        if ((distance >= 0) != (next >= 0))
                        {
                            double alpha = distance / (distance - next);
                            for (int k = 0; k < 4; k++)
                            {
                                double value = a[previous + k] + (a[current + k] - a[previous + k]) * alpha;
                                b.Add(value); if (inverse) outside.Add(value);
                            }
                        }
                        if (next >= 0) for (int k = 0; k < 4; k++) b.Add(a[current + k]);
                        else if (inverse) for (int k = 0; k < 4; k++) outside.Add(a[current + k]);
                        previous = current; distance = next;
                    }
                    if (inverse) Append(output, outside);
                    var swap = a; a = b; b = swap;
                }
                if (!inverse) Append(output, a);
            }
        }

        private static void Append(MeshBuffer output, List<double> p)
        {
            int count = p.Count / 4, first = output.VertexCount;
            if (count < 3) return;
            output.Reserve(first + count, output.IndexCount + (count - 2) * 3);
            for (int i = 0; i < count; i++)
            {
                output.Vertices[(first + i) * 2] = (float)p[i * 4]; output.Vertices[(first + i) * 2 + 1] = (float)p[i * 4 + 1];
                output.Uvs[(first + i) * 2] = (float)p[i * 4 + 2]; output.Uvs[(first + i) * 2 + 1] = (float)p[i * 4 + 3];
            }
            output.VertexCount += count;
            for (int i = 1; i + 1 < count; i++)
            {
                if (Cross(p[0], p[1], p[i * 4], p[i * 4 + 1], p[i * 4 + 4], p[i * 4 + 5]) == 0) continue;
                output.Indices[output.IndexCount++] = (uint)first;
                output.Indices[output.IndexCount++] = (uint)(first + i); output.Indices[output.IndexCount++] = (uint)(first + i + 1);
            }
        }
    }
}
