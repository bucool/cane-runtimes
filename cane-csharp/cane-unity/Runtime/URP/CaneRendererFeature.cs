using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Cane.Unity.URP
{
    /// <summary>Ordered final packets in URP's native raster render graph.</summary>
    [DisallowMultipleRendererFeature]
    public sealed class CaneRendererFeature : ScriptableRendererFeature
    {
        public RenderPassEvent InjectionPoint = RenderPassEvent.AfterRenderingTransparents;
        public bool RenderSceneView = true;
        public bool RenderPreview;
        public CaneBlendColorSpace BlendColorSpace = CaneBlendColorSpace.Gamma;
        private PacketPass pass;
        public ulong GraphPasses => pass?.GraphPasses ?? 0;
        public ulong CompatibilityPasses => pass?.CompatibilityPasses ?? 0;
        public int LastSkeletonCount => pass?.Recorder.LastSkeletonCount ?? 0;

        public override void Create() { pass?.Recorder.Dispose(); pass = new PacketPass(); }
        protected override void Dispose(bool disposing) { pass?.Recorder.Dispose(); pass = null; }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var type = data.cameraData.cameraType;
            if ((!RenderSceneView && type == CameraType.SceneView) || (!RenderPreview && type == CameraType.Preview)) return;
            if (pass == null) Create();
            pass.Recorder.BlendColorSpace = BlendColorSpace;
            pass.requiresIntermediateTexture = BlendColorSpace == CaneBlendColorSpace.Gamma;
            pass.renderPassEvent = InjectionPoint; renderer.EnqueuePass(pass);
        }

        private sealed class PacketPass : ScriptableRenderPass
        {
            internal readonly CaneSceneRenderer Recorder = new CaneSceneRenderer();
            internal ulong GraphPasses, CompatibilityPasses;
            private sealed class PassData
            {
                internal PacketPass Pass; internal Camera Camera;
                internal TextureHandle Color, Depth;
                internal RenderTextureDescriptor Descriptor;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (Recorder.BlendColorSpace == CaneBlendColorSpace.Gamma)
                {
                    using (var builder = graph.AddUnsafePass<PassData>("Cane Gamma final packets", out var data))
                    {
                        var cameraData = frameData.Get<UniversalCameraData>();
                        data.Pass = this; data.Camera = cameraData.camera;
                        data.Color = resources.activeColorTexture; data.Depth = resources.activeDepthTexture;
                        data.Descriptor = cameraData.cameraTargetDescriptor;
                        builder.UseTexture(data.Color, AccessFlags.ReadWrite);
                        if (data.Depth.IsValid()) builder.UseTexture(data.Depth, AccessFlags.Read);
                        builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static (PassData value, UnsafeGraphContext context) =>
                        {
                            var commands = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                            RenderTargetIdentifier depth = value.Depth.IsValid() ? value.Depth : BuiltinRenderTextureType.None;
                            value.Pass.Recorder.Record(commands, value.Camera, value.Color, depth, value.Descriptor,
                                new Rect(0, 0, value.Descriptor.width, value.Descriptor.height),
                                value.Camera.clearFlags == CameraClearFlags.SolidColor && value.Camera.backgroundColor.a < 1);
                            ++value.Pass.GraphPasses;
                        });
                    }
                    return;
                }
                using (var builder = graph.AddRasterRenderPass<PassData>("Cane final packets", out var data))
                {
                    data.Pass = this; data.Camera = frameData.Get<UniversalCameraData>().camera;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    if (resources.activeDepthTexture.IsValid()) builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData value, RasterGraphContext context) =>
                    {
                        value.Pass.Recorder.Record(new RasterDrawCommands(context.cmd), value.Camera);
                        ++value.Pass.GraphPasses;
                    });
                }
            }

#if !UNITY_6000_4_OR_NEWER
            [Obsolete("Used only by URP 17.3 compatibility mode.")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                CommandBuffer commands = CommandBufferPool.Get("Cane final packets");
                try
                {
                    var camera = data.cameraData.camera;
                    Recorder.Record(commands, camera, data.cameraData.renderer.cameraColorTargetHandle, data.cameraData.renderer.cameraDepthTargetHandle,
                        data.cameraData.cameraTargetDescriptor, camera.pixelRect,
                        camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor.a < 1);
                    context.ExecuteCommandBuffer(commands); ++CompatibilityPasses;
                }
                finally { CommandBufferPool.Release(commands); }
            }
#endif
        }

        private readonly struct RasterDrawCommands : ICaneDrawCommands
        {
            private readonly RasterCommandBuffer commands;
            internal RasterDrawCommands(RasterCommandBuffer commands) { this.commands = commands; }
            public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submesh, int pass, MaterialPropertyBlock properties)
                => commands.DrawMesh(mesh, matrix, material, submesh, pass, properties);
            public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass)
                => commands.DrawRenderer(renderer, material, submesh, pass);
        }
    }
}
