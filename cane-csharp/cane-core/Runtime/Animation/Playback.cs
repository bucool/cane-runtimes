using System;
using System.Collections.Generic;

namespace Cane
{
    public enum TrackBlend { Replace, Additive }
    public sealed class TrackOptions
    {
        public float? BlendWeight { get; set; }
        public float? PlaybackRate { get; set; }
        public bool? Looping { get; set; }
        public TrackBlend? Blend { get; set; }
        public float? EventBlendThreshold { get; set; }
        public float? AttachmentBlendThreshold { get; set; }
        public float? DrawOrderBlendThreshold { get; set; }
        public bool? HoldPreviousPose { get; set; }
    }
    public sealed class QueuedEntryOptions
    {
        public float? StartDelaySeconds { get; set; }
        public float? BlendDurationSeconds { get; set; }
        public bool? Looping { get; set; }
    }
    public readonly struct SamplingOptions
    {
        public readonly bool Stepped;
        public readonly float FramesPerSecond;
        public readonly float? FrameStepSeconds;
        public SamplingOptions(bool stepped = false, float framesPerSecond = 0)
        { Stepped = stepped; FramesPerSecond = framesPerSecond; FrameStepSeconds = null; }
        private SamplingOptions(float frameStepSeconds, bool stepped)
        { Stepped = stepped; FramesPerSecond = 0; FrameStepSeconds = frameStepSeconds; }
        public static SamplingOptions FixedFrame(float frameStepSeconds, bool stepped = false) => new SamplingOptions(frameStepSeconds, stepped);
        internal float Time(float value)
        {
            if (!FrameStepSeconds.HasValue && FramesPerSecond == 0) return value;
            float step = FrameStepSeconds ?? (1 / FramesPerSecond);
            float result = (float)(Math.Floor((double)value / step + 0.5) * step);
            if (!Numeric.Finite(result)) throw new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Fixed-frame sample time became non-finite.", "sampling");
            return result;
        }
        internal void Validate(string op)
        {
            Numeric.RequireFinite(FramesPerSecond, op, "sampling.fps");
            if (FramesPerSecond < 0) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, op, "Sampling fps must be positive or zero for authored sampling.", "sampling.fps");
            if (FrameStepSeconds.HasValue || FramesPerSecond > 0)
            {
                float step = FrameStepSeconds ?? (1 / FramesPerSecond); Numeric.RequireFinite(step, op, "sampling.frameStepSeconds");
                if (step <= 0) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, op, "Frame step must be positive.", "sampling.frameStepSeconds");
            }
        }
    }
    public sealed class RuntimeStep
    {
        public bool Changed { get; }
        public float TimeSeconds { get; }
        public ulong FrameSequence { get; }
        public IReadOnlyList<RuntimeEvent> Events { get; }
        internal RuntimeStep(bool changed, RuntimeFrame frame, RuntimeEvent[] events)
        { Changed = changed; TimeSeconds = frame.TimeSeconds; FrameSequence = frame.Sequence; Events = Array.AsReadOnly(events); }
    }
    public sealed class TrackState
    {
        public int TrackIndex { get; }
        public string? AnimationId { get; }
        public float PlaybackTime { get; }
        public float AnimationTime { get; }
        public float AnimationRangeStart { get; }
        public float AnimationRangeEnd { get; }
        public float StartDelaySeconds { get; }
        public float? PlaybackEnd { get; }
        public float BlendWeight { get; }
        public float PlaybackRate { get; }
        public bool Looping { get; }
        public TrackBlend Blend { get; }
        public float BlendElapsed { get; }
        public float BlendDuration { get; }
        public float BlendProgress { get; }
        public float EventBlendThreshold { get; }
        public float AttachmentBlendThreshold { get; }
        public float DrawOrderBlendThreshold { get; }
        public bool HoldPreviousPose { get; }
        public int QueuedEntryCount { get; }
        internal TrackState(int index, Animation.AnimationPlayback e, int count)
        {
            TrackIndex = index; AnimationId = e.Clip?.Id; PlaybackTime = e.Time; AnimationTime = e.PoseTime;
            AnimationRangeStart = e.Start; AnimationRangeEnd = e.End; StartDelaySeconds = e.StartDelay; PlaybackEnd = e.PlaybackEnd; BlendWeight = e.BlendWeight; PlaybackRate = e.Rate;
            Looping = e.Loop; Blend = e.Additive ? TrackBlend.Additive : TrackBlend.Replace; BlendElapsed = e.BlendElapsed;
            BlendDuration = e.BlendDuration; BlendProgress = e.Progress; EventBlendThreshold = e.EventBlendThreshold;
            AttachmentBlendThreshold = e.AttachmentBlendThreshold; DrawOrderBlendThreshold = e.DrawOrderBlendThreshold;
            HoldPreviousPose = e.Hold; QueuedEntryCount = count;
        }
    }
    public sealed class QueuedEntry
    {
        public int TrackIndex { get; }
        public int QueueIndex { get; }
        public string? AnimationId { get; }
        public float StartDelaySeconds { get; }
        public float BlendDuration { get; }
        public bool Looping { get; }
        internal QueuedEntry(int track, int i, Animation.AnimationPlayback e)
        { TrackIndex = track; QueueIndex = i; AnimationId = e.Clip?.Id; StartDelaySeconds = e.StartDelay; BlendDuration = e.BlendDuration; Looping = e.Loop; }
    }
}

