using System;
using Cane.Animation;

namespace Cane
{
    public sealed partial class RuntimePlayer
    {
        private RuntimeFrame ConfigureCurrent(string operation, int track, Action<AnimationPlayback> change)
        {
            RequireIdle(operation); long identity = Current(playback, track, operation).EventOrder;
            return Configure(operation, (s, _) => change(Current(s, track, operation)),
                configureBaseline: s => change(BaselineEntry(s, track, identity, operation)));
        }
        private static AnimationPlayback BaselineEntry(AnimationPlayer state, int track, long identity, string operation)
        {
            if (state.Tracks.TryGetValue(track, out Track? t))
            {
                for (AnimationPlayback? e = t.Current; e != null; e = e.From) if (e.EventOrder == identity) return e;
                foreach (AnimationPlayback e in t.Queue) if (e.EventOrder == identity) return e;
            }
            throw new RuntimeException(RuntimeErrorCode.InvalidState, operation, "The playback entry is missing from its configuration baseline.", "trackIndex");
        }
        private static AnimationPlayback Queued(AnimationPlayer state, int track, int queueIndex, string operation)
        {
            if (queueIndex < 0) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, operation, "Queue index must be non-negative.", "queueIndex");
            if (!state.Tracks.TryGetValue(track, out Track? t) || queueIndex >= t.Queue.Count)
                throw new RuntimeException(RuntimeErrorCode.NotFound, operation, "Queued entry is missing.", "queueIndex");
            return t.Queue[queueIndex];
        }
        public RuntimeFrame SetAnimationTime(float timeSeconds)
        {
            const string op = "setAnimationTime"; Nonnegative(timeSeconds, op, "timeSeconds"); RequireIdle(op);
            long? identity = playback.Tracks.TryGetValue(0, out Track? t) ? t.Current.EventOrder : (long?)null;
            void Set(AnimationPlayer state, bool origin)
            {
                state.DetachedTime = timeSeconds;
                if (!identity.HasValue) return;
                AnimationPlayback e = origin ? BaselineEntry(state, 0, identity.Value, op) : Current(state, 0, op);
                e.Time = Numeric.Clamp(timeSeconds, e.Start, e.End) - e.Start; e.CursorInitialized = true;
            }
            return Configure(op, (s, _) => Set(s, false), configureBaseline: s => Set(s, true));
        }
        public RuntimeFrame SetQueuedEntryOptions(int trackIndex, int queueIndex, QueuedEntryOptions options)
        {
            const string op = "setQueuedEntryOptions"; Index(trackIndex, op); RequireIdle(op);
            if (options == null) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, op, "Queued entry options are required.", "options");
            float? delay = options.StartDelaySeconds, mix = options.BlendDurationSeconds; bool? loop = options.Looping;
            if (!delay.HasValue && !mix.HasValue && !loop.HasValue) throw new RuntimeException(RuntimeErrorCode.InvalidArgument, op, "At least one queued entry option is required.", "options");
            if (delay.HasValue) Nonnegative(delay.Value, op, "startDelaySeconds"); if (mix.HasValue) Nonnegative(mix.Value, op, "blendDurationSeconds");
            long identity = Queued(playback, trackIndex, queueIndex, op).EventOrder;
            void Edit(AnimationPlayback entry)
            { if (delay.HasValue) entry.StartDelay = delay.Value; if (mix.HasValue) entry.BlendDuration = mix.Value; if (loop.HasValue) entry.Loop = loop.Value; }
            return Configure(op, (s, _) => Edit(Queued(s, trackIndex, queueIndex, op)),
                configureBaseline: s => Edit(BaselineEntry(s, trackIndex, identity, op)));
        }
        public RuntimeFrame RemoveQueuedEntry(int trackIndex, int queueIndex)
        {
            const string op = "removeQueuedEntry"; Index(trackIndex, op); RequireIdle(op);
            long identity = Queued(playback, trackIndex, queueIndex, op).EventOrder;
            return Configure(op, (s, events) => {
                AnimationPlayback removed = Queued(s, trackIndex, queueIndex, op); s.Tracks[trackIndex].Queue.RemoveAt(queueIndex);
                events.Lifecycle("playbackRemoved", trackIndex, removed, 0);
            }, configureBaseline: s => {
                AnimationPlayback removed = BaselineEntry(s, trackIndex, identity, op);
                if (!s.Tracks[trackIndex].Queue.Remove(removed)) throw new RuntimeException(RuntimeErrorCode.InvalidState, op, "The baseline entry is no longer queued.", "queueIndex");
            });
        }
        public RuntimeStep AdvancePhysics(float deltaSeconds)
        {
            const string op = "advancePhysics"; Nonnegative(deltaSeconds, op, "deltaSeconds"); ulong sequence = NextSequence(op);
            try
            {
                AnimationPlayer state = playback.Clone();
                RuntimeFrame frame = Evaluate(state, host, sequence, default, out Pose pose, operation: op, physicsDeltaOverride: deltaSeconds);
                playback = state; Frame = frame; publishedPose = pose;
                return new RuntimeStep(deltaSeconds > 0, frame, Array.Empty<RuntimeEvent>());
            }
            catch (RuntimeException e) { throw Reframe(e, op); }
        }
    }
}
