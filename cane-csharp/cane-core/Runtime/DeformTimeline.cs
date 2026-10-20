using System;
using System.Collections.Generic;

namespace Cane
{
    public readonly struct DeformKey
    {
        public readonly double Time;
        public readonly double[] Vertices;
        public readonly Curve Curve;
        public DeformKey(double time, double[] vertices, Curve curve = default) { Time = time; Vertices = vertices; Curve = curve; }
    }

    public sealed class DeformOffset
    {
        public readonly Timeline Timing;
        public readonly double[] Direction;
        public DeformOffset(Timeline timing, double[] direction) { Timing = timing; Direction = direction; }
    }

    public sealed class DeformTimeline
    {
        public readonly int Slot;
        public readonly Geometry Geometry;
        public readonly Timeline Timing;
        public readonly uint[] Frames;
        public readonly double[][] Vertices;
        public readonly DeformOffset? Offset;

        public DeformTimeline(int slot, Geometry geometry, Timeline timing, uint[] frames, double[][] vertices, DeformOffset? offset = null)
        { Slot = slot; Geometry = geometry; Timing = timing; Frames = frames; Vertices = vertices; Offset = offset; }

        public DeformTimeline(int slot, Geometry geometry, ReadOnlySpan<DeformKey> input)
        {
            Slot = slot; Geometry = geometry;
            var keys = new List<DeformKey>(); bool offset = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (i + 1 < input.Length && input[i].Time == input[i + 1].Time) continue;
                keys.Add(input[i]); offset |= input[i].Curve.Kind == 3;
            }
            var timing = new List<Key>(); Vertices = new double[keys.Count][];
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i]; Curve curve = key.Curve;
                if (curve.Kind == 3) curve = Curve.Bezier(curve.X1, 0, curve.X2, 1);
                timing.Add(new Key(key.Time, 0, curve));
                if (i + 1 < keys.Count) timing.Add(new Key(keys[i + 1].Time, 1));
                Vertices[i] = key.Vertices;
            }
            Timing = new Timeline(0, timing.ToArray()); Frames = new uint[Timing.Count];
            for (int i = 0, k = 0; i < Frames.Length; i++)
            { while (k + 1 < keys.Count && Timing.Times[i] >= keys[k + 1].Time) k++; Frames[i] = (uint)k; }
            if (offset)
            {
                var controls = new Key[keys.Count];
                for (int i = 0; i < controls.Length; i++) controls[i] = new Key(keys[i].Time, 0, keys[i].Curve.Kind == 3 ? keys[i].Curve : Curve.Linear);
                var direction = new double[keys[0].Vertices.Length]; Array.Fill(direction, 1);
                Offset = new DeformOffset(new Timeline(0, controls), direction);
            }
        }

        public void Apply(Span<double> output, double time, int key, double alpha, bool additive)
        {
            int frame = (int)Frames[key]; var a = Vertices[frame]; var b = frame + 1 < Vertices.Length ? Vertices[frame + 1] : a;
            double mix = Timing.Value(time, key);
            if (alpha == 1 && !additive && mix == 0) a.AsSpan().CopyTo(output);
            else for (int i = 0; i < output.Length; i++)
            {
                double value = a[i] + (b[i] - a[i]) * mix;
                output[i] = additive ? output[i] + value * alpha : output[i] + (value - output[i]) * alpha;
            }
            if (Offset != null)
            {
                int at = Offset.Timing.Locate(time, key); double value = at < 0 ? 0 : Offset.Timing.Value(time, at) * alpha;
                if (value != 0) for (int i = 0; i < output.Length; i++) output[i] += Offset.Direction[i] * value;
            }
        }
    }
}