namespace Cane.Animation
{
    internal sealed class AnimationPlayback
    {
        internal Clip? Clip;
        internal float Time, Start, End, StartDelay, BlendElapsed, BlendDuration, BlendWeight = 1, Rate = 1;
        internal float EventBlendThreshold, AttachmentBlendThreshold, DrawOrderBlendThreshold;
        internal float? PlaybackEnd;
        internal bool Loop, Additive, Hold, CursorInitialized;
        internal AnimationPlayback? From;
        internal long EventOrder;
        internal float Duration => Math.Max(0, End - Start);
        internal float Progress => From == null || BlendDuration == 0 ? 1 : Numeric.Clamp(BlendElapsed / BlendDuration, 0, 1);
        internal float PoseTime
        {
            get { float d = Duration; if (d == 0) return Start; float t = Loop ? Time % d : Numeric.Clamp(Time, 0, d); if (t < 0) t += d; return Start + t; }
        }
        internal AnimationPlayback(Clip? clip, bool loop, float mix)
        { Clip = clip; Loop = loop; End = clip?.Duration ?? 0; BlendDuration = mix; }
        internal AnimationPlayback Clone()
        {
            var result=(AnimationPlayback)MemberwiseClone();var output=result;
            for(var input=From;input!=null;input=input.From){output.From=(AnimationPlayback)input.MemberwiseClone();output=output.From;}
            output.From=null;return result;
        }
    }
    internal sealed class Track
    {
        internal AnimationPlayback Current;
        internal readonly List<AnimationPlayback> Queue = new List<AnimationPlayback>();
        internal Track(AnimationPlayback entry) { Current = entry; }
        internal Track Clone() { var copy = new Track(Current.Clone()); foreach (AnimationPlayback e in Queue) copy.Queue.Add(e.Clone()); return copy; }
    }
    internal sealed class AnimationPlayer
    {
        internal readonly SortedDictionary<int, Track> Tracks = new SortedDictionary<int, Track>();
        internal readonly Dictionary<(string, string), float> Mixes = new Dictionary<(string, string), float>();
        internal float DefaultMix, DetachedTime;
        internal int AllocatedTracks;
        internal long NextEventOrder;
        internal Constraints.PhysicsStore Physics = new Constraints.PhysicsStore();
        internal AnimationPlayer Clone()
        {
            var copy = new AnimationPlayer { DefaultMix = DefaultMix, DetachedTime = DetachedTime, AllocatedTracks = AllocatedTracks, NextEventOrder = NextEventOrder, Physics = Physics.Clone() };
            foreach (var p in Tracks) copy.Tracks.Add(p.Key, p.Value.Clone());
            foreach (var p in Mixes) copy.Mixes.Add(p.Key, p.Value); return copy;
        }
        internal float Time => Tracks.TryGetValue(0, out Track? t) ? t.Current.PoseTime : DetachedTime;
        internal float Mix(AnimationPlayback? from, Clip? to) => from?.Clip == null || to == null ? DefaultMix : Mixes.TryGetValue((from.Clip.Id, to.Id), out float m) ? m : DefaultMix;
        internal void Replace(int index, Clip? clip, bool loop, float? mix, EventSink events)
        {
            AllocatedTracks = Math.Max(AllocatedTracks, index + 1);
            Tracks.TryGetValue(index, out Track? old); float duration = old == null ? 0 : mix ?? Mix(old.Current, clip);
            var entry = new AnimationPlayback(clip, loop, duration) { EventOrder = NextEventOrder++ };
            if (old != null)
            {
                events.Lifecycle("playbackInterrupted", index, old.Current, 0);
                if (duration == 0) EndChain(old.Current, index, 0, events); else entry.From = old.Current;
                foreach (var queued in old.Queue) events.Lifecycle("playbackRemoved", index, queued, 0);
            }
            Tracks[index] = new Track(entry); events.Lifecycle("playbackStarted", index, entry, 0);
        }
        internal void Enqueue(int index, Clip? clip, bool loop, float delay, float? mix, EventSink events)
        {
            if (!Tracks.TryGetValue(index, out Track? track)) { Replace(index, clip, loop, 0, events); return; }
            AnimationPlayback previous = track.Queue.Count == 0 ? track.Current : track.Queue[track.Queue.Count - 1];
            float duration = mix ?? Mix(previous, clip);
            track.Queue.Add(new AnimationPlayback(clip, loop, duration) { EventOrder = NextEventOrder++, StartDelay = delay > 0 ? delay : Math.Max(0, previous.Duration - duration + delay) });
        }
        internal void Clear(int index, EventSink events, double offset = 0)
        {
            if (!Tracks.TryGetValue(index, out Track? t)) return;
            EndChain(t.Current, index, offset, events); foreach (var e in t.Queue) events.Lifecycle("playbackRemoved", index, e, offset); Tracks.Remove(index);
        }
        internal static void EndChain(AnimationPlayback? e, int track, double offset, EventSink events)
        { long order = e == null ? 0 : e.EventOrder * 2 + 1; while (e != null) { events.Lifecycle("playbackEnded", track, e, offset, order); events.Lifecycle("playbackRemoved", track, e, offset, order); e = e.From; } }
        internal void Apply(Pose pose, SamplingOptions sampling) => EndpointPose.ApplyTracks(pose, Tracks.Values, sampling);
        internal void AdvanceTime(float delta, EventSink events)
        {
            DetachedTime += delta; if (!Numeric.Finite(DetachedTime)) throw new RuntimeException(RuntimeErrorCode.NonFinite, events.Operation, "AnimationPlayer clock overflowed.", "deltaSeconds");
            var indices = new List<int>(Tracks.Keys);
            foreach (int index in indices)
            {
                double offset = 0, remaining = delta;
                while (Tracks.TryGetValue(index, out Track? track))
                {
                    AnimationPlayback e = track.Current; double span = remaining;
                    bool promote = track.Queue.Count != 0 && e.Rate > 0;
                    double queueAt = promote ? Math.Max(0, ((double)track.Queue[0].StartDelay - e.Time) / e.Rate) : double.PositiveInfinity;
                    double endAt = e.PlaybackEnd.HasValue && e.Rate > 0 ? Math.Max(0, ((double)e.PlaybackEnd.Value - e.Time) / e.Rate) : double.PositiveInfinity;
                    double mixAt = NextMix(e);
                    span = Math.Min(span, Math.Min(queueAt, Math.Min(endAt, mixAt)));
                    AdvanceChain(e, null, index, offset, span, events);
                    offset += span; remaining = Math.Max(0, remaining - span);
                    bool boundary = false;
                    if (queueAt <= span && queueAt <= endAt)
                    {
                        AnimationPlayback next = track.Queue[0]; track.Queue.RemoveAt(0);
                        events.Lifecycle("playbackInterrupted", index, e, offset);
                        if (next.BlendDuration == 0) EndChain(e, index, offset, events); else next.From = e;
                        track.Current = next; events.Lifecycle("playbackStarted", index, next, offset); boundary = true;
                    }
                    else if (endAt <= span) { Clear(index, events, offset); break; }
                    else boundary = CompleteMixes(e, index, offset, events);
                    if (remaining == 0 && !boundary) break;
                    if (span == 0 && !boundary) break;
                }
            }
        }
        private static double NextMix(AnimationPlayback e)
        {
            double at = double.PositiveInfinity;
            for (AnimationPlayback? entry = e; entry?.From != null; entry = entry.From) at = Math.Min(at, Math.Max(0, (double)entry.BlendDuration - entry.BlendElapsed));
            return at;
        }
        private static bool CompleteMixes(AnimationPlayback e, int index, double offset, EventSink events)
        {
            for(AnimationPlayback? entry=e;entry?.From!=null;entry=entry.From)
                if(entry.BlendDuration==0 || entry.BlendElapsed>=entry.BlendDuration){EndChain(entry.From,index,offset,events);entry.From=null;return true;}
            return false;
        }
        private static void AdvanceChain(AnimationPlayback e, AnimationPlayback? incoming, int index, double offset, double delta, EventSink events)
        {
            var path=new List<AnimationPlayback>();for(AnimationPlayback? entry=e;entry!=null;entry=entry.From)path.Add(entry);
            for(int i=path.Count;i-->0;){e=path[i];var controller=i==0?incoming:path[i-1];
            float previous = e.Time, next = (float)((double)previous + (float)delta * e.Rate);
            if (!e.Loop) next = Math.Max(0, next);
            if (!Numeric.Finite(next)) throw new RuntimeException(RuntimeErrorCode.NonFinite, events.Operation, "Track clock overflowed.", "timeSeconds");
            EventCrossings.Collect(e, controller, index, previous, next, offset, delta, events);
            e.Time = next; e.BlendElapsed += (float)delta;
            if (!Numeric.Finite(e.BlendElapsed)) throw new RuntimeException(RuntimeErrorCode.NonFinite, events.Operation, "Blend clock overflowed.", "blendElapsed");
            }
        }
    }
}
