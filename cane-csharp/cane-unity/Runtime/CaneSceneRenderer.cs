using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Shared camera filtering and stable skeleton ordering for all render pipelines.</summary>
    public sealed class CaneSceneRenderer : IDisposable
    {
        private readonly List<CaneSkeleton> skeletons = new List<CaneSkeleton>();
        private static readonly Comparison<CaneSkeleton> comparison = Compare;
        private bool recording;
        private readonly CaneColorCompositor compositor = new CaneColorCompositor();
        public CaneBlendColorSpace BlendColorSpace { get; set; } = CaneBlendColorSpace.Gamma;
        public int LastSkeletonCount { get; private set; }
        public ulong CameraRecords { get; private set; }

        public void Record(CommandBuffer commands, Camera camera)
        {
            if (!camera) throw new ArgumentNullException(nameof(camera));
            var target = camera.targetTexture;
            var descriptor = target ? target.descriptor : new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.ARGBFloat, 0)
                { msaaSamples = camera.allowMSAA ? Math.Max(1, QualitySettings.antiAliasing) : 1 };
            Record(commands, camera, BuiltinRenderTextureType.CameraTarget, BuiltinRenderTextureType.CameraTarget,
                descriptor, camera.pixelRect, camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor.a < 1);
        }

        /// <summary>Record into an explicit linear host target. Gamma uses a separate floating-point work surface.</summary>
        public void Record(CommandBuffer commands, Camera camera, RenderTargetIdentifier color, RenderTargetIdentifier depth,
            RenderTextureDescriptor descriptor, Rect viewport, bool transparentTarget = false)
        {
            if (BlendColorSpace == CaneBlendColorSpace.Linear)
            {
                commands.SetGlobalFloat(CaneColorCompositor.GammaId, 0);
                RecordPackets(new CaneDrawCommands(commands), camera); return;
            }
            if (BlendColorSpace != CaneBlendColorSpace.Gamma) throw new ArgumentOutOfRangeException(nameof(BlendColorSpace));
            using (var pass = compositor.Begin(commands, color, depth, descriptor, viewport, transparentTarget)) RecordPackets(pass, camera);
        }

        /// <summary>Draw-only integration. Requires Linear; Gamma needs the target-aware native-buffer overload.</summary>
        public void Record<TCommands>(TCommands commands, Camera camera) where TCommands : struct, ICaneDrawCommands
        {
            if (BlendColorSpace != CaneBlendColorSpace.Linear)
                throw UnityObjects.Error("blendColorSpace", "Gamma requires the target-aware CommandBuffer recorder; draw-only command adapters must select Linear.");
            RecordPackets(commands, camera);
        }

        private void RecordPackets<TCommands>(TCommands commands, Camera camera) where TCommands : struct, ICaneDrawCommands
        {
            if (!camera) throw new ArgumentNullException(nameof(camera));
            if (recording) throw UnityObjects.Error("recordCamera", "A camera recorder cannot be reentered.");
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                throw UnityObjects.Error("recordCamera", "Set Player Settings / Color Space to Linear for the Cane color contract.");
            recording = true; LastSkeletonCount = 0;
            try
            {
                foreach (CaneSkeleton skeleton in CaneSkeleton.Active)
                    if (skeleton && skeleton.isActiveAndEnabled && skeleton.Frame != null &&
                        Application.IsPlaying(skeleton.gameObject) == (Application.IsPlaying(camera.gameObject) ||
                            (Application.isPlaying && camera.cameraType == CameraType.SceneView)) &&
                        (camera.cullingMask & (1 << skeleton.gameObject.layer)) != 0) skeletons.Add(skeleton);
                skeletons.Sort(comparison);
                foreach (CaneSkeleton skeleton in skeletons)
                    if (skeleton && skeleton.isActiveAndEnabled)
                    { skeleton.Record(commands, camera.cullingMask); ++LastSkeletonCount; }
                ++CameraRecords;
            }
            finally { skeletons.Clear(); recording = false; }
        }

        private static int Compare(CaneSkeleton a, CaneSkeleton b)
        {
            int layer = SortingLayer.GetLayerValueFromID(a.SortingLayerId).CompareTo(SortingLayer.GetLayerValueFromID(b.SortingLayerId));
            if (layer != 0) return layer;
            int order = a.SortingOrder.CompareTo(b.SortingOrder);
            if (order != 0) return order;
#if UNITY_6000_4_OR_NEWER
            return a.GetEntityId().CompareTo(b.GetEntityId());
#else
            return a.GetInstanceID().CompareTo(b.GetInstanceID());
#endif
        }
        public void Dispose() { compositor.Dispose(); }
    }
}
