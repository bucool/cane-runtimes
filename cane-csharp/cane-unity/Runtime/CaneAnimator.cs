#if CANE_ANIMATION
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cane.Unity
{
    [Serializable]
    public sealed class CaneAnimatorBinding
    {
        public AnimationClip Clip;
        public string Animation = "";
        [Tooltip("Optional full controller state path, e.g. Base Layer.Run.")]
        public string State = "";
        public bool Reverse;
        public float CycleOffset;
        public string CycleOffsetParameter = "", TimeParameter = "";
    }

    [DisallowMultipleComponent, RequireComponent(typeof(CanePlayer), typeof(Animator)), DefaultExecutionOrder(-100)]
    public sealed class CaneAnimator : MonoBehaviour
    {
        public int FirstTrack;
        public CaneAnimatorBinding[] Bindings = Array.Empty<CaneAnimatorBinding>();
        public bool[] AdditiveLayers = Array.Empty<bool>();
        // Generated controller clips animate this value to retain their native duration.
        [HideInInspector] public float Clock;
        private CanePlayer player;
        private Animator animator;
        private CaneAnimationClock clock;
        private RuntimeAnimatorController controller;
        private AnimationState state;
        private int firstTrack;
        private Layer[] layers = Array.Empty<Layer>();
        private readonly Dictionary<(int State, AnimationClip Clip), Binding> mappings = new Dictionary<(int, AnimationClip), Binding>();
        private readonly Dictionary<int, List<AnimationClip>> stateClips = new Dictionary<int, List<AnimationClip>>();
        private struct Binding
        {
            public Cane.Animation Animation;
            public bool Reverse;
            public float Offset;
            public int OffsetParameter, TimeParameter;
        }
        private struct Sample
        {
            public AnimationClip Clip;
            public Cane.Animation Animation;
            public int State;
            public double Time, Rate, Weight;
            public bool Loop, Reverse;
        }
        private sealed class Layer
        {
            public readonly List<AnimatorClipInfo> Current = new List<AnimatorClipInfo>(), Next = new List<AnimatorClipInfo>();
            public readonly List<Sample> Samples = new List<Sample>(), Configured = new List<Sample>();
            public TrackEntry[] Entries = Array.Empty<TrackEntry>();
        }
        private void OnEnable()
        {
            player = GetComponent<CanePlayer>(); animator = GetComponent<Animator>(); clock = CaneAnimationClock.Acquire(player);
            player.Rebuilt += Rebuilt; Rebind();
        }
        private void Rebuilt(CanePlayer _) => Rebind();
        public void Rebind()
        {
            if (player == null || animator == null) return;
            Clear(); mappings.Clear(); stateClips.Clear(); firstTrack = FirstTrack; state = player.State; controller = animator.runtimeAnimatorController;
            if (state == null || controller == null) return;
            foreach (var clip in controller.animationClips)
            {
                Map(clip, clip.name, 0, default);
            }
            foreach (var binding in Bindings) if (binding.Clip != null)
                Map(binding.Clip, binding.Animation.Length == 0 ? binding.Clip.name : binding.Animation,
                    binding.State.Length == 0 ? 0 : Animator.StringToHash(binding.State),
                    new Binding { Reverse = binding.Reverse, Offset = binding.CycleOffset, OffsetParameter = Hash(binding.CycleOffsetParameter), TimeParameter = Hash(binding.TimeParameter) });
            layers = new Layer[animator.layerCount]; for (int i = 0; i < layers.Length; i++) layers[i] = new Layer();
        }
        private static int Hash(string name) => name.Length == 0 ? 0 : Animator.StringToHash(name);
        private void Map(AnimationClip clip, string name, int hash, Binding binding)
        {
            int index = Array.IndexOf(player.Resources.Asset.AnimationIds, name);
            if (index < 0) index = Array.FindIndex(player.Resources.Asset.Animations, value => value.Name == name);
            if (index < 0) return;
            binding.Animation = player.Resources.Asset.Animations[index]; mappings[(hash, clip)] = binding;
            if (hash == 0) return;
            if (!stateClips.TryGetValue(hash, out var clips)) stateClips[hash] = clips = new List<AnimationClip>();
            if (!clips.Contains(clip)) clips.Add(clip);
        }
        private void Clear()
        {
            if (state != null && player != null && player.State == state) for (int i = 0; i < layers.Length; i++)
            {
                var entries = layers[i].Entries;
                if (entries.Length > 0 && state.Current(firstTrack + i) == entries[entries.Length - 1]) state.Clear(firstTrack + i);
            }
            layers = Array.Empty<Layer>();
        }
        private void OnDisable()
        {
            if (player != null) player.Rebuilt -= Rebuilt;
            Clear(); if (clock != null) clock.Release(); clock = null;
        }
        private void LateUpdate()
        {
            if (!animator.isActiveAndEnabled) return;
            clock.Unscaled = animator.updateMode == AnimatorUpdateMode.UnscaledTime;
            Synchronize(clock.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime);
        }
        /// <summary>Reads native state only. The shared clock evaluates Core afterward.
        /// Pass emitEvents=false when scrubbing or moving the controller discontinuously.</summary>
        public void Synchronize(double delta = 0, bool emitEvents = true)
        {
            if (state != player.State || controller != animator.runtimeAnimatorController || layers.Length != animator.layerCount || firstTrack != FirstTrack) Rebind();
            if (state == null || controller == null) return;
            for (int l = 0; l < layers.Length; l++)
            {
                var layer = layers[l]; layer.Samples.Clear();
                animator.GetCurrentAnimatorClipInfo(l, layer.Current); animator.GetNextAnimatorClipInfo(l, layer.Next);
                double weight = l == 0 ? 1 : animator.GetLayerWeight(l);
                Add(layer.Samples, layer.Current, animator.GetCurrentAnimatorStateInfo(l), weight);
                Add(layer.Samples, layer.Next, animator.GetNextAnimatorStateInfo(l), weight);
                bool changed = layer.Samples.Count != layer.Configured.Count;
                for (int i = 0; !changed && i < layer.Samples.Count; i++) changed = !Same(layer.Samples[i], layer.Configured[i]);
                if (changed)
                {
                    var animations = new Cane.Animation[layer.Samples.Count]; for (int i = 0; i < animations.Length; i++) animations[i] = layer.Samples[i].Animation;
                    var old = layer.Entries; layer.Entries = state.SetBlend(FirstTrack + l, animations);
                    var used = new bool[old.Length];
                    for (int i = 0; i < layer.Entries.Length; i++)
                    {
                        var sample = layer.Samples[i]; double previous = Math.Max(0, sample.Time - delta * sample.Rate);
                        int match = -1; double distance = double.PositiveInfinity;
                        for (int p = 0; p < layer.Configured.Count; p++) if (!used[p] && Same(sample, layer.Configured[p]) && Math.Abs(old[p].Time - sample.Time) < distance)
                        { match = p; distance = Math.Abs(old[p].Time - sample.Time); }
                        if (match >= 0) { previous = old[match].Time; used[match] = true; }
                        layer.Entries[i].Seek(previous == 0 ? -double.Epsilon : previous);
                    }
                    layer.Configured.Clear(); layer.Configured.AddRange(layer.Samples);
                }
                for (int i = 0; i < layer.Entries.Length; i++)
                {
                    var entry = layer.Entries[i]; var sample = layer.Samples[i];
                    if (!emitEvents || sample.Time < entry.Time || entry.Reverse != sample.Reverse) entry.Seek(sample.Time);
                    entry.Time = sample.Time; entry.Speed = 0; entry.Alpha = sample.Weight; entry.Loop = sample.Loop; entry.Reverse = sample.Reverse;
                    entry.Additive = l < AdditiveLayers.Length && AdditiveLayers[l];
                }
            }
        }
        private static bool Same(Sample a, Sample b) => a.Clip == b.Clip && a.State == b.State && a.Animation == b.Animation;
        private void Add(List<Sample> output, List<AnimatorClipInfo> clips, AnimatorStateInfo info, double layerWeight)
        {
            if (stateClips.TryGetValue(info.fullPathHash, out var prepared))
            {
                // Keep zero-weight inputs bound; crossing a blend-tree threshold must
                // not rebuild Core tracks or allocate during ordinary locomotion.
                foreach (var clip in prepared)
                {
                    double weight = 0; foreach (var input in clips) if (input.clip == clip) weight += input.weight;
                    SampleClip(output, clip, info, weight * layerWeight);
                }
            }
            else foreach (var input in clips) SampleClip(output, input.clip, info, input.weight * layerWeight);
        }
        private void SampleClip(List<Sample> output, AnimationClip clip, AnimatorStateInfo info, double weight)
        {
            if (!mappings.TryGetValue((info.fullPathHash, clip), out var binding) && !mappings.TryGetValue((0, clip), out binding)) return;
            double speed = animator.speed * info.speed * info.speedMultiplier;
            double phase = binding.TimeParameter == 0 ? info.normalizedTime : animator.GetFloat(binding.TimeParameter);
            double offset = binding.Offset + (binding.OffsetParameter == 0 ? 0 : animator.GetFloat(binding.OffsetParameter));
            double time = (phase + offset) * binding.Animation.Duration;
            bool reverse = (speed < 0) != binding.Reverse;
            output.Add(new Sample { Clip = clip, Animation = binding.Animation, State = info.fullPathHash, Time = time,
                Rate = info.length > 0 ? Math.Abs(animator.speed) * binding.Animation.Duration / info.length : 0, Weight = weight, Loop = clip.isLooping, Reverse = reverse });
        }
    }
}
#endif
