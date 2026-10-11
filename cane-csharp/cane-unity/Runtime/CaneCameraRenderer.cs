using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Built-in pipeline camera pass. SRP hosts use their Cane renderer feature or custom pass.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class CaneCameraRenderer : MonoBehaviour
    {
        public CaneBlendColorSpace BlendColorSpace = CaneBlendColorSpace.Gamma;
        private Camera target;
        private bool ownsIntermediate;
        private CommandBuffer commands;
        private readonly CaneSceneRenderer recorder = new CaneSceneRenderer();
        private const CameraEvent Pass = CameraEvent.AfterForwardAlpha;

        private void OnEnable()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
                throw UnityObjects.Error("cameraRenderer", "This camera component requires Unity's Built-in pipeline. An SRP must explicitly record Cane packets in its render pass.");
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                throw UnityObjects.Error("cameraRenderer", "Set Player Settings / Color Space to Linear for the Cane color contract.");
            target = GetComponent<Camera>();
            UpdateIntermediate();
            commands = new CommandBuffer { name = "Cane ordered final packets" };
            target.AddCommandBuffer(Pass, commands);
        }

        private void OnPreCull()
        {
            if (commands == null) return;
            UpdateIntermediate();
            commands.Clear(); recorder.BlendColorSpace = BlendColorSpace; recorder.Record(commands, target);
        }
        private void UpdateIntermediate()
        {
            // The screen backbuffer cannot be sampled on every graphics API.
            // Request a camera texture before rendering, then restore the host
            // setting when this component no longer needs its own intermediate.
            if (BlendColorSpace == CaneBlendColorSpace.Gamma && !target.forceIntoRenderTexture)
            { target.forceIntoRenderTexture = true; ownsIntermediate = true; }
            else if (BlendColorSpace != CaneBlendColorSpace.Gamma && ownsIntermediate)
            { target.forceIntoRenderTexture = false; ownsIntermediate = false; }
        }
        private void OnDisable()
        {
            if (target && commands != null) target.RemoveCommandBuffer(Pass, commands);
            if (target && ownsIntermediate) target.forceIntoRenderTexture = false;
            ownsIntermediate = false;
            commands?.Dispose(); commands = null;
            recorder.Dispose();
        }
    }
}
