using System;
using System.Collections.Generic;

namespace Cane
{
    public sealed class TrackEntry
    {
        public readonly Animation? Animation;
        public bool Loop, Additive, Reverse, ShortestRotation;
        public double Time, Speed = 1, Alpha = 1, Start, End, MixTime, MixDuration, Delay, TrackEnd = double.PositiveInfinity;
        public double EventThreshold, AttachmentThreshold, AlphaAttachmentThreshold, DrawOrderThreshold;
        public Timeline? MixCurve;
        internal int RotationVersion;
        internal double EventTime = -2.220446049250313e-16;
        internal readonly int[] Cursors, DeformCursors, SequenceCursors, OrderCursors;
        internal TrackEntry(Animation? animation, bool loop, double mixDuration)
        {
            Animation = animation; Loop = loop; MixDuration = mixDuration; End = animation?.Duration ?? 0;
            if (animation == null) TrackEnd = mixDuration;
            Cursors = new int[animation?.Timelines.Length ?? 0]; DeformCursors = new int[animation?.Deforms.Length ?? 0];
            SequenceCursors = new int[animation?.Sequences.Length ?? 0]; OrderCursors = new int[animation?.DrawOrders.Length ?? 0];
        }
        public double SampleTime()
        {
            double duration = Math.Max(0, End - Start), time = Loop && duration > 0 ? Cane.Animation.Modulo(Time, duration) : Math.Max(0, Math.Min(duration, Time));
            return Reverse ? Start + duration - time : Start + time;
        }
        public void Seek(double time, bool emitEvents = false) { Time = time; ResetRotationDirections(); if (!emitEvents) EventTime = time; }
        public void ResetRotationDirections() => RotationVersion++;
        public double Mix(double ago = 0)
        {
            if (Delay > 0) return 0;
            double ratio = MixDuration == 0 ? 1 : Math.Max(0, Math.Min(1, (MixTime - ago) / MixDuration)); int key = MixCurve?.Locate(ratio) ?? -1;
            return key >= 0 ? MixCurve!.Value(ratio, key) : ratio;
        }
    }
    public sealed class QueuedEntry
    {
        public readonly TrackEntry Entry;
        public double Delay;
        internal QueuedEntry(TrackEntry entry, double delay) { Entry = entry; Delay = delay; }
    }
    internal sealed class DeformBinding
    {
        public readonly int Slot, First;
        public readonly Geometry Geometry;
        public readonly int[] Timelines;
        public bool Lower;
        public DeformBinding(DeformTimeline timeline, int first, int count)
        { Slot = timeline.Slot; Geometry = timeline.Geometry; First = first; Timelines = new int[count]; Array.Fill(Timelines, -1); }
    }
    internal sealed class Track
    {
        public bool Weighted;
        public int Dominant = -1;
        public readonly List<TrackEntry> Entries = new List<TrackEntry>();
        public readonly List<QueuedEntry> Queue = new List<QueuedEntry>();
        public ChannelBinding[] Bindings = Array.Empty<ChannelBinding>();
        public byte[] Lower = Array.Empty<byte>();
        private double[] rotations = Array.Empty<double>();
        public readonly List<DeformBinding> Deforms = new List<DeformBinding>();
        public double[] Times = Array.Empty<double>(), Mixes = Array.Empty<double>();
        private TrackEntry[] boundEntries = Array.Empty<TrackEntry>();
        public void Prepare()
        {
            if (!Weighted)
            {
                for (int i = 0; i < Entries.Count; i++) { var entry = Entries[i]; Times[i] = entry.SampleTime(); Mixes[i] = i == 0 ? 1 : entry.Mix(); }
                return;
            }
            double total = 0; Dominant = -1;
            foreach (var entry in Entries) if (entry.Delay == 0 && !entry.Additive) total += Math.Max(0, entry.Alpha);
            double divisor = Math.Max(1, total), greatest = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i]; Times[i] = entry.SampleTime();
                double weight = entry.Delay > 0 ? 0 : Math.Max(0, entry.Alpha) / (entry.Additive ? 1 : divisor);
                Mixes[i] = weight;
                if (weight > 0 && weight >= greatest) { greatest = weight; Dominant = i; }
            }
        }
        public void Bind()
        {
            var previous = Bindings; var history = rotations;
            if (Entries.Count == 1) Bindings = Entries[0].Animation?.Bindings ?? Array.Empty<ChannelBinding>();
            else
            {
                var animations = new Animation?[Entries.Count]; for (int i = 0; i < animations.Length; i++) animations[i] = Entries[i].Animation;
                Bindings = ChannelBinding.Bind(animations);
            }
            Lower = new byte[Bindings.Length];
            if (history.Length > 0)
            {
                var channels = new Dictionary<int, int>(); for (int i = 0; i < previous.Length; i++) channels[previous[i].Channel] = i;
                rotations = new double[Bindings.Length * Entries.Count * 3]; Array.Fill(rotations, double.NaN);
                for (int b = 0; b < Bindings.Length; b++)
                {
                    if (!channels.TryGetValue(Bindings[b].Channel, out int old)) continue;
                    for (int e = 0; e < Entries.Count; e++)
                    {
                        int entry = Array.IndexOf(boundEntries, Entries[e]);
                        if (entry >= 0) Array.Copy(history, (old * boundEntries.Length + entry) * 3, rotations, (b * Entries.Count + e) * 3, 3);
                    }
                }
            }
            Deforms.Clear();
            for (int e = 0; e < Entries.Count; e++)
            {
                var deforms = Entries[e].Animation?.Deforms ?? Array.Empty<DeformTimeline>();
                for (int t = 0; t < deforms.Length; t++)
                {
                    var timeline = deforms[t]; DeformBinding? binding = null;
                    foreach (var b in Deforms) if (b.Slot == timeline.Slot && b.Geometry == timeline.Geometry) { binding = b; break; }
                    if (binding == null) { binding = new DeformBinding(timeline, e, Entries.Count); Deforms.Add(binding); }
                    binding.Timelines[e] = t;
                }
            }
            Times = new double[Entries.Count]; Mixes = new double[Entries.Count]; boundEntries = Entries.ToArray();
        }
        public double Blend(int bindingIndex, int index, double from, double to, double alpha, bool incoming = true)
        {
            var binding = Bindings[bindingIndex]; var entry = Entries[index];
            if (binding.Kind == ChannelKind.Angle && entry.ShortestRotation && Lower[bindingIndex] == 0 && index == binding.First) return from + (to - from) * alpha;
            if (binding.Kind != ChannelKind.Angle || entry.ShortestRotation || alpha == 0 || alpha == 1) return Animation.MixValue(from, to, alpha, binding.Kind, incoming);
            if (rotations.Length == 0) { rotations = new double[Bindings.Length * Entries.Count * 3]; Array.Fill(rotations, double.NaN); }
            int at = (bindingIndex * Entries.Count + index) * 3;
            if (rotations[at + 2] != entry.RotationVersion) { rotations[at] = rotations[at + 1] = double.NaN; rotations[at + 2] = entry.RotationVersion; }
            double raw = Animation.RotationDelta(to - from), turn = double.IsNaN(rotations[at]) ? raw : rotations[at + 1] + Affine.WrapAngle(raw - rotations[at]);
            rotations[at] = raw; rotations[at + 1] = turn; return from + turn * alpha;
        }
        public double Contribution(ReadOnlySpan<int> timelines, int index, bool discrete = false)
            => Contribution(Entries, Mixes, Weighted, timelines, index, discrete);
        internal static double Contribution(List<TrackEntry> entries, ReadOnlySpan<double> mixes, bool weighted, ReadOnlySpan<int> timelines, int index, bool discrete = false)
        {
            if (weighted)
            {
                double weight = mixes[index];
                if (discrete || entries[index].Additive) return weight;
                double remainder = 1;
                for (int i = index + 1; i < entries.Count; i++) if (timelines[i] >= 0 && !entries[i].Additive) remainder -= mixes[i];
                return remainder > 0 ? Math.Min(1, weight / remainder) : 0;
            }
            var entry = entries[index]; double alpha = entry.Alpha * mixes[index];
            if (index + 1 < entries.Count)
            {
                var next = entries[index + 1]; double mix = mixes[index + 1];
                if (!entry.Additive && !discrete && !next.Additive && timelines[index + 1] >= 0)
                {
                    double keep = 1 - mix * next.Alpha; if (keep > 0) alpha *= (1 - mix) / keep;
                    for (int later = index + 2; later < entries.Count; later++)
                        if (entries[later].Additive || timelines[later] < 0) { alpha *= 1 - mixes[later]; break; }
                }
                else alpha *= 1 - mix;
            }
            return alpha;
        }
    }
}
