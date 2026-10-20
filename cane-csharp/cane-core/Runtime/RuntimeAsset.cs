using System;

namespace Cane
{
    public enum ColorSpace { Srgb, Linear }
    public enum AlphaMode { Straight, Premultiplied }
    public enum TextureFilter { Nearest, Linear }
    public enum TextureWrap { Clamp, Repeat, Mirror }

    public sealed class TextureInfo
    {
        public readonly string Id, Path;
        public readonly uint Width, Height;
        public readonly ColorSpace ColorSpace;
        public readonly AlphaMode AlphaMode;
        public readonly TextureFilter MinFilter, MagFilter;
        public readonly TextureFilter? MipmapFilter;
        public readonly TextureWrap WrapU, WrapV;
        public TextureInfo(string id, string path, uint width, uint height, ColorSpace colorSpace = ColorSpace.Srgb,
            AlphaMode alphaMode = AlphaMode.Straight, TextureFilter minFilter = TextureFilter.Linear,
            TextureFilter magFilter = TextureFilter.Linear, TextureFilter? mipmapFilter = null, TextureWrap wrapU = TextureWrap.Clamp, TextureWrap wrapV = TextureWrap.Clamp)
        { Id = id; Path = path; Width = width; Height = height; ColorSpace = colorSpace; AlphaMode = alphaMode; MinFilter = minFilter; MagFilter = magFilter; MipmapFilter = mipmapFilter; WrapU = wrapU; WrapV = wrapV; }
    }

    public sealed class RuntimeAsset
    {
        public string Name { get; }
        public SkeletonData Data { get; }
        public readonly TextureInfo[] Textures;
        public readonly Animation[] Animations;
        public readonly string[] BoneIds, SlotIds, AttachmentIds, SkinIds, ConstraintIds, AnimationIds;
        internal RuntimeAsset(Format.ModelRecord model, SkeletonData data, TextureInfo[] textures, Animation[] animations)
        {
            Name = model.Name; Data = data; Textures = textures; Animations = animations;
            BoneIds = Array.ConvertAll(model.Bones, b => b.Id); SlotIds = Array.ConvertAll(model.Slots, s => s.Id);
            AttachmentIds = Array.ConvertAll(model.Attachments, a => a.Id); SkinIds = Array.ConvertAll(model.Skins, s => s.Id);
            ConstraintIds = Array.ConvertAll(model.Constraints, c => c.Id); AnimationIds = Array.ConvertAll(model.Animations, a => a.Id);
        }
        public static RuntimeAsset Load(byte[] input) => Format.AssetCompiler.Compile(Format.DocumentReader.Decode(input));
        public static RuntimeAsset Load(string json) => Format.AssetCompiler.Compile(Format.DocumentReader.Json(json));
    }
}
