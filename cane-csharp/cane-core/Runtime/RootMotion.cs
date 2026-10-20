using System;
using System.Collections.Generic;

namespace Cane
{
    /// <summary>Local translation extracted from animation tracks. Consume after each
    /// AnimationState.Update; PoseX/Y retain the translation of unselected tracks.</summary>
    public sealed class RootMotion
    {
        public readonly int Bone;
        public int[] Tracks = Array.Empty<int>(); // Empty selects every track.
        public double DeltaX { get; private set; }
        public double DeltaY { get; private set; }
        public double PoseX { get; private set; }
        public double PoseY { get; private set; }
        private readonly double setupX, setupY;
        private double delta;
        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<double> boundaries = new List<double>();
        private readonly Dictionary<TrackEntry, double> starts = new Dictionary<TrackEntry, double>();
        private sealed class Sample
        {
            public bool Weighted;
            public readonly List<TrackEntry> Entries = new List<TrackEntry>();
            public int[] X = Array.Empty<int>(), Y = Array.Empty<int>();
            public double[] From = Array.Empty<double>(), Start = Array.Empty<double>(), Mixes = Array.Empty<double>();
        }
        public RootMotion(SkeletonData data, int bone)
        { Bone = bone; setupX = data.Setup[bone * 8]; setupY = data.Setup[bone * 8 + 1]; }
        /// <summary>One entry's unweighted local displacement between elapsed entry times.
        /// Includes ranges, reverse and completed loops; does not advance or change state.</summary>
        public (double X, double Y) Translation(TrackEntry entry, double from, double to)
        {
            double x = 0, y = 0;
            foreach (var timeline in entry.Animation?.Timelines ?? Array.Empty<Timeline>())
            {
                if (timeline.Channel == Bone * 8) x = Position(timeline, entry, to, setupX) - Position(timeline, entry, from, setupX);
                else if (timeline.Channel == Bone * 8 + 1) y = Position(timeline, entry, to, setupY) - Position(timeline, entry, from, setupY);
            }
            return (x, y);
        }
        internal void Begin(double seconds)
        {
            delta = seconds; DeltaX = DeltaY = 0; PoseX = setupX; PoseY = setupY;
            boundaries.Clear(); starts.Clear();
        }
        private bool Selected(int track) => Tracks.Length == 0 || Array.IndexOf(Tracks, track) >= 0;
        internal void Started(int index, TrackEntry entry, double remaining)
        {
            if (!Selected(index) || delta <= 0) return;
            double time = Math.Max(0, delta - remaining); starts[entry] = time;
            if (time > 0 && time < delta) boundaries.Add(time);
        }
        internal void Capture(int index, Track track)
        {
            while (samples.Count <= index) samples.Add(new Sample());
            var sample = samples[index];
            if (!Selected(index)) { sample.Entries.Clear(); return; }
            int count = track.Entries.Count; sample.Weighted = track.Weighted;
            if (sample.From.Length < count)
            {
                int size = Math.Max(count, sample.From.Length * 2);
                Array.Resize(ref sample.From, size); Array.Resize(ref sample.Start, size);
                Array.Resize(ref sample.Mixes, size); Array.Resize(ref sample.X, size); Array.Resize(ref sample.Y, size);
            }
            for (int i = 0; i < count; i++)
            {
                var entry = track.Entries[i];
                if (i >= sample.Entries.Count || sample.Entries[i] != entry)
                {
                    if (i == sample.Entries.Count) sample.Entries.Add(entry); else sample.Entries[i] = entry;
                    sample.X[i] = sample.Y[i] = -1;
                    var timelines = entry.Animation?.Timelines ?? Array.Empty<Timeline>();
                    for (int t = 0; t < timelines.Length; t++)
                    {
                        if (timelines[t].Channel == Bone * 8) sample.X[i] = t;
                        else if (timelines[t].Channel == Bone * 8 + 1) sample.Y[i] = t;
                    }
                }
                sample.From[i] = entry.EventTime; sample.Start[i] = starts.TryGetValue(entry, out double time) ? time : 0;
            }
            if (sample.Entries.Count > count) sample.Entries.RemoveRange(count, sample.Entries.Count - count);
        }
        internal void End(List<Track> tracks)
        {
            // Split only at entry activation. Each segment samples actual curve times
            // and uses its ending blend weights, including the exact lower-track interval.
            boundaries.Sort(); double from = 0;
            foreach (double to in boundaries) { if (to > from) Integrate(from, to); from = to; }
            Integrate(from, delta);
            bool lowerX = false, lowerY = false;
            for (int index = 0; index < tracks.Count; index++)
            {
                if (Selected(index)) continue;
                var track = tracks[index]; track.Prepare();
                foreach (var binding in track.Bindings)
                {
                    if (binding.Channel == Bone * 8) { PoseX = Retain(track, binding, PoseX, setupX, lowerX); lowerX = true; }
                    else if (binding.Channel == Bone * 8 + 1) { PoseY = Retain(track, binding, PoseY, setupY, lowerY); lowerY = true; }
                }
            }
        }
        private void Integrate(double from, double to)
        {
            double x = 0, y = 0, ago = delta - to;
            foreach (var sample in samples)
            {
                double total = 0;
                for (int i = 0; i < sample.Entries.Count; i++)
                {
                    var entry = sample.Entries[i];
                    bool active = entry.Delay == 0 && (delta <= 0 || sample.Start[i] < to);
                    double weight = !active ? 0 : sample.Weighted ? Math.Max(0, entry.Alpha) : i == 0 ? 1 : entry.Mix(ago);
                    sample.Mixes[i] = weight;
                    if (sample.Weighted && !entry.Additive) total += weight;
                }
                if (total > 1) for (int i = 0; i < sample.Entries.Count; i++)
                    if (!sample.Entries[i].Additive) sample.Mixes[i] /= total;
                x = Displacement(sample, sample.X, x, setupX, from, to);
                y = Displacement(sample, sample.Y, y, setupY, from, to);
            }
            DeltaX += x; DeltaY += y;
        }
        private double Displacement(Sample sample, int[] timelines, double value, double setup, double from, double to)
        {
            for (int i = 0; i < sample.Entries.Count; i++)
            {
                var entry = sample.Entries[i]; int t = timelines[i];
                if (t < 0 || sample.Mixes[i] == 0) continue;
                double alpha = Track.Contribution(sample.Entries, sample.Mixes, sample.Weighted, timelines, i);
                if (alpha == 0) continue;
                double first = sample.From[i], last = entry.Time;
                if (delta > 0)
                {
                    double start = sample.Start[i], duration = delta - start;
                    double length = last - first;
                    last = first + length * Math.Max(0, (to - start) / duration);
                    first += length * Math.Max(0, (from - start) / duration);
                }
                var timeline = entry.Animation!.Timelines[t];
                double movement = Position(timeline, entry, last, setup) - Position(timeline, entry, first, setup);
                value = entry.Additive ? value + movement * alpha : value + (movement - value) * alpha;
            }
            return value;
        }
        private static double Retain(Track track, ChannelBinding binding, double value, double setup, bool lower)
        {
            for (int i = 0; i < track.Entries.Count; i++)
            {
                var entry = track.Entries[i]; int t = binding.Timelines[i];
                if (t < 0 || entry.Delay > 0) continue;
                double alpha = track.Contribution(binding.Timelines, i);
                if (track.Weighted && alpha == 0) continue;
                var timeline = entry.Animation!.Timelines[t]; double time = track.Times[i]; int key = timeline.Locate(time);
                if (key < 0)
                {
                    if (!lower && !entry.Additive) value = i == binding.First ? setup : value + (setup - value) * alpha;
                    continue;
                }
                double pose = timeline.Value(time, key);
                value = entry.Additive ? value + (pose - setup) * alpha : value + (pose - value) * alpha;
            }
            return value;
        }
        /// <summary>Translation at an unwrapped entry time, including completed loops.</summary>
        public static double Position(Timeline timeline, TrackEntry entry, double time, double setup = 0)
        {
            double duration = Math.Max(0, entry.End - entry.Start), cycle = 0;
            if (entry.Loop && duration > 0) { cycle = Math.Floor(time / duration); time -= cycle * duration; }
            else time = Math.Max(0, Math.Min(duration, time));
            double sample = Value(timeline, entry.Reverse ? entry.Start + duration - time : entry.Start + time, setup);
            if (cycle != 0)
            {
                double distance = Value(timeline, entry.End, setup) - Value(timeline, entry.Start, setup);
                sample += cycle * (entry.Reverse ? -distance : distance);
            }
            return sample;
        }
        private static double Value(Timeline timeline, double time, double setup)
        { int key = timeline.Locate(time); return key < 0 ? setup : timeline.Value(time, key); }
    }
}
