using System;
using System.Collections.Generic;

namespace Cane
{
    /// <summary>Track selections compile channel ownership. Frames blend scalar values
    /// directly into the instance pose, without creating endpoint pose snapshots.</summary>
    public sealed class AnimationState
    {
        public double Speed = 1;
        public RootMotion? Motion;
        /// <summary>Consume in order and Clear after dispatch. Evaluation invokes no callbacks.</summary>
        public readonly List<RuntimeEvent> Events = new List<RuntimeEvent>();
        private readonly List<Track> tracks = new List<Track>();
        private int[] owned = Array.Empty<int>();
        private readonly HashSet<int> resetOnce = new HashSet<int>(), physicsResets = new HashSet<int>();
        private List<(int Slot, Geometry Geometry)> ownedDeforms = new List<(int, Geometry)>();
        private readonly List<(int Slot, Geometry Geometry)> resetDeforms = new List<(int, Geometry)>();
        private List<(int Slot, Sequence Sequence)> ownedSequences = new List<(int, Sequence)>();
        private readonly List<(int Slot, Sequence Sequence)> resetSequences = new List<(int, Sequence)>();
        private List<DrawOrderGroup> ownedOrders = new List<DrawOrderGroup>();
        private readonly List<DrawOrderGroup> resetOrders = new List<DrawOrderGroup>();
        private bool skinTracks;

