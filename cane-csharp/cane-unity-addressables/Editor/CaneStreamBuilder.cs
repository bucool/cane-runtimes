using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Experimental.Rendering;

namespace Cane.Unity.Editor
{
    public static class CaneStreamBuilder
    {
        [MenuItem("Assets/Cane/Create streamed asset", true)]
        private static bool CanCreate() => Selection.activeObject is CaneAsset;
        [MenuItem("Assets/Cane/Create streamed asset")]
        private static void CreateSelected()
        {
            var source = (CaneAsset)Selection.activeObject;
            Directory.CreateDirectory("Assets/Cane Streaming"); AssetDatabase.Refresh();
            string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
            Selection.activeObject = Create(source, AssetDatabase.GenerateUniqueAssetPath("Assets/Cane Streaming/" + file + ".asset"));
        }

        /// <summary>Creates a separate streaming asset. Source document and textures are untouched.
        /// Keep the destination outside Resources so full textures stay exclusively addressable.</summary>
        public static CaneTextureStream Create(CaneAsset source, string destination, int previewSize = 16)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup("Cane textures");
            if (group == null) {
                group = settings.CreateGroup("Cane textures", false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = group.GetSchema<BundledAssetGroupSchema>(); schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            }
            string folder = Path.ChangeExtension(destination, null) + " Textures";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var stream = ScriptableObject.CreateInstance<CaneTextureStream>(); AssetDatabase.CreateAsset(stream, destination);
            var asset = ScriptableObject.CreateInstance<CaneAsset>(); asset.name = "Skeleton";
            asset.Document = (byte[])source.Document.Clone(); asset.SceneShader = source.SceneShader;
            asset.Textures = new Texture2D[source.Textures.Length];
            stream.Asset = asset; stream.Textures = new AssetReferenceTexture2D[source.Textures.Length];
            AssetDatabase.AddObjectToAsset(asset, stream);
            for (int i = 0; i < source.Textures.Length; i++)
            {
                var input = source.Textures[i]; string original = AssetDatabase.GetAssetPath(input), file;
                if (!string.IsNullOrEmpty(original) && AssetDatabase.LoadMainAssetAtPath(original) == input)
                {
                    file = folder + "/" + i + Path.GetExtension(original);
                    if (!AssetDatabase.CopyAsset(original, file)) throw new IOException("Could not copy addressable texture " + original);
                }
                else
                {
                    file = folder + "/" + i + ".asset";
                    var full = Object.Instantiate(input); full.name = input.name; full.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(full, file);
                }
                string guid = AssetDatabase.AssetPathToGUID(file); settings.CreateOrMoveEntry(guid, group);
                stream.Textures[i] = new AssetReferenceTexture2D(guid);
                asset.Textures[i] = Preview(input, source.Data.Textures[i], previewSize);
                asset.Textures[i].name = "Preview " + i; AssetDatabase.AddObjectToAsset(asset.Textures[i], stream);
            }
            EditorUtility.SetDirty(asset); EditorUtility.SetDirty(stream); AssetDatabase.SaveAssets();
            return stream;
        }
        private static Texture2D Preview(Texture2D source, TextureInfo info, int size)
        {
            float scale = Mathf.Min(1, (float)Mathf.Max(1, size) / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            bool srgb = info.ColorSpace == ColorSpace.Srgb;
            var preview = new Texture2D(width, height, TextureFormat.RGBA32, info.MipmapFilter.HasValue, !srgb);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var material = new Material(Resources.Load<Shader>("Cane/NormalizeTexture")) { hideFlags = HideFlags.DontSave };
            var previous = RenderTexture.active; bool previousWrite = GL.sRGBWrite;
            try
            {
                // Read original storage values; the regular resource path performs working-space conversion.
                material.SetVector("_Storage", new Vector4(srgb ? 1 : 0, GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat) ? 1 : 0,
                    info.AlphaMode == AlphaMode.Premultiplied ? 1 : 0, srgb ? 0 : 1));
                GL.sRGBWrite = false; Graphics.Blit(source, target, material); RenderTexture.active = target;
                preview.ReadPixels(new Rect(0, 0, width, height), 0, 0); preview.Apply();
                return preview;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousWrite; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(material); }
        }
        [MenuItem("GameObject/Cane/Streamed skeleton", false, 10)]
        private static void CreateObject()
        {
            var source = Selection.activeObject as CaneTextureStream;
            var go = new GameObject("Cane streamed skeleton"); Undo.RegisterCreatedObjectUndo(go, "Create Cane streamed skeleton");
            var loader = go.AddComponent<CaneTextureLoader>(); loader.Source = source;
            go.AddComponent<CaneRenderer>(); Selection.activeGameObject = go;
        }
    }
}
