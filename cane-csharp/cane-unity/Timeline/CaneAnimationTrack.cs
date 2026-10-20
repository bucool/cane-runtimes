using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Cane.Unity
{
    [TrackBindingType(typeof(CanePlayer)), TrackClipType(typeof(CaneAnimationClip)), TrackColor(.12f, .58f, .73f)]
    public sealed class CaneAnimationTrack : TrackAsset
    {
        public int TrackIndex;
        public bool Additive;
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var playable = ScriptPlayable<CaneAnimationMixer>.Create(graph, inputCount);
            var mixer = playable.GetBehaviour(); mixer.TrackIndex = TrackIndex; mixer.Additive = Additive; mixer.Director = owner.GetComponent<PlayableDirector>();
            return playable;
        }
    }
    internal sealed class CaneAnimationMixer : PlayableBehaviour
    {
        internal int TrackIndex;
        internal bool Additive;
        internal PlayableDirector Director;
        private CanePlayer player;
        private AnimationState state;
        private CaneAnimationClock clock;
        private TrackEntry[] entries = Array.Empty<TrackEntry>();
        private int[] inputs = Array.Empty<int>();
        private bool initialized;
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as CanePlayer;
            if (player != target || state != target?.State)
            {
                Release(); player = target; state = target?.State;
                if (state == null) return;
                var animations = new List<Cane.Animation>(); var selected = new List<int>();
                for (int i = 0; i < playable.GetInputCount(); i++)
                {
                    var input = ((ScriptPlayable<CaneAnimationInput>)playable.GetInput(i)).GetBehaviour();
                    if (input.Animation.Length == 0) continue;
                    int index = Array.IndexOf(player.Resources.Asset.AnimationIds, input.Animation);
                    if (index < 0) index = Array.FindIndex(player.Resources.Asset.Animations, animation => animation.Name == input.Animation);
                    if (index < 0) throw new ArgumentException("Unknown Cane animation: " + input.Animation);
                    animations.Add(player.Resources.Asset.Animations[index]); selected.Add(i);
                }
                inputs = selected.ToArray(); entries = state.SetBlend(TrackIndex, animations.ToArray());
                if (Application.isPlaying) clock = CaneAnimationClock.Acquire(player);
                if (Director != null) Director.stopped += Stopped;
                initialized = false;
            }
            if (state == null) return;
            bool evaluate = info.evaluationType == FrameData.EvaluationType.Evaluate || info.seekOccurred;
            if (clock != null && Director != null) clock.Unscaled = Director.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime;
            for (int i = 0; i < entries.Length; i++)
            {
                var source = playable.GetInput(inputs[i]); var input = ((ScriptPlayable<CaneAnimationInput>)source).GetBehaviour();
                var entry = entries[i]; bool backwards = info.effectiveSpeed * source.GetSpeed() < 0;
                double time = backwards ? entry.Animation.Duration - source.GetTime() : source.GetTime();
                bool reverse = input.Reverse != backwards;
                if (evaluate || time < entry.Time || initialized && entry.Reverse != reverse) entry.Seek(time);
                else if (!initialized)
                {
                    double previous = Math.Max(0, time - info.deltaTime * Math.Abs(source.GetSpeed()));
                    entry.Seek(previous == 0 ? -double.Epsilon : previous);
                }
                entry.Time = time; entry.Speed = 0; entry.Loop = input.Loop; entry.Reverse = reverse; entry.Additive = Additive;
                entry.Alpha = playable.GetInputWeight(inputs[i]) * info.effectiveWeight;
            }
            initialized = true;
            // Explicit director evaluation and editor scrubbing must be visible immediately.
            // During playback the shared clock evaluates all graph inputs together.
            if (!Application.isPlaying || evaluate) player.Advance(0);
        }
        private void Release()
        {
            if (Director != null) Director.stopped -= Stopped;
            if (state != null && player != null && player.State == state && entries.Length > 0 && state.Current(TrackIndex) == entries[entries.Length - 1])
            { state.Clear(TrackIndex); player.Advance(0); }
            if (clock != null) clock.Release(); clock = null; player = null; state = null;
            entries = Array.Empty<TrackEntry>(); inputs = Array.Empty<int>();
        }
        private void Stopped(PlayableDirector _) => Release();
        public override void OnPlayableDestroy(Playable playable) => Release();
    }
}
