using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Cane.Unity
{
    /// <summary>Repackages the current skin's image regions without changing exported assets.
    /// Dispose before the source resources; disposal restores the instance's original attachments.</summary>
    public sealed class CaneAtlas : IDisposable
    {
        private readonly Skeleton skeleton;
        private readonly CaneResources resources;
        private readonly List<int> textures = new List<int>();
        private readonly List<RenderTexture> auxiliary = new List<RenderTexture>();
        private readonly List<(int Index, MeshAttachment Source, MeshAttachment Mapped)> attachments = new List<(int, MeshAttachment, MeshAttachment)>();
        public IReadOnlyList<int> Textures => textures;
        private readonly struct Image
        {
            public readonly Texture Texture;
            public readonly int Width, Height;
            public Image(Texture texture, int width = 0, int height = 0)
            { Texture = texture; Width = width == 0 ? texture.width : width; Height = height == 0 ? texture.height : height; }
        }
        private sealed class Input
        {
            public TextureInfo Info;
            public Image[] Images;
            public int Profile, StepX = 1, StepY = 1, LimitWidth, LimitHeight, PaddingX, PaddingY;
        }
        private sealed class Region
        {
            public int Texture, X, Y;
            public TextureRegion Source;
            public Input Input;
            public Page Page;
        }
        private sealed class Page
        {
            public Input Input;
            public int Texture, X, Y, RowHeight, Width, Height;
            public readonly List<Region> Regions = new List<Region>();
        }
        public CaneAtlas(Skeleton skeleton, CaneResources resources) { this.skeleton = skeleton; this.resources = resources; }

        /// <summary>Returns the packed region count. Mipmapped and oversized regions retain their original textures.
        /// Call again after changing skins or attachments. Images are never resized.</summary>
        public int Pack(int maxSize = 2048, int padding = 2, Material material = null, IReadOnlyDictionary<string, Texture[]> layers = null)
        {
            if (maxSize <= 0 || padding < 1 || padding >= maxSize / 2) throw new ArgumentOutOfRangeException(nameof(maxSize), "Atlas size must leave room for the padded images.");
            var properties = layers == null ? Array.Empty<string>() : new List<string>(layers.Keys).ToArray();
            foreach (string property in properties)
                if (material == null || !material.HasTexture(property)) throw new ArgumentException("A material with texture property " + property + " is required.", nameof(material));
            Dispose();
            var frame = skeleton.Frame;
            if (frame == null) return 0;
            maxSize = Math.Min(maxSize, SystemInfo.maxTextureSize);
            var selected = new SortedSet<int>();
            foreach (int index in frame.Bindings) if (index >= 0) selected.Add(index);
            foreach (int index in frame.Overrides) if (index >= 0) selected.Add(index);
            var regions = new Dictionary<(int, TextureRegion), Region>();
            var inputs = new Dictionary<int, Input>(); var profiles = new List<Input>();
            var transforms = new (Vector2 Scale, Vector2 Offset)[properties.Length + 1];
            bool isolated = material != null && material.HasProperty("_OutlineWidth") && material.GetFloat("_Effects") != 0 && material.GetFloat("_OutlineWidth") > 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                string property = i == 0 ? "_MainTex" : properties[i - 1];
                transforms[i] = material == null ? (Vector2.one, Vector2.zero) : i == 0
                    ? (material.mainTextureScale, material.mainTextureOffset) : (material.GetTextureScale(property), material.GetTextureOffset(property));
                isolated |= transforms[i].Scale != Vector2.one || transforms[i].Offset != Vector2.zero;
            }
            void Collect(int texture, float[] uvs)
            {
                if (uvs.Length == 0) return;
                if (!inputs.TryGetValue(texture, out var input))
                {
                    input = Prepare(texture, material, properties, layers, maxSize, padding); inputs.Add(texture, input);
                    if (input != null)
                    {
                        input.Profile = profiles.FindIndex(other => Compatible(input, other));
                        if (input.Profile < 0) { input.Profile = profiles.Count; profiles.Add(input); }
                    }
                }
                if (input == null) return;
                var image = input.Images[0];
                var bounds = TextureRegion.Bounds(uvs, image.Width, image.Height); var rect = bounds;
                if (isolated)
                {
                    rect = rect.Union(new TextureRegion(0, 0, image.Width, image.Height));
                    foreach (var transform in transforms)
                        rect = rect.Union(bounds.Transform(transform.Scale.x, transform.Scale.y,
                            transform.Offset.x * image.Width, (1 - transform.Scale.y - transform.Offset.y) * image.Height));
                    if (material.HasProperty("_OutlineUvRect"))
                    {
                        var outline = material.GetVector("_OutlineUvRect");
                        rect = rect.Union(TextureRegion.Bounds(new[] { outline.x, 1 - outline.w, outline.z, 1 - outline.y }, image.Width, image.Height));
                    }
                }
                rect = rect.Align(input.StepX, input.StepY);
                var key = (texture, isolated ? default : bounds);
                if (regions.TryGetValue(key, out var region)) region.Source = region.Source.Union(rect);
                else regions.Add(key, new Region { Texture = texture, Source = rect, Input = input });
            }
            foreach (int index in selected) if (frame.Attachments[index] is MeshAttachment mesh)
            {
                Collect(mesh.Texture, mesh.Uvs);
                if (mesh.Sequence != null) foreach (var image in mesh.Sequence.Frames) Collect(image.Texture, image.Uvs);
            }
            var sorted = new List<Region>(regions.Values);
            if (sorted.Count == 0) return 0;
            sorted.Sort((a, b) => {
                int order = b.Source.Height.CompareTo(a.Source.Height);
                return order != 0 ? order : b.Source.Width.CompareTo(a.Source.Width);
            });
            var pages = new List<Page>();
            var current = new Dictionary<(int, int), Page>(); int packed = 0;
            foreach (var region in sorted)
            {
                var input = region.Input; var key = (input.Profile, isolated ? region.Texture : -1);
                int width = region.Source.Width + input.PaddingX * 2, height = region.Source.Height + input.PaddingY * 2;
                if (width > input.LimitWidth || height > input.LimitHeight) continue;
                current.TryGetValue(key, out var page);
                if (page != null && page.X + width > input.LimitWidth) { page.Y += page.RowHeight; page.X = page.RowHeight = 0; }
                if (page == null || page.Y + height > input.LimitHeight)
                {
                    page = new Page { Input = input }; pages.Add(page); current[key] = page;
                }
                region.X = page.X + input.PaddingX; region.Y = page.Y + input.PaddingY; region.Page = page; page.Regions.Add(region); packed++;
                page.X += width; page.RowHeight = Math.Max(page.RowHeight, height);
                page.Width = Math.Max(page.Width, page.X); page.Height = Math.Max(page.Height, page.Y + height);
            }
            if (packed == 0) return 0;
            try
            {
                foreach (var page in pages)
                {
                    var input = page.Input;
                    page.Width = Math.Min(input.LimitWidth, RoundUp(Mathf.NextPowerOfTwo(page.Width), input.StepX));
                    page.Height = Math.Min(input.LimitHeight, RoundUp(Mathf.NextPowerOfTwo(page.Height), input.StepY));
                    var texture = Render(page, 0);
                    var info = new TextureInfo("runtime-atlas", "", (uint)page.Width, (uint)page.Height,
                        QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear ? ColorSpace.Linear : ColorSpace.Srgb,
                        input.Info.AlphaMode, input.Info.MinFilter, input.Info.MagFilter);
                    try { page.Texture = resources.AddTexture(texture, info, true); }
                    catch { CaneResources.Destroy(texture); throw; }
                    textures.Add(page.Texture);
                    if (material != null)
                    {
                        var template = new Material(material) { shaderKeywords = material.shaderKeywords };
                        try
                        {
                            for (int i = 0; i < properties.Length; i++)
                            {
                                var layer = Render(page, i + 1); auxiliary.Add(layer); template.SetTexture(properties[i], layer);
                            }
                            if (isolated) RemapMaterial(template, page, properties, transforms);
                            resources.SetMaterialTemplate(page.Texture, template);
                        }
                        finally { CaneResources.Destroy(template); }
                    }
                }
                (int, float[]) Map(int texture, float[] uvs)
                {
                    var input = resources.Textures[texture];
                    var key = (texture, isolated ? default : TextureRegion.Bounds(uvs, input.width, input.height));
                    if (!regions.TryGetValue(key, out var region) || region.Page == null) return (texture, uvs);
                    return (region.Page.Texture, region.Source.RemapUvs(uvs, input.width, input.height, region.X, region.Y, region.Page.Width, region.Page.Height));
                }
                foreach (int index in selected) if (frame.Attachments[index] is MeshAttachment mesh)
                {
                    var mapped = mesh.RemapTextures(Map);
                    attachments.Add((index, mesh, mapped)); skeleton.ReplaceAttachment(index, mapped);
                }
                skeleton.UpdateGeometry();
                return packed;
            }
            catch { Dispose(); throw; }
        }
        private Input Prepare(int texture, Material material, string[] properties, IReadOnlyDictionary<string, Texture[]> layers, int maxSize, int padding)
        {
            var info = resources.GetTextureInfo(texture);
            if (info.MipmapFilter.HasValue) return null;
            var basis = resources.Textures[texture];
            var input = new Input { Info = info, Images = new Image[properties.Length + 1], LimitWidth = maxSize, LimitHeight = maxSize };
            input.Images[0] = new Image(basis);
            for (int i = 0; i < properties.Length; i++)
            {
                var values = layers[properties[i]];
                var image = values != null && texture < values.Length ? values[texture] : null;
                if (image == null) image = material.GetTexture(properties[i]);
                if (image == null) image = DefaultTexture(material.shader, properties[i]);
                bool constant = image.width == 1 && image.height == 1 || image == Texture2D.whiteTexture || image == Texture2D.blackTexture
                    || image == Texture2D.grayTexture || image == Texture2D.normalTexture || image == Texture2D.redTexture;
                if (image.dimension != UnityEngine.Rendering.TextureDimension.Tex2D || !constant && image.mipmapCount > 1) return null;
                input.Images[i + 1] = constant ? new Image(image, basis.width, basis.height) : new Image(image);
            }
            foreach (var image in input.Images)
            {
                input.LimitWidth = Math.Min(input.LimitWidth, Scale(maxSize, basis.width, image.Width));
                input.LimitHeight = Math.Min(input.LimitHeight, Scale(maxSize, basis.height, image.Height));
                input.StepX = Lcm(input.StepX, basis.width / Gcd(basis.width, image.Width));
                input.StepY = Lcm(input.StepY, basis.height / Gcd(basis.height, image.Height));
                if (input.StepX > input.LimitWidth || input.StepY > input.LimitHeight) return null;
            }
            input.LimitWidth -= input.LimitWidth % input.StepX; input.LimitHeight -= input.LimitHeight % input.StepY;
            input.PaddingX = RoundUp(padding, input.StepX); input.PaddingY = RoundUp(padding, input.StepY);
            return input;
        }
        private static Texture DefaultTexture(Shader shader, string property)
        {
            switch (shader.GetPropertyTextureDefaultName(shader.FindPropertyIndex(property)))
            {
                case "bump": return Texture2D.normalTexture;
                case "black": return Texture2D.blackTexture;
                case "gray": case "grey": return Texture2D.grayTexture;
                case "red": return Texture2D.redTexture;
                default: return Texture2D.whiteTexture;
            }
        }
        private static bool Compatible(Input a, Input b)
        {
            if (a.Info.AlphaMode != b.Info.AlphaMode || a.Info.MinFilter != b.Info.MinFilter || a.Info.MagFilter != b.Info.MagFilter) return false;
            for (int i = 0; i < a.Images.Length; i++)
            {
                var left = a.Images[i]; var right = b.Images[i];
                if ((long)left.Width * b.Images[0].Width != (long)right.Width * a.Images[0].Width
                    || (long)left.Height * b.Images[0].Height != (long)right.Height * a.Images[0].Height
                    || Precision(left.Texture) != Precision(right.Texture) || left.Texture.filterMode != right.Texture.filterMode
                    || left.Texture.anisoLevel != right.Texture.anisoLevel) return false;
            }
            return true;
        }
        private static int Gcd(int a, int b) { while (b != 0) { int next = a % b; a = b; b = next; } return a; }
        private static int Lcm(int a, int b) => (int)Math.Min(int.MaxValue, (long)a / Gcd(a, b) * b);
        private static int Scale(int value, int size, int basis) => (int)((long)value * size / basis);
        private static int RoundUp(int value, int step) => (value + step - 1) / step * step;
        private static int Wrap(TextureWrapMode mode) => mode == TextureWrapMode.Clamp ? 0 : mode == TextureWrapMode.Repeat ? 1 : mode == TextureWrapMode.Mirror ? 2 : 3;
        private static int Precision(Texture texture)
        {
            var format = texture.graphicsFormat;
            return format == GraphicsFormat.R8G8B8A8_UNorm || format == GraphicsFormat.B8G8R8A8_UNorm ? 0
                : format == GraphicsFormat.R16G16B16A16_SFloat ? 1 : 2;
        }
        private static void RemapMaterial(Material material, Page page, string[] properties, (Vector2 Scale, Vector2 Offset)[] transforms)
        {
            var region = page.Regions[0]; var basis = region.Input.Images[0];
            var origin = region.Source.RemapUvs(new float[] { 0, 1 }, basis.Width, basis.Height, region.X, region.Y, page.Width, page.Height);
            var scale = new Vector2((float)basis.Width / page.Width, (float)basis.Height / page.Height);
            var offset = new Vector2(origin[0], 1 - origin[1]);
            for (int i = 0; i < transforms.Length; i++)
            {
                var value = transforms[i];
                var remapped = Vector2.Scale(value.Offset, scale) + Vector2.Scale(offset, Vector2.one - value.Scale);
                if (i == 0) material.mainTextureOffset = remapped;
                else material.SetTextureOffset(properties[i - 1], remapped);
            }
            if (material.HasProperty("_OutlineUvRect"))
            {
                var rect = material.GetVector("_OutlineUvRect");
                var uv = region.Source.RemapUvs(new[] { rect.x, 1 - rect.w, rect.z, 1 - rect.y }, basis.Width, basis.Height, region.X, region.Y, page.Width, page.Height);
                material.SetVector("_OutlineUvRect", new Vector4(uv[0], 1 - uv[3], uv[2], 1 - uv[1]));
            }
        }
        private RenderTexture Render(Page page, int layer)
        {
            var input = page.Input; var image = input.Images[layer]; var basis = input.Images[0]; int precision = Precision(image.Texture);
            var target = new RenderTexture(Scale(page.Width, image.Width, basis.Width), Scale(page.Height, image.Height, basis.Height), 0,
                precision == 0 ? RenderTextureFormat.ARGB32 : precision == 1 ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear) {
                name = "Cane runtime atlas", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp,
                anisoLevel = image.Texture.anisoLevel, filterMode = image.Texture.filterMode
            };
            var material = new Material(Resources.Load<Shader>("Cane/Repack")) { hideFlags = HideFlags.DontSave };
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            GL.PushMatrix();
            try
            {
                RenderTexture.active = target; GL.sRGBWrite = false; GL.LoadOrtho(); GL.Clear(false, true, Color.clear);
                foreach (var region in page.Regions)
                {
                    var images = region.Input.Images; var source = images[layer].Texture; var rect = region.Source;
                    int px = region.Input.PaddingX, py = region.Input.PaddingY, width = rect.Width + px * 2, height = rect.Height + py * 2;
                    material.SetTexture("_MainTex", source);
                    material.SetVector("_Source", new Vector4(Scale(rect.X - px, images[layer].Width, images[0].Width),
                        source.height - Scale(rect.Y + rect.Height + py, images[layer].Height, images[0].Height),
                        Scale(width, images[layer].Width, images[0].Width), Scale(height, images[layer].Height, images[0].Height)));
                    material.SetVector("_Size", new Vector4(source.width, source.height, Wrap(source.wrapModeU), Wrap(source.wrapModeV)));
                    material.SetPass(0);
                    float left = (float)(region.X - px) / page.Width, right = (float)(region.X - px + width) / page.Width;
                    float top = 1f - (float)(region.Y - py) / page.Height, bottom = 1f - (float)(region.Y - py + height) / page.Height;
                    GL.Begin(GL.QUADS);
                    GL.TexCoord2(0, 0); GL.Vertex3(left, bottom, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(right, bottom, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(right, top, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(left, top, 0);
                    GL.End();
                }
                return target;
            }
            catch { CaneResources.Destroy(target); throw; }
            finally { GL.PopMatrix(); GL.sRGBWrite = srgb; RenderTexture.active = previous; CaneResources.Destroy(material); }
        }
        public void Dispose()
        {
            var frame = skeleton.Frame;
            foreach (var item in attachments)
                if (ReferenceEquals(frame.Attachments[item.Index], item.Mapped)) skeleton.ReplaceAttachment(item.Index, item.Source);
            if (attachments.Count > 0) skeleton.UpdateGeometry();
            attachments.Clear();
            foreach (int texture in textures) resources.ReleaseTexture(texture);
            textures.Clear();
            foreach (var image in auxiliary) CaneResources.Destroy(image);
            auxiliary.Clear();
        }
    }
}
