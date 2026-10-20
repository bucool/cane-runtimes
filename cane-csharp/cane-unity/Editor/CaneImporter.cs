using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace Cane.Unity.Editor
{
    [ScriptedImporter(1, "cane2")]
    public sealed class CaneImporter : ScriptedImporter
    {
        public Shader SceneShader;
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = Read(context.assetPath);
            asset.SceneShader = SceneShader;
            context.AddObjectToAsset("skeleton", asset); context.SetMainObject(asset);
            for (int i = 0; i < asset.Textures.Length; i++)
            {
                context.DependsOnSourceAsset(ImagePath(context.assetPath, asset.Data.Textures[i].Path));
                context.AddObjectToAsset("texture" + i, asset.Textures[i]);
            }
        }
        private static string ImagePath(string document, string image) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document), image)).Replace('\\', '/');
        internal static CaneAsset Read(string path)
        {
            var asset = ScriptableObject.CreateInstance<CaneAsset>(); asset.name = Path.GetFileNameWithoutExtension(path); asset.Document = File.ReadAllBytes(path);
            var data = asset.Data; asset.Textures = new Texture2D[data.Textures.Length];
            for (int i = 0; i < asset.Textures.Length; i++)
            {
                var info = data.Textures[i];
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, info.MipmapFilter.HasValue, info.ColorSpace == ColorSpace.Linear) { name = info.Id };
                if (!texture.LoadImage(File.ReadAllBytes(ImagePath(path, info.Path)))) throw new InvalidDataException("Cannot decode Cane texture " + info.Path);
                texture.filterMode = info.MinFilter == TextureFilter.Nearest && info.MagFilter == TextureFilter.Nearest ? FilterMode.Point : info.MipmapFilter == TextureFilter.Linear ? FilterMode.Trilinear : FilterMode.Bilinear;
                texture.wrapModeU = CaneResources.Wrap(info.WrapU); texture.wrapModeV = CaneResources.Wrap(info.WrapV);
                asset.Textures[i] = texture;
            }
            return asset;
        }
        [MenuItem("Assets/Cane/Create from JSON", true)]
        private static bool CanCreate() => Selection.activeObject is TextAsset && AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".json");
        [MenuItem("Assets/Cane/Create from JSON")]
        private static void Create()
        {
            string source = AssetDatabase.GetAssetPath(Selection.activeObject), destination = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(source, ".asset"));
            var asset = Read(source); AssetDatabase.CreateAsset(asset, destination);
            foreach (var texture in asset.Textures) AssetDatabase.AddObjectToAsset(texture, asset);
            AssetDatabase.SaveAssets(); Selection.activeObject = asset;
        }
    }
}
