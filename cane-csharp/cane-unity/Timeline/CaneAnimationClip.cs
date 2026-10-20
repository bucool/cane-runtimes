using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Cane.Unity
{
    public sealed class CaneAnimationClip : PlayableAsset, ITimelineClipAsset
    {
        public string Animation = "";
        public bool Loop = true, Reverse;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.ClipIn | ClipCaps.SpeedMultiplier | ClipCaps.Extrapolation | (Loop ? ClipCaps.Looping : ClipCaps.None);
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CaneAnimationInput>.Create(graph);
            var input = playable.GetBehaviour(); input.Animation = Animation; input.Loop = Loop; input.Reverse = Reverse;
            return playable;
        }
    }
    internal sealed class CaneAnimationInput : PlayableBehaviour
    {
        internal string Animation;
        internal bool Loop, Reverse;
    }
}
