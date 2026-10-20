using System;
using System.Collections.Generic;

namespace Cane
{
    public readonly struct EventKey
    {
        public readonly double Time, Number, Volume, Balance;
        public readonly int Integer;
        public readonly string Name, Text;
        public readonly string? Audio;
        public EventKey(double time, string name, int integer = 0, double number = 0, string text = "", string? audio = null, double volume = 1, double balance = 0)
        { Time = time; Name = name; Integer = integer; Number = number; Text = text; Audio = audio; Volume = volume; Balance = balance; }
    }

    public readonly struct SkinKey
    {
        public readonly double Time;
        public readonly int Skin;
        public SkinKey(double time, int skin = -1) { Time = time; Skin = skin; }
    }

    public readonly struct PhysicsResetKey
    {
        public readonly double Time;
        public readonly int Constraint;
        public PhysicsResetKey(double time, int constraint = -1) { Time = time; Constraint = constraint; }
    }

    internal sealed class ChannelBinding
    {
        public readonly int Channel, First;
        public readonly ChannelKind Kind;
        public readonly int[] Timelines;
        private ChannelBinding(Timeline timeline, int first, int count)
        { Channel = timeline.Channel; Kind = timeline.Kind; First = first; Timelines = new int[count]; Array.Fill(Timelines, -1); }
        public static ChannelBinding[] Bind(params Animation?[] animations)
        {
            var indices = new Dictionary<int, ChannelBinding>(); var bindings = new List<ChannelBinding>();
            for (int e = 0; e < animations.Length; e++)
            {
                var timelines = animations[e]?.Timelines ?? Array.Empty<Timeline>();
                for (int i = 0; i < timelines.Length; i++)
                {
                    var timeline = timelines[i];
                    if (!indices.TryGetValue(timeline.Channel, out var binding))
                    { binding = new ChannelBinding(timeline, e, animations.Length); indices.Add(timeline.Channel, binding); bindings.Add(binding); }
                    binding.Timelines[e] = i;
                }
            }
            return bindings.ToArray();
        }
    }

    public sealed class Animation
    {
        public string Name { get; }
        public double Duration { get; }
        public readonly Timeline[] Timelines;
        public readonly DeformTimeline[] Deforms;
        public readonly SequenceTimeline[] Sequences;
        public readonly DrawOrderTimeline[] DrawOrders;
        public readonly EventKey[] Events;
        public readonly PhysicsResetKey[] PhysicsResets;
        public readonly SkinKey[] Skins;
        internal readonly ChannelBinding[] Bindings;

        public Animation(string name, Timeline[] timelines, double? duration = null, DeformTimeline[]? deforms = null,
            SequenceTimeline[]? sequences = null, DrawOrderTimeline[]? drawOrders = null, EventKey[]? events = null,
            PhysicsResetKey[]? physicsResets = null, SkinKey[]? skins = null)
        {
            Name = name; Timelines = timelines; Deforms = deforms ?? Array.Empty<DeformTimeline>();
            Sequences = sequences ?? Array.Empty<SequenceTimeline>(); DrawOrders = drawOrders ?? Array.Empty<DrawOrderTimeline>();
            Events = events ?? Array.Empty<EventKey>(); PhysicsResets = physicsResets ?? Array.Empty<PhysicsResetKey>();
            Skins = skins == null ? Array.Empty<SkinKey>() : (SkinKey[])skins.Clone();
            // Keep author order at duplicate times.
            for (int i = 1; i < Skins.Length; i++)
            { var key = Skins[i]; int j = i; while (j > 0 && Skins[j - 1].Time > key.Time) { Skins[j] = Skins[j - 1]; j--; } Skins[j] = key; }
            double end = 0;
            foreach (var t in timelines) end = Math.Max(end, End(t));
            foreach (var t in Deforms) end = Math.Max(end, End(t.Timing));
            foreach (var t in Sequences) end = Math.Max(end, End(t.Timing));
            foreach (var t in DrawOrders) end = Math.Max(end, End(t.Timing));
            if (Events.Length > 0) end = Math.Max(end, Events[Events.Length - 1].Time);
            if (PhysicsResets.Length > 0) end = Math.Max(end, PhysicsResets[PhysicsResets.Length - 1].Time);
            if (Skins.Length > 0) end = Math.Max(end, Skins[Skins.Length - 1].Time);
            Duration = duration ?? end; Bindings = ChannelBinding.Bind(this);
        }

        private static double End(Timeline timeline) => timeline.Count == 0 ? 0 : timeline.Times[timeline.Count - 1];
        internal static bool IsDiscrete(ChannelKind kind) => kind == ChannelKind.Discrete || kind == ChannelKind.Attachment;
        internal static double RotationDelta(double value)
        { double delta = Affine.WrapAngle(value); return delta == -180 && value > 0 ? 180 : delta; }
        internal static double Modulo(double value, double period) => (value % period + period) % period;
        internal static double MixValue(double from, double to, double alpha, ChannelKind kind, bool incoming = true)
        {
            if (alpha == 0) return from;
            if (alpha == 1 || IsDiscrete(kind)) return to;
            if (kind == ChannelKind.Scale)
            {
                if (incoming) from = Math.Abs(from) * (to == 0 ? (from < 0 ? -1 : 1) : to < 0 ? -1 : 1);
                else to = Math.Abs(to) * (from < 0 ? -1 : 1);
            }
            return from + (kind == ChannelKind.Angle ? RotationDelta(to - from) : to - from) * alpha;
        }

        /// <summary>Samples bound channels without resetting unrelated host pose values.</summary>
        public void Apply(Span<double> pose, ReadOnlySpan<double> setup, double time, double alpha = 1, bool additive = false, bool loop = false, Span<int> cursors = default)
        {
            if (loop && Duration > 0) time = Modulo(time, Duration);
            bool replace = alpha == 1 && !additive;
            for (int i = 0; i < Timelines.Length; i++)
            {
                var timeline = Timelines[i]; int key = timeline.Locate(time, cursors.IsEmpty ? 0 : cursors[i]);
                if (!cursors.IsEmpty) cursors[i] = key;
                if (key < 0) continue;
                int channel = timeline.Channel; double value = timeline.Value(time, key);
                pose[channel] = replace || timeline.IsDiscrete ? value : additive ? pose[channel] + (value - setup[channel]) * alpha : MixValue(pose[channel], value, alpha, timeline.Kind);
            }
        }
    }
}
