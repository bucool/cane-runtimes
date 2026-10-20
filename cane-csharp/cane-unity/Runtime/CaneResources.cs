using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Shared GPU resources. Input textures remain caller-owned unless requested.</summary>
    public sealed class CaneResources : IDisposable
    {
        public readonly RuntimeAsset Asset;
        public readonly Shader SceneShader;
        public Texture[] Textures { get; private set; }
        public event Action<int> TextureChanged;
        private Texture[] inputs;
        private bool[] owned;
        private TextureInfo[] info;
        private Material[] materials, templates;
        private const int MaterialsPerTexture = 20;
        private bool disposed;
        internal int AssetReferences;
        public CaneResources(RuntimeAsset asset, Texture[] textures, bool ownsTextures = false, Shader sceneShader = null)
        {
            if (textures.Length != asset.Textures.Length) throw new ArgumentException("Texture count does not match the Cane asset.", nameof(textures));
            Asset = asset; SceneShader = sceneShader; inputs = (Texture[])textures.Clone(); info = (TextureInfo[])asset.Textures.Clone(); owned = new bool[textures.Length];
            if (ownsTextures) Array.Fill(owned, true);
            Textures = new Texture[textures.Length]; materials = new Material[textures.Length * MaterialsPerTexture]; templates = new Material[textures.Length];
            try { for (int i = 0; i < textures.Length; i++) Textures[i] = Prepare(textures[i], asset.Textures[i]); }
            catch { Dispose(); throw; }
        }
        public Material Material(int texture, Cane.BlendMode blend, bool canvas = false, bool overlay = false, SpriteMaskInteraction mask = SpriteMaskInteraction.None)
        {
            int index = texture * MaterialsPerTexture + (canvas ? overlay ? 16 : 12 : (int)mask * 4) + (int)blend;
            if (materials[index] != null) return materials[index];
            var template = templates[texture];
            var shader = !canvas ? template != null ? template.shader : SceneShader : null;
            var material = new Material(shader != null ? shader : Resources.Load<Shader>("Cane/Skeleton")) { name = "Cane " + texture + " " + blend, hideFlags = HideFlags.DontSave };
            if (template != null) { material.CopyPropertiesFromMaterial(template); material.shaderKeywords = template.shaderKeywords; }
            material.mainTexture = Textures[texture];
            material.SetFloat("_Canvas", canvas ? 1 : 0);
            material.SetInt("_ZTest", (int)(canvas && overlay ? CompareFunction.Always : CompareFunction.LessEqual));
            if (!canvas && mask != SpriteMaskInteraction.None)
                material.SetInt("_StencilComp", (int)(mask == SpriteMaskInteraction.VisibleInsideMask ? CompareFunction.NotEqual : CompareFunction.Equal));
            material.SetFloat("_Pma", info[texture].AlphaMode == AlphaMode.Premultiplied ? 1 : 0);
            var sampling = info[texture];
            material.DisableKeyword("CANE_SAMPLE_CONTROL");
            if (sampling.MinFilter != sampling.MagFilter || sampling.MinFilter == TextureFilter.Nearest && sampling.MipmapFilter == TextureFilter.Linear)
            {
                material.EnableKeyword("CANE_SAMPLE_CONTROL");
                material.SetVector("_Sampling", new Vector4((int)sampling.MinFilter, (int)sampling.MagFilter, sampling.MipmapFilter.HasValue ? (int)sampling.MipmapFilter.Value : -1, Textures[texture].mipmapCount - 1));
            }
            material.SetInt("_SrcBlend", (int)(blend == Cane.BlendMode.Multiply ? UnityEngine.Rendering.BlendMode.DstColor : UnityEngine.Rendering.BlendMode.One));
            material.SetInt("_DstBlend", (int)(blend == Cane.BlendMode.Additive ? UnityEngine.Rendering.BlendMode.One : blend == Cane.BlendMode.Screen ? UnityEngine.Rendering.BlendMode.OneMinusSrcColor : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetInt("_DstBlendAlpha", (int)(blend == Cane.BlendMode.Additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            return materials[index] = material;
        }
        /// <summary>Snapshots scene shader and surface settings. Canvas retains its UI shader.
        /// Texture, alpha, sampling and blend state still come from the resource and final draw.</summary>
        public void SetMaterialTemplate(int texture, Material source)
        {
            var copy = source == null ? null : new Material(source) { hideFlags = HideFlags.DontSave, shaderKeywords = source.shaderKeywords };
            Destroy(templates[texture]); templates[texture] = copy; ClearMaterials(texture);
            TextureChanged?.Invoke(texture);
        }
        private void ClearMaterials(int texture)
        {
            for (int i = texture * MaterialsPerTexture; i < (texture + 1) * MaterialsPerTexture; i++) { Destroy(materials[i]); materials[i] = null; }
        }
        public int AddTexture(Texture texture, TextureInfo metadata, bool takeOwnership = false)
        {
            var prepared = Prepare(texture, metadata); int index = Asset.Textures.Length;
            while (index < Textures.Length && !ReferenceEquals(Textures[index], null)) index++;
            if (index == Textures.Length)
            {
                var output = Textures; Array.Resize(ref output, index + 1); Textures = output;
                Array.Resize(ref inputs, index + 1); Array.Resize(ref info, index + 1); Array.Resize(ref owned, index + 1); Array.Resize(ref materials, (index + 1) * MaterialsPerTexture);
                Array.Resize(ref templates, index + 1);
            }
            inputs[index] = texture; Textures[index] = prepared; info[index] = metadata; owned[index] = takeOwnership; return index;
        }
        public TextureInfo GetTextureInfo(int index) => info[index];
        internal void ReleaseTexture(int index)
        {
            if (Textures[index] != inputs[index]) Destroy(Textures[index]);
            if (owned[index]) Destroy(inputs[index]);
            Textures[index] = inputs[index] = null; owned[index] = false;
            Destroy(templates[index]); templates[index] = null; ClearMaterials(index);
            TextureChanged?.Invoke(index);
        }
        public void ReplaceTexture(int index, Texture texture, TextureInfo metadata, bool takeOwnership = false)
        {
            var prepared = Prepare(texture, metadata);
            if (Textures[index] != inputs[index]) Destroy(Textures[index]);
            if (owned[index] && inputs[index] != texture) Destroy(inputs[index]);
            inputs[index] = texture; Textures[index] = prepared; info[index] = metadata; owned[index] = takeOwnership;
            ClearMaterials(index);
            TextureChanged?.Invoke(index);
        }
        private static Texture Prepare(Texture source, TextureInfo info)
        {
            bool linear = QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear;
            bool hardwareDecode = GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            bool srgb = info.ColorSpace == Cane.ColorSpace.Srgb, pma = info.AlphaMode == AlphaMode.Premultiplied;
            bool convert = linear ? srgb != hardwareDecode || srgb && pma : !srgb || hardwareDecode;
            var filter = info.MinFilter == TextureFilter.Nearest && info.MagFilter == TextureFilter.Nearest ? FilterMode.Point : info.MipmapFilter == TextureFilter.Linear ? FilterMode.Trilinear : FilterMode.Bilinear;
            var wrapU = Wrap(info.WrapU); var wrapV = Wrap(info.WrapV);
            if (!convert && source.filterMode == filter && source.wrapModeU == wrapU && source.wrapModeV == wrapV && (source.mipmapCount > 1) == info.MipmapFilter.HasValue) return source;
            // Convert texels once, before interpolation/mip generation, never after filtering.
            var target = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                name = source.name + " Cane working color", hideFlags = HideFlags.DontSave,
                useMipMap = info.MipmapFilter.HasValue, autoGenerateMips = false,
                filterMode = filter, wrapModeU = wrapU, wrapModeV = wrapV
            };
            var material = new Material(Resources.Load<Shader>("Cane/NormalizeTexture")) { hideFlags = HideFlags.DontSave };
            var previous = RenderTexture.active; bool previousWrite = GL.sRGBWrite;
            try
            {
                material.SetVector("_Storage", new Vector4(srgb ? 1 : 0, hardwareDecode ? 1 : 0, pma ? 1 : 0, linear ? 1 : 0));
                GL.sRGBWrite = false; Graphics.Blit(source, target, material);
                if (target.useMipMap) target.GenerateMips();
                return target;
            }
            catch { Destroy(target); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousWrite; Destroy(material); }
        }
        public static TextureWrapMode Wrap(TextureWrap wrap) => wrap == TextureWrap.Repeat ? TextureWrapMode.Repeat : wrap == TextureWrap.Mirror ? TextureWrapMode.Mirror : TextureWrapMode.Clamp;
        internal static void Destroy(UnityEngine.Object value)
        { if (value != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); } }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var material in materials) Destroy(material);
            foreach (var template in templates) Destroy(template);
            for (int i = 0; i < Textures.Length; i++) { if (Textures[i] != inputs[i]) Destroy(Textures[i]); if (owned[i]) Destroy(inputs[i]); }
        }
    }
}
