#if CANE_ANIMATION
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Cane.Unity.Editor
{
    [CustomEditor(typeof(CaneAnimator))]
    public sealed class CaneAnimatorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector(); var bridge = (CaneAnimator)target; var player = bridge.GetComponent<CanePlayer>();
            if (player.Asset != null && GUILayout.Button("Create Animator Controller"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Create Cane controller", player.Asset.name, "controller", "Choose the controller asset location.");
                if (path.Length > 0)
                {
                    var animator = bridge.GetComponent<Animator>(); Undo.RecordObject(animator, "Assign Cane controller");
                    animator.runtimeAnimatorController = CreateController(player.Asset, path); ReadSettings(bridge);
                }
            }
            if (GUILayout.Button("Prepare controller bindings")) ReadSettings(bridge);
        }
        public static AnimatorController CreateController(CaneAsset asset, string path)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path); var data = asset.Data;
            for (int i = 0; i < data.Animations.Length; i++)
            {
                var animation = data.Animations[i]; var clip = new AnimationClip { name = data.AnimationIds[i] };
                clip.SetCurve("", typeof(CaneAnimator), "Clock", AnimationCurve.Linear(0, 0, (float)animation.Duration, 1));
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.AddObjectToAsset(clip, controller); var state = controller.layers[0].stateMachine.AddState(animation.Name); state.motion = clip;
                if (i == 0) controller.layers[0].stateMachine.defaultState = state;
            }
            AssetDatabase.SaveAssets(); return controller;
        }
        public static void ReadSettings(CaneAnimator bridge)
        {
            var runtime = bridge.GetComponent<Animator>().runtimeAnimatorController;
            var replacements = new Dictionary<AnimationClip, AnimationClip>();
            while (runtime is AnimatorOverrideController replacement)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); replacement.GetOverrides(pairs);
                foreach (var pair in pairs) if (pair.Value != null && !replacements.ContainsKey(pair.Key)) replacements[pair.Key] = pair.Value;
                runtime = replacement.runtimeAnimatorController;
            }
            if (!(runtime is AnimatorController controller)) return;
            Undo.RecordObject(bridge, "Read Cane Animator settings");
            var bindings = new List<CaneAnimatorBinding>(); var layers = controller.layers; bridge.AdditiveLayers = new bool[layers.Length];
            var used = new HashSet<Motion>(); var clonedFrom = new Dictionary<AnimationClip, AnimationClip>();
            for (int i = 0; i < layers.Length; i++)
            {
                bridge.AdditiveLayers[i] = layers[i].blendingMode == AnimatorLayerBlendingMode.Additive;
                States(layers[i].syncedLayerIndex < 0 ? layers[i].stateMachine : layers[layers[i].syncedLayerIndex].stateMachine, layers[i].name + ".");
            }
            bridge.Bindings = bindings.ToArray(); EditorUtility.SetDirty(bridge); bridge.Rebind();
            void States(AnimatorStateMachine machine, string path)
            {
                foreach (var child in machine.states)
                {
                    var state = child.state; used.Clear(); Motion(state.motion, state, path + state.name, state.cycleOffsetParameterActive ? 0 : state.cycleOffset, false);
                }
                foreach (var child in machine.stateMachines) States(child.stateMachine, path + child.stateMachine.name + ".");
            }
            Motion Motion(Motion motion, AnimatorState state, string path, float offset, bool reverse)
            {
                if (motion is BlendTree tree)
                {
                    if (!used.Add(tree))
                    {
                        tree = Instantiate(tree); Undo.RegisterCreatedObjectUndo(tree, "Prepare Cane blend tree");
                        AssetDatabase.AddObjectToAsset(tree, controller); used.Add(tree);
                    }
                    var children = tree.children; bool changed = false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var child = children[i]; var prepared = Motion(child.motion, state, path, offset + child.cycleOffset, reverse != (child.timeScale < 0));
                        changed |= prepared != child.motion; children[i].motion = prepared;
                    }
                    if (changed) { Undo.RecordObject(tree, "Prepare Cane blend tree"); tree.children = children; EditorUtility.SetDirty(tree); }
                    return tree;
                }
                if (!(motion is AnimationClip clip)) return motion;
                string animation = clip.name; var actual = replacements.TryGetValue(clip, out var replacement) ? replacement : clip;
                clonedFrom.TryGetValue(actual, out var original);
                foreach (var previous in bridge.Bindings) if ((previous.Clip == actual || original != null && previous.Clip == original) && (previous.State.Length == 0 || previous.State == path) && previous.Animation.Length > 0) animation = previous.Animation;
                if (!used.Add(actual))
                {
                    // Unity omits zero-weight inputs and exposes no child identity. A unique
                    // control clip keeps repeated motions distinguishable at every weight.
                    original = actual; actual = Instantiate(actual); actual.name = animation; clip = actual; clonedFrom[actual] = original;
                    Undo.RegisterCreatedObjectUndo(actual, "Prepare Cane control clip"); AssetDatabase.AddObjectToAsset(actual, controller); used.Add(actual);
                }
                bindings.Add(new CaneAnimatorBinding { Clip = actual, Animation = animation, State = path, Reverse = reverse,
                    CycleOffset = offset + AnimationUtility.GetAnimationClipSettings(actual).cycleOffset,
                    CycleOffsetParameter = state.cycleOffsetParameterActive ? state.cycleOffsetParameter : "", TimeParameter = state.timeParameterActive ? state.timeParameter : "" });
                return clip;
            }
        }
    }
}
#endif