        public int TrackCount => tracks.Count;
        public TrackEntry? Current(int track) => (uint)track < tracks.Count && tracks[track].Entries.Count > 0 ? tracks[track].Entries[tracks[track].Entries.Count - 1] : null;
        public IReadOnlyList<QueuedEntry> Queued(int track) => (uint)track < tracks.Count ? tracks[track].Queue : Array.Empty<QueuedEntry>();
        public bool RemoveQueued(int track, int index)
        {
            if ((uint)track >= tracks.Count || (uint)index >= tracks[track].Queue.Count) return false;
            var entry = tracks[track].Queue[index]; tracks[track].Queue.RemoveAt(index); Emit(EventType.Dispose, track, entry.Entry); return true;
        }
        public TrackEntry Set(int track, Animation? animation, bool loop = false, double mix = 0)
        {
            while (tracks.Count <= track) tracks.Add(new Track());
            var target = tracks[track]; var entry = new TrackEntry(animation, loop, mix); var current = Current(track);
            if (current != null) Emit(EventType.Interrupt, track, current);
            foreach (var queued in target.Queue) Emit(EventType.Dispose, track, queued.Entry); target.Queue.Clear();
            if (mix == 0 || target.Weighted) { foreach (var old in target.Entries) Finish(track, old); target.Entries.Clear(); }
            target.Weighted = false;
            target.Entries.Add(entry); target.Bind(); BindChannels(); Emit(EventType.Start, track, entry); return entry;
        }
        /// <summary>Creates simultaneous inputs for a blend tree or external animation graph.
        /// Set input Alpha weights and Time/Speed. Unused weight retains the underlay;
        /// replacement weights above one are normalized. Reconfigure only when inputs change.</summary>
        public TrackEntry[] SetBlend(int track, params Animation[] animations)
        {
            while (tracks.Count <= track) tracks.Add(new Track());
            var target = tracks[track]; var current = Current(track);
            if (current != null) Emit(EventType.Interrupt, track, current);
            foreach (var old in target.Entries) Finish(track, old);
            foreach (var queued in target.Queue) Emit(EventType.Dispose, track, queued.Entry);
            target.Entries.Clear(); target.Queue.Clear(); target.Weighted = true;
            var inputs = new TrackEntry[animations.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                var entry = new TrackEntry(animations[i], false, 0) { Alpha = 0, ShortestRotation = true };
                inputs[i] = entry; target.Entries.Add(entry); Emit(EventType.Start, track, entry);
            }
            target.Bind(); BindChannels(); return inputs;
        }
        public TrackEntry Add(int track, Animation? animation, bool loop = false, double delay = -1, double mix = 0)
        {
            var current = Current(track); if (current == null) return Set(track, animation, loop, mix);
            var target = tracks[track]; var previous = target.Queue.Count > 0 ? target.Queue[target.Queue.Count - 1].Entry : current;
            if (target.Weighted) throw new InvalidOperationException("Use Set to leave a blend track before queuing animations.");
            var entry = new TrackEntry(animation, loop, mix);
            if (delay < 0)
            {
                double duration = previous.End - previous.Start, complete = previous.Loop && duration > 0 ? (Math.Floor(previous.Time / duration) + 1) * duration : duration;
                delay = Math.Max(0, complete - mix * previous.Speed);
            }
            target.Queue.Add(new QueuedEntry(entry, delay)); return entry;
        }
        public void Clear(int track)
        {
            if ((uint)track >= tracks.Count) return;
            var target = tracks[track]; foreach (var old in target.Entries) Finish(track, old);
            foreach (var queued in target.Queue) Emit(EventType.Dispose, track, queued.Entry);
            target.Entries.Clear(); target.Queue.Clear(); target.Weighted = false; target.Bind(); BindChannels();
        }
        private void Emit(EventType type, int track, TrackEntry entry) => Events.Add(new RuntimeEvent(type, track, entry));
        private void Finish(int track, TrackEntry entry) { Emit(EventType.End, track, entry); Emit(EventType.Dispose, track, entry); }
        private void BindChannels()
        {
            skinTracks = false; var next = new HashSet<int>();
            foreach (var track in tracks)
            {
                foreach (var entry in track.Entries) skinTracks |= (entry.Animation?.Skins.Length ?? 0) > 0;
                for (int i = 0; i < track.Bindings.Length; i++) { int channel = track.Bindings[i].Channel; track.Lower[i] = (byte)(next.Contains(channel) ? 1 : 0); next.Add(channel); }
            }
            foreach (int channel in owned) if (!next.Contains(channel)) resetOnce.Add(channel);
            owned = new int[next.Count]; next.CopyTo(owned);
            var deforms = new List<(int Slot, Geometry Geometry)>();
            foreach (var track in tracks) foreach (var binding in track.Deforms)
            { var target = (binding.Slot, binding.Geometry); binding.Lower = deforms.Contains(target); if (!binding.Lower) deforms.Add(target); }
            foreach (var old in ownedDeforms) if (!deforms.Contains(old)) resetDeforms.Add(old); ownedDeforms = deforms;
            var sequences = new List<(int Slot, Sequence Sequence)>(); var groups = new List<DrawOrderGroup>();
            foreach (var track in tracks) foreach (var entry in track.Entries)
            {
                foreach (var t in entry.Animation?.Sequences ?? Array.Empty<SequenceTimeline>()) if (!sequences.Contains((t.Slot, t.Sequence))) sequences.Add((t.Slot, t.Sequence));
                foreach (var t in entry.Animation?.DrawOrders ?? Array.Empty<DrawOrderTimeline>()) if (!groups.Contains(t.Group)) groups.Add(t.Group);
            }
            foreach (var old in ownedSequences) if (!sequences.Contains(old)) resetSequences.Add(old); ownedSequences = sequences;
            foreach (var old in ownedOrders) if (!groups.Contains(old)) resetOrders.Add(old); ownedOrders = groups;
        }
        public void Update(double delta)
        {
            delta *= Speed; bool changed = false; Motion?.Begin(delta);
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                var track = tracks[trackIndex];
                for (int i = 0; i < track.Entries.Count; i++)
                {
                    var entry = track.Entries[i]; var next = i + 1 < track.Entries.Count ? track.Entries[i + 1] : null;
                    double elapsed = entry.Delay > 0 ? Math.Max(0, delta - entry.Delay) : delta;
                    if (entry.Delay > 0 && delta >= entry.Delay) Motion?.Started(trackIndex, entry, elapsed);
                    entry.Delay = Math.Max(0, entry.Delay - delta);
                    if (track.Weighted || next == null || next.Delay > 0 || next.MixTime < next.MixDuration) entry.Time += elapsed * entry.Speed;
                    entry.MixTime += elapsed;
                }
                bool rebound = false;
                while (track.Queue.Count > 0)
                {
                    var current = track.Entries[track.Entries.Count - 1]; var queued = track.Queue[0];
                    if (current.Time < queued.Delay || current.Speed <= 0) break;
                    double elapsed = (current.Time - queued.Delay) / current.Speed, active = Math.Max(0, elapsed - queued.Entry.Delay);
                    Motion?.Started(trackIndex, queued.Entry, active);
                    queued.Entry.Delay = Math.Max(0, queued.Entry.Delay - elapsed); track.Queue.RemoveAt(0);
                    queued.Entry.Time = active * queued.Entry.Speed; queued.Entry.MixTime = active; track.Entries.Add(queued.Entry); rebound = true;
                    Emit(EventType.Interrupt, trackIndex, current); Emit(EventType.Start, trackIndex, queued.Entry);
                }
                Motion?.Capture(trackIndex, track);
                for (int i = 0; i < track.Entries.Count; i++)
                {
                    var entry = track.Entries[i]; var next = i + 1 < track.Entries.Count ? track.Entries[i + 1] : null;
                    if (entry.Delay > 0) continue;
                    double incoming = next?.Mix() ?? 0;
                    if (!track.Weighted || entry.Alpha > 0)
                    {
                        EventCollector.Collect(entry, trackIndex, entry.EventTime, track.Weighted || next == null || next.Delay > 0 || incoming < entry.EventThreshold, Events);
                        EventCollector.PhysicsResets(entry, entry.EventTime, physicsResets);
                    }
                    entry.EventTime = entry.Time;
                }
                if (track.Weighted) continue;
                bool keepLast = track.Entries.Count > 1;
                for (int i = 1; i < track.Entries.Count; i++) keepLast &= track.Entries[i].Delay == 0 && track.Entries[i].MixTime >= track.Entries[i].MixDuration;
                if (keepLast)
                { int count = track.Entries.Count - 1; for (int i = 0; i < count; i++) Finish(trackIndex, track.Entries[i]); track.Entries.RemoveRange(0, count); rebound = true; }
                var latest = Current(trackIndex);
                if (latest != null && latest.Delay == 0 && latest.Time >= latest.TrackEnd && track.Queue.Count == 0)
                { foreach (var old in track.Entries) Finish(trackIndex, old); track.Entries.Clear(); rebound = true; }
                if (rebound) { track.Bind(); changed = true; }
            }
            if (changed) BindChannels();
            Motion?.End(tracks);
        }
        public void Apply(Skeleton skeleton)
        {
            ApplyPose(skeleton.Pose, skeleton.Data.Setup);
            if (skinTracks || skeleton.AnimationSkin != -2)
            {
                int skin = -2;
                foreach (var track in tracks) for (int i = 0; i < track.Entries.Count; i++)
                {
                    var entry = track.Entries[i]; var next = i + 1 < track.Entries.Count ? track.Entries[i + 1] : null;
                    if (entry.Delay > 0) continue;
                    bool outgoing = !track.Weighted && next != null && next.Delay == 0; double mix = outgoing ? track.Mixes[i + 1] : 0;
                    if (track.Weighted ? i != track.Dominant || track.Mixes[i] < entry.AlphaAttachmentThreshold : outgoing && mix >= entry.AttachmentThreshold || entry.Alpha * track.Mixes[i] * (1 - mix) < entry.AlphaAttachmentThreshold) continue;
                    var keys = entry.Animation?.Skins; if (keys == null || keys.Length == 0) continue;
                    int low = 0, high = keys.Length;
                    while (low < high) { int middle = (low + high) >> 1; if (keys[middle].Time <= track.Times[i]) low = middle + 1; else high = middle; }
                    if (low > 0) skin = keys[low - 1].Skin;
                }
                skeleton.SetAnimationSkin(skin);
            }
            foreach (int index in physicsResets)
            {
                if (index < 0) { for (int i = 0; i < skeleton.Physics.Length; i++) if (skeleton.ActiveConstraints[i] != 0) skeleton.Physics[i]?.Reset(); }
                else if (skeleton.ActiveConstraints[index] != 0) skeleton.Physics[index]?.Reset();
            }
            physicsResets.Clear(); var frame = skeleton.Frame; if (frame == null) return;
            foreach (var group in resetOrders) group.Reorder(frame.Order, group.Slots); resetOrders.Clear();
            foreach (var group in ownedOrders) group.Reorder(frame.Order, group.Slots);
            foreach (var track in tracks) for (int i = 0; i < track.Entries.Count; i++)
            {
                var entry = track.Entries[i]; var next = i + 1 < track.Entries.Count ? track.Entries[i + 1] : null;
                if (entry.Delay > 0 || track.Weighted && i != track.Dominant) continue;
                var timelines = entry.Animation?.DrawOrders ?? Array.Empty<DrawOrderTimeline>();
                for (int t = 0; t < timelines.Length; t++)
                {
                    var timeline = timelines[t];
                    if (!track.Weighted && next != null && next.Delay == 0 && (timeline.Group.Slots.Length != frame.Order.Length || track.Mixes[i + 1] >= entry.DrawOrderThreshold)) continue;
                    int key = timeline.Timing.Locate(track.Times[i], entry.OrderCursors[t]); entry.OrderCursors[t] = key;
                    if (key >= 0) timeline.Apply(frame.Order, key);
                }
            }
            ResetSequences(skeleton, resetSequences); resetSequences.Clear(); ResetSequences(skeleton, ownedSequences);
            foreach (var track in tracks)
            {
                int i = track.Weighted ? track.Dominant : track.Entries.Count - 1; while (i >= 0 && track.Entries[i].Delay > 0) i--;
                if (i < 0) continue;
                var entry = track.Entries[i]; var timelines = entry.Animation?.Sequences ?? Array.Empty<SequenceTimeline>();
                for (int t = 0; t < timelines.Length; t++)
                {
                    var timeline = timelines[t]; var slot = frame.PoseSlots[timeline.Slot];
                    if (slot.Sequence != timeline.Sequence || slot.Attachment < 0) continue;
                    double time = track.Times[i]; int key = timeline.Timing.Locate(time, entry.SequenceCursors[t]); entry.SequenceCursors[t] = key;
                    if (key >= 0) slot.SequenceIndex = timeline.Value(time, key);
                }
            }
            ResetDeforms(skeleton, resetDeforms); resetDeforms.Clear(); ResetDeforms(skeleton, ownedDeforms);
            foreach (var track in tracks) foreach (var binding in track.Deforms)
            {
                if (frame.PoseSlots[binding.Slot].Geometry != binding.Geometry) continue;
                var output = frame.Deform(binding.Slot)!;
                for (int i = 0; i < track.Entries.Count; i++)
                {
                    var entry = track.Entries[i]; int index = binding.Timelines[i];
                    if (entry.Delay > 0 || index < 0) continue;
                    var timeline = entry.Animation!.Deforms[index]; double time = track.Times[i];
                    int key = timeline.Timing.Locate(time, entry.DeformCursors[index]); entry.DeformCursors[index] = key;
                    double alpha = track.Contribution(binding.Timelines, i);
                    if (track.Weighted && alpha == 0) continue;
                    if (key >= 0) timeline.Apply(output, time, key, alpha, entry.Additive);
                    else if (!binding.Lower && !entry.Additive)
                    { double retain = i == binding.First ? 0 : 1 - alpha; for (int v = 0; v < output.Length; v++) output[v] *= retain; }
                }
            }
        }
        private static void ResetSequences(Skeleton skeleton, List<(int Slot, Sequence Sequence)> targets)
        {
            var frame = skeleton.Frame!;
            foreach (var target in targets)
            {
                frame.Resolve(target.Slot, (int)skeleton.Pose[skeleton.Data.SlotOffset + target.Slot * 8], true);
                var slot = frame.PoseSlots[target.Slot]; if (slot.Sequence == target.Sequence) slot.SequenceIndex = -1;
            }
        }
        private static void ResetDeforms(Skeleton skeleton, List<(int Slot, Geometry Geometry)> targets)
        {
            var frame = skeleton.Frame!;
            foreach (var target in targets)
            {
                frame.Resolve(target.Slot, (int)skeleton.Pose[skeleton.Data.SlotOffset + target.Slot * 8], true);
                if (frame.PoseSlots[target.Slot].Geometry == target.Geometry) { var deform = frame.Deform(target.Slot)!; Array.Clear(deform, 0, deform.Length); }
            }
        }
        public void ApplyPose(Span<double> pose, ReadOnlySpan<double> setup)
        {
            foreach (int channel in resetOnce) pose[channel] = setup[channel]; resetOnce.Clear();
            foreach (var track in tracks)
            {
                track.Prepare();
                if (track.Entries.Count == 1)
                {
                    var e = track.Entries[0];
                    if (e.Delay == 0 && e.Alpha == 1 && !e.Additive && e.AlphaAttachmentThreshold == 0)
                    {
                        double time = track.Times[0];
                        for (int b = 0; b < track.Bindings.Length; b++)
                        {
                            var binding = track.Bindings[b]; int index = binding.Timelines[0]; var timeline = e.Animation!.Timelines[index];
                            int key = timeline.Locate(time, e.Cursors[index]); e.Cursors[index] = key;
                            if (key >= 0) pose[binding.Channel] = timeline.Value(time, key);
                            else if (track.Lower[b] == 0) pose[binding.Channel] = setup[binding.Channel];
                        }
                        continue;
                    }
                }
                for (int b = 0; b < track.Bindings.Length; b++)
                {
                    var binding = track.Bindings[b]; bool lower = track.Lower[b] != 0;
                    double value = lower ? pose[binding.Channel] : setup[binding.Channel];
                    for (int i = 0; i < track.Entries.Count; i++)
                    {
                        var entry = track.Entries[i]; int index = binding.Timelines[i];
                        if (entry.Delay > 0 || index < 0) continue;
                        bool outgoing = !track.Weighted && i + 1 < track.Entries.Count && track.Entries[i + 1].Delay == 0, discrete = Animation.IsDiscrete(binding.Kind);
                        if (track.Weighted && discrete && i != track.Dominant) continue;
                        double alpha = track.Contribution(binding.Timelines, i, discrete), time = track.Times[i]; var timeline = entry.Animation!.Timelines[index];
                        if (track.Weighted && alpha == 0) continue;
                        int key = timeline.Locate(time, entry.Cursors[index]); entry.Cursors[index] = key;
                        if (key < 0)
                        {
                            if (!lower && !discrete && !entry.Additive) value = i == binding.First ? setup[binding.Channel] : track.Blend(b, i, value, setup[binding.Channel], alpha, false);
                            continue;
                        }
                        double sample = timeline.Value(time, key);
                        if (discrete)
                        {
                            if ((!outgoing || track.Mixes[i + 1] < entry.AttachmentThreshold) && (binding.Kind != ChannelKind.Attachment || alpha >= entry.AlphaAttachmentThreshold)) value = sample;
                        }
                        else if (entry.Additive) value += (sample - setup[binding.Channel]) * alpha;
                        else value = track.Blend(b, i, value, sample, alpha, !outgoing);
                    }
                    pose[binding.Channel] = value;
                }
            }
        }
    }
}
