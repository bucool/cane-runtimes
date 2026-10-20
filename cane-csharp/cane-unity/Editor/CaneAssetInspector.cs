using UnityEngine;
using UnityEditor;

namespace Cane.Unity.Editor
{
    [CustomEditor(typeof(CaneAsset))]
    public sealed class CaneAssetInspector : UnityEditor.Editor
    {
        private PreviewRenderUtility preview;
        private CanePlayer player;
        private CaneRenderer renderer;
        private string[] animations, skins;
        private int animation, skin;
        private bool playing = true;
        private double lastTime;
        private UnityEngine.Bounds bounds;
        private void OnEnable()
        {
            if (((CaneAsset)target).Document.Length == 0) return;
            var data = ((CaneAsset)target).Data;
            animations = new string[data.Animations.Length + 1]; animations[0] = "Setup pose";
            for (int i = 0; i < data.Animations.Length; i++) animations[i + 1] = data.Animations[i].Name;
            var allSkins = data.Data.Appearance?.Skins;
            skins = new string[(allSkins?.Length ?? 0) + 1]; skins[0] = "Default skin";
            for (int i = 1; i < skins.Length; i++) skins[i] = allSkins[i - 1].Name;
        }
        private void OnDisable() { preview?.Cleanup(); preview = null; player = null; renderer = null; }
        public override void OnInspectorGUI()
        {
            var asset = (CaneAsset)target;
            if (asset.Document.Length == 0) { EditorGUILayout.HelpBox("Import a .cane2 file, or use Assets → Cane → Create from JSON.", MessageType.Info); return; }
            var data = asset.Data;
            EditorGUILayout.LabelField(data.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{data.BoneIds.Length} bones · {data.SlotIds.Length} slots · {data.Animations.Length} animations");
            DrawDefaultInspector();
            if (GUILayout.Button("Create scene character"))
            {
                var go = new GameObject(asset.name); Undo.RegisterCreatedObjectUndo(go, "Create Cane character");
                var player = go.AddComponent<CanePlayer>(); player.Asset = asset; player.Initialize(); go.AddComponent<CaneRenderer>().Upload();
                Selection.activeGameObject = go;
            }
        }
        public override bool HasPreviewGUI() => animations != null;
        public override bool RequiresConstantRepaint() => playing && animation > 0;
        public override void OnPreviewSettings()
        {
            playing = GUILayout.Toggle(playing, "Play", EditorStyles.toolbarButton);
            EditorGUI.BeginChangeCheck();
            animation = EditorGUILayout.Popup(animation, animations, EditorStyles.toolbarPopup, GUILayout.Width(130));
            skin = EditorGUILayout.Popup(skin, skins, EditorStyles.toolbarPopup, GUILayout.Width(110));
            if (EditorGUI.EndChangeCheck() && player != null) Select();
        }
        private void Select()
        {
            player.Initialize();
            if (skin > 0) player.Skeleton.SetSkins(new[] { player.Skeleton.Data.Appearance.Skins[skin - 1] });
            if (animation > 0) player.State.Set(0, player.Resources.Asset.Animations[animation - 1], true);
            player.Advance(0); renderer.Upload(); bounds = renderer.Buffer.Mesh.bounds; lastTime = EditorApplication.timeSinceStartup;
        }
        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
        {
            if (preview == null)
            {
                preview = new PreviewRenderUtility();
                var go = new GameObject("Cane preview") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(go); player = go.AddComponent<CanePlayer>(); player.Asset = (CaneAsset)target; player.UpdateMode = UpdateMode.Manual;
                renderer = go.AddComponent<CaneRenderer>(); renderer.Manual = true; Select();
                preview.camera.orthographic = true; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
            }
            var entry = player.State.Current(0);
            if (entry != null && entry.End > 0)
            {
                var slider = new Rect(rect.x + 8, rect.yMax - 20, rect.width - 16, 16); rect.height -= 24;
                EditorGUI.BeginChangeCheck(); double time = GUI.HorizontalSlider(slider, (float)entry.SampleTime(), 0, (float)entry.End);
                if (EditorGUI.EndChangeCheck()) { entry.Seek(time); playing = false; player.Advance(0); }
            }
            if (Event.current.type != UnityEngine.EventType.Repaint) return;
            double now = EditorApplication.timeSinceStartup;
            if (playing && entry != null) player.Advance(System.Math.Min(.1, now - lastTime)); lastTime = now;
            renderer.Upload(); bounds.Encapsulate(renderer.Buffer.Mesh.bounds);
            preview.camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10);
            preview.camera.orthographicSize = Mathf.Max(.01f, Mathf.Max(bounds.size.y, bounds.size.x * rect.height / rect.width) * .55f);
            preview.BeginPreview(rect, background); preview.Render(); GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }
        public override void OnPreviewGUI(Rect rect, GUIStyle background) => OnInteractivePreviewGUI(rect, background);
    }
}
