using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cane.Unity
{
    [UxmlElement]
    public partial class CaneVisualElement : VisualElement
    {
        private CaneAsset asset;
        private GameObject owner;
        private CaneResources resources;
        private readonly List<CaneVisualPart> parts = new List<CaneVisualPart>();
        private readonly IVisualElementScheduledItem clock;
        private Rect referenceBounds;
        private int lastFrame = -1;

        [UxmlAttribute] public CaneAsset Asset { get => asset; set { if (asset != value) { asset = value; Rebuild(); } } }
        [UxmlAttribute] public string InitialAnimation { get; set; } = "";
        [UxmlAttribute] public bool Loop { get; set; } = true;
        [UxmlAttribute] public float TimeScale { get; set; } = 1;
        [UxmlAttribute] public bool UnscaledTime { get; set; }
        [UxmlAttribute] public bool Frozen { get; set; }
        /// <summary>Viewport in Cane coordinates. A zero-sized rectangle fits the initial pose.</summary>
        [UxmlAttribute] public Rect ViewBox { get; set; }
        public CanePlayer Player { get; private set; }

        public CaneVisualElement()
        {
            clock = schedule.Execute(Tick).Every(0); clock.Pause();
            RegisterCallback<AttachToPanelEvent>(_ => { if (asset != null) Rebuild(); else Listen(); clock.Resume(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { clock.Pause(); Release(); });
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
        }

        /// <summary>Display an existing player. Its owner continues to advance and dispose it.</summary>
        public void Bind(CanePlayer player)
        {
            Release(); asset = null; Player = player; referenceBounds = default;
            if (panel != null) Listen();
        }

        public void Rebuild()
        {
            Release(); Player = null; referenceBounds = default;
            if (panel == null || asset == null) return;
            owner = new GameObject("Cane UI Toolkit player") { hideFlags = HideFlags.HideAndDontSave };
            Player = owner.AddComponent<CanePlayer>(); Player.UpdateMode = UpdateMode.Manual;
            Player.Asset = asset; Player.Initialize();
            FitInitialPose();
            if (!string.IsNullOrEmpty(InitialAnimation)) Player.Play(InitialAnimation, Loop);
            Listen(); Player.Advance(0); lastFrame = -1;
        }

        private void Listen()
        {
            if (Player == null) return;
            Player.AfterGeometry += Updated; Player.Rebuilt += Reinitialized;
            ListenResources(); FitInitialPose(); Refresh();
        }
        private void ListenResources()
        {
            if (resources != null) resources.TextureChanged -= TextureChanged;
            resources = Player?.Resources;
            if (resources != null) resources.TextureChanged += TextureChanged;
        }
        private void TextureChanged(int _) => Refresh();
        private void Reinitialized(CanePlayer _) { ListenResources(); referenceBounds = default; Refresh(); }
        private void Updated(CanePlayer _) => Refresh();
        private void Tick()
        {
            if (owner == null || Frozen || !Application.isPlaying || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount; Player.TimeScale = TimeScale;
            Player.Advance(UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }
        private void FitInitialPose()
        {
            var draws = Player?.Skeleton?.Frame?.Draws;
            if (draws == null || referenceBounds.width > 0 && referenceBounds.height > 0) return;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (var draw in draws) for (int i = 0; i < draw.VertexCount * 2; i += 2)
            {
                minX = Mathf.Min(minX, draw.Vertices[i]); maxX = Mathf.Max(maxX, draw.Vertices[i]);
                minY = Mathf.Min(minY, draw.Vertices[i + 1]); maxY = Mathf.Max(maxY, draw.Vertices[i + 1]);
            }
            if (maxX > minX && maxY > minY) referenceBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
        public void Refresh()
        {
            if (panel == null) return;
            FitInitialPose();
            var box = ViewBox.width > 0 && ViewBox.height > 0 ? ViewBox : referenceBounds;
            var draws = Player?.Skeleton?.Frame?.Draws;
            int count = draws == null || box.width <= 0 || box.height <= 0 ? 0 : draws.Count;
            float scale = count == 0 ? 0 : Mathf.Min(contentRect.width / box.width, contentRect.height / box.height);
            var origin = contentRect.center - new Vector2(box.center.x, -box.center.y) * scale;
            int used = 0;
            for (int first = 0; first < count;)
            {
                int end = first + 1;
                while (end < count && CaneVisualPart.Compatible(draws[first], draws[end])) end++;
                if (used == parts.Count) { var part = new CaneVisualPart(); parts.Add(part); hierarchy.Add(part); }
                parts[used].style.display = DisplayStyle.Flex;
                parts[used++].Update(draws, first, end, Player.Resources, origin, scale);
                first = end;
            }
            for (int i = used; i < parts.Count; i++) parts[i].style.display = DisplayStyle.None;
        }
        private void Release()
        {
            if (Player != null) { Player.AfterGeometry -= Updated; Player.Rebuilt -= Reinitialized; }
            if (resources != null) resources.TextureChanged -= TextureChanged;
            resources = null;
            foreach (var part in parts) { part.Dispose(); part.RemoveFromHierarchy(); }
            parts.Clear();
            if (owner != null) { Destroy(owner); owner = null; Player = null; }
        }
        internal static void Destroy(Object value)
        { if (value != null) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); } }
    }
}
