using UnityEditor;
using UnityEngine;

namespace Cane.Unity.Editor
{
    [CustomEditor(typeof(CaneTextureStream))]
    public sealed class CaneStreamInspector : UnityEditor.Editor
    {
        private UnityEditor.Editor preview;
        private CaneAsset previewAsset, source;
        private int revision, assetRevision;

        private void OnEnable() => EditorApplication.projectChanged += Invalidate;
        private void OnDisable()
        {
            EditorApplication.projectChanged -= Invalidate;
            ClearPreview();
        }
        private void Invalidate() { ClearPreview(); Repaint(); }
        private void ClearPreview()
        {
            if (preview != null) DestroyImmediate(preview);
            if (previewAsset != null) DestroyImmediate(previewAsset);
            preview = null; previewAsset = null; source = null;
        }
        private UnityEditor.Editor EditorForPreview()
        {
            var stream = (CaneTextureStream)target;
            if (preview != null && source != null && source == stream.Asset && revision == stream.Revision && assetRevision == source.Revision)
                return preview;
            ClearPreview();
            source = stream.Asset;
            if (source == null || source.Document.Length == 0) return null;
            revision = stream.Revision; assetRevision = source.Revision;
            previewAsset = CreateInstance<CaneAsset>();
            previewAsset.hideFlags = HideFlags.HideAndDontSave;
            previewAsset.Document = source.Document;
            previewAsset.SceneShader = source.SceneShader;
            previewAsset.Textures = (Texture2D[])source.Textures.Clone();
            for (int i = 0; i < previewAsset.Textures.Length && i < stream.Textures.Length; i++)
            {
                var page = stream.Textures[i];
                if (page == null || string.IsNullOrEmpty(page.AssetGUID)) continue;
                string path = AssetDatabase.GUIDToAssetPath(page.AssetGUID);
                if (path.Length == 0) continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null) previewAsset.Textures[i] = texture;
            }
            return preview = CreateEditor(previewAsset, typeof(CaneAssetInspector));
        }
        public override void OnInspectorGUI()
        {
            if (DrawDefaultInspector()) Invalidate();
        }
        public override bool HasPreviewGUI() => EditorForPreview() != null;
        public override bool RequiresConstantRepaint() => preview != null && preview.RequiresConstantRepaint();
        public override void OnPreviewSettings() => EditorForPreview()?.OnPreviewSettings();
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) =>
            EditorForPreview()?.OnInteractivePreviewGUI(rect, background);
        public override void OnPreviewGUI(Rect rect, GUIStyle background) => OnInteractivePreviewGUI(rect, background);
    }
}
