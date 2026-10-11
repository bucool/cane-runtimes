using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    // A pass-owned working surface is essential: encoding only the source shader
    // output would still blend it against Unity's linear destination.
    internal sealed class CaneColorCompositor : IDisposable
    {
        private static int nextId;
        internal static readonly int GammaId = Shader.PropertyToID("_CaneGamma");
        private static readonly int CoverageId = Shader.PropertyToID("_CaneTransferCoverage");
        private static readonly int SourceId = Shader.PropertyToID("_CaneTransferSource"), ArraySourceId = Shader.PropertyToID("_CaneTransferArray"),
            IsArrayId = Shader.PropertyToID("_CaneTransferIsArray"), SamplesId = Shader.PropertyToID("_CaneTransferSamples"),
            MultisampleId = Shader.PropertyToID("_CaneTransferMultisample"), MultisampleArrayId = Shader.PropertyToID("_CaneTransferMultisampleArray");
        private readonly int workingId, hostId;
        private Material conversion;
        internal CaneColorCompositor()
        {
            int id = Interlocked.Increment(ref nextId);
            workingId = Shader.PropertyToID("_CaneGammaTarget" + id);
            hostId = Shader.PropertyToID("_CaneHostTarget" + id);
        }
        internal Pass Begin(CommandBuffer commands, RenderTargetIdentifier color, RenderTargetIdentifier depth,
            RenderTextureDescriptor descriptor, Rect viewport, bool transparent)
        {
            if ((descriptor.dimension != TextureDimension.Tex2D && descriptor.dimension != TextureDimension.Tex2DArray) || descriptor.volumeDepth > 1)
                throw UnityObjects.Error("blendColorSpace", "Gamma pass conversion currently supports a single 2D camera view.");
            if (!conversion)
            {
                var shader = Resources.Load<Shader>("CaneColorTransfer");
                if (!shader || !shader.isSupported) throw UnityObjects.Error("blendColorSpace", "Cane color transfer shader is unavailable.");
                conversion = UnityObjects.Own(new Material(shader) { hideFlags = HideFlags.HideAndDontSave });
            }
            descriptor.colorFormat = RenderTextureFormat.ARGBFloat;
            descriptor.depthBufferBits = 0; descriptor.sRGB = false;
            descriptor.useMipMap = false; descriptor.autoGenerateMips = false;
            commands.GetTemporaryRT(workingId, descriptor, FilterMode.Point);
            commands.GetTemporaryRT(hostId, descriptor, FilterMode.Point);
            commands.SetGlobalFloat(CoverageId, transparent ? 1 : 0);
            bool array = descriptor.dimension == TextureDimension.Tex2DArray;
            int samples = descriptor.bindMS ? descriptor.msaaSamples : 1;
            Transfer(commands, color, workingId, conversion, 0, descriptor.width, descriptor.height, array, samples);
            commands.SetRenderTarget(workingId, depth); commands.SetViewport(viewport);
            commands.SetGlobalFloat(GammaId, 1);
            return new Pass(commands, color, depth, viewport, workingId, hostId, conversion, descriptor.width, descriptor.height, array, samples);
        }
        private static void Transfer(CommandBuffer commands, RenderTargetIdentifier source, RenderTargetIdentifier destination,
            Material material, int pass, int width, int height, bool array, int samples)
        {
            // HDRP uses single-slice texture arrays even for ordinary cameras.
            // Native Blit only binds its 2D source reliably; bind and fetch the
            // actual resource type explicitly, without filtering or UV flipping.
            commands.SetGlobalTexture(samples > 1 ? (array ? MultisampleArrayId : MultisampleId) : (array ? ArraySourceId : SourceId), source);
            commands.SetGlobalFloat(IsArrayId, array ? 1 : 0);
            commands.SetGlobalInt(SamplesId, samples);
            commands.SetRenderTarget(destination, 0, CubemapFace.Unknown, 0);
            commands.SetViewport(new Rect(0, 0, width, height));
            commands.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
        }
        internal readonly struct Pass : ICaneDrawCommands, IDisposable
        {
            private readonly CommandBuffer commands;
            private readonly RenderTargetIdentifier color, depth;
            private readonly Rect viewport;
            private readonly int working, host;
            private readonly Material transfer;
            private readonly int width, height, samples;
            private readonly bool array;
            internal Pass(CommandBuffer commands, RenderTargetIdentifier color, RenderTargetIdentifier depth,
                Rect viewport, int working, int host, Material transfer, int width, int height, bool array, int samples)
            { this.commands = commands; this.color = color; this.depth = depth; this.viewport = viewport; this.working = working; this.host = host; this.transfer = transfer; this.width = width; this.height = height; this.array = array; this.samples = samples; }
            public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submesh, int pass, MaterialPropertyBlock properties)
                => commands.DrawMesh(mesh, matrix, material, submesh, pass, properties);
            public void DrawRenderer(Renderer renderer, Material material, int submesh, int pass)
            {
                // Slot content belongs to Unity and retains Unity's normal linear
                // material behavior. Restore the complete destination around it.
                Transfer(commands, working, host, transfer, 1, width, height, array, samples);
                commands.SetRenderTarget(host, depth); commands.SetViewport(viewport);
                commands.SetGlobalFloat(GammaId, 0);
                commands.DrawRenderer(renderer, material, submesh, pass);
                Transfer(commands, host, working, transfer, 0, width, height, array, samples);
                commands.SetRenderTarget(working, depth); commands.SetViewport(viewport);
                commands.SetGlobalFloat(GammaId, 1);
            }
            public void Dispose()
            {
                Transfer(commands, working, color, transfer, 1, width, height, array, samples);
                commands.SetRenderTarget(color, depth); commands.SetViewport(viewport);
                commands.SetGlobalFloat(GammaId, 0);
                commands.ReleaseTemporaryRT(host); commands.ReleaseTemporaryRT(working);
            }
        }
        public void Dispose() { UnityObjects.Release(conversion); conversion = null; }
    }
}
