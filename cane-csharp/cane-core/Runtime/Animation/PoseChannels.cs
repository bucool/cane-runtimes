using System;
using System.Collections.Generic;

namespace Cane.Animation
{
    internal enum PoseChannelKind { Scalar, Angle, Bounded, Vector, Choice, Order }
    internal readonly struct PoseValue
    {
        internal readonly float Number;
        internal readonly object? Choice;
        internal readonly float[]? Vector;
        internal PoseValue(float value) { Number = value; Choice = null; Vector = null; }
        internal PoseValue(object? value) { Number = 0; Choice = value; Vector = null; }
        internal PoseValue(float[] value) { Number = 0; Choice = null; Vector = value; }
    }
    internal sealed class PoseChannel
    {
        internal readonly PoseChannelKind Kind;
        internal readonly PoseValue Setup;
        internal readonly float Minimum, Maximum;
        internal PoseChannel(PoseChannelKind kind, PoseValue setup, float minimum = 0, float maximum = 1)
        { Kind = kind; Setup = setup; Minimum = minimum; Maximum = maximum; }
    }
    internal sealed class AnimationWork
    {
        private long remaining = 1_000_000_000;
        internal void Use(long amount = 1)
        {
            if (amount < 0 || amount > remaining) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "apply", "Animation work limit exceeded.");
            remaining -= amount;
        }
    }
    internal sealed class ChannelSample
    {
        internal readonly Dictionary<int, PoseValue> Values = new Dictionary<int, PoseValue>();
        internal readonly List<int[]> OrderPatches = new List<int[]>();
        internal float Weight;
        internal bool Additive;
    }
    internal static class PoseChannels
    {
        internal static float Narrow(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue)
                throw new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Animation channel exceeded binary32 range.");
            return (float)value;
        }
        private static double Angle(double value) => ((value + 180) % 360 + 360) % 360 - 180;
        private static PoseValue Mix(PoseChannel channel, PoseValue from, PoseValue to, float weight, bool additive)
        {
            if (weight == 0) return from;
            if (channel.Kind == PoseChannelKind.Choice || channel.Kind == PoseChannelKind.Order) return weight >= .5f ? to : from;
            double a = from.Number, b = to.Number, setup = channel.Setup.Number;
            if (channel.Kind == PoseChannelKind.Vector)
            {
                float[] x = from.Vector!, y = to.Vector!, baseline = channel.Setup.Vector!;
                if (x.Length != y.Length || x.Length != baseline.Length) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "apply", "Animation vector layout changed.");
                var values = new float[x.Length];
                for (int i = 0; i < values.Length; i++) values[i] = Narrow(additive ? x[i] + (double)weight * ((double)y[i] - baseline[i]) : (1.0 - weight) * x[i] + (double)weight * y[i]);
                return new PoseValue(values);
            }
            if (channel.Kind == PoseChannelKind.Angle) return new PoseValue(Narrow(!additive && weight == 1 ? b : a + weight * Angle(b - (additive ? setup : a))));
            double value = additive ? a + weight * (b - setup) : (1.0 - weight) * a + weight * b;
            if (channel.Kind == PoseChannelKind.Bounded) value = Math.Max(channel.Minimum, Math.Min(channel.Maximum, value));
            return new PoseValue(Narrow(value));
        }
        internal static PoseValue[] Apply(IReadOnlyList<PoseChannel> schema, PoseValue[] underlay, ChannelSample sample, int orderIndex, AnimationWork work)
        {
            work.Use(schema.Count + sample.Values.Count);
            var result = (PoseValue[])underlay.Clone();
            if (sample.Weight == 0) return result;
            foreach (var pair in sample.Values)
            {
                var channel = schema[pair.Key];
                if (channel.Kind == PoseChannelKind.Vector) work.Use(pair.Value.Vector!.Length);
                result[pair.Key] = Mix(channel, underlay[pair.Key], pair.Value, sample.Weight, sample.Additive);
            }
            if (sample.Weight >= .5f && sample.OrderPatches.Count != 0)
            {
                var order = (int[])((int[])underlay[orderIndex].Choice!).Clone();
                foreach (int[] patch in sample.OrderPatches)
                {
                    work.Use(order.Length + patch.Length);
                    var members = new HashSet<int>(patch);
                    if (members.Count != patch.Length) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "apply", "Duplicate order member.");
                    int cursor = 0;
                    for (int i = 0; i < order.Length; i++) if (members.Contains(order[i])) order[i] = patch[cursor++];
                    if (cursor != patch.Length) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "apply", "Unknown order member.");
                }
                result[orderIndex] = new PoseValue((object)order);
            }
            return result;
        }
        internal static PoseValue[] Transition(IReadOnlyList<PoseChannel> schema, PoseValue[] from, PoseValue[] to, ChannelSample sample, float progress, bool hold, AnimationWork work)
        {
            if (progress == 0) return from;
            if (progress == 1) return to;
            work.Use(schema.Count); var result = new PoseValue[schema.Count];
            for (int i = 0; i < result.Length; i++)
            {
                if (hold && !sample.Values.ContainsKey(i)) { result[i] = from[i]; continue; }
                if (schema[i].Kind == PoseChannelKind.Vector) work.Use(from[i].Vector!.Length);
                result[i] = Mix(schema[i], from[i], to[i], progress, false);
            }
            return result;
        }
    }
}
