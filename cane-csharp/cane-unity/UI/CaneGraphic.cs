using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cane.Unity
{
    /// <summary>Canvas adapter. Consecutive material groups use independent maskable graphics.</summary>
    [ExecuteAlways, RequireComponent(typeof(CanePlayer), typeof(CanvasRenderer))]
    public sealed class CaneGraphic : MaskableGraphic
    {
        public bool Manual;
        public Material[] SlotMaterials = Array.Empty<Material>();
        private CanePlayer player;
        private readonly List<CaneGraphicPart> parts = new List<CaneGraphicPart>();
        private readonly CanvasDraws canvasDraws = new CanvasDraws();
        private readonly List<CaneSlotGraphic> objects = new List<CaneSlotGraphic>();
        private CaneSlotGraphic[] slotObjects = Array.Empty<CaneSlotGraphic>();
        private bool objectsDirty = true;
        public int PartCount { get; private set; }
        protected override void OnEnable() { base.OnEnable(); player = GetComponent<CanePlayer>(); player.Rebuilt += Rebuilt; objectsDirty = true; ConfigureCanvas(); }
        private void Rebuilt(CanePlayer _) => objectsDirty = true;
        internal void Register(CaneSlotGraphic value) { objects.Add(value); objectsDirty = true; }
        internal void Unregister(CaneSlotGraphic value) { objects.Remove(value); objectsDirty = true; }
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            ConfigureCanvas();
        }
        private void ConfigureCanvas()
        {
            if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        }
        private void LateUpdate() { if (!Manual) Upload(); }
        public void Upload()
        {
            if (!isActiveAndEnabled) return;
            var frame = player.Skeleton?.Frame;
            if (frame != null) canvasDraws.Update(frame.Draws); else canvasDraws.Draws.Clear();
            int part = 0, sibling = 0;
            if (frame == null) foreach (var item in objects) item.Hide();
            if (frame == null || objects.Count == 0) UploadRange(0, canvasDraws.Draws.Count, ref part, ref sibling, false);
            else
            {
                if (objectsDirty)
                {
                    slotObjects = new CaneSlotGraphic[frame.Slots.Length];
                    foreach (var item in objects) { item.Hide(); int slot = player.FindSlot(item.Slot); if (slot >= 0) slotObjects[slot] = item; }
                    objectsDirty = false;
                }
                int at = 0, start = 0;
                foreach (int slot in frame.AppliedOrder)
                {
                    var item = slotObjects[slot];
                    if (item != null && item.Before)
                    {
                        UploadRange(start, at, ref part, ref sibling, true); start = at;
                        item.Upload(player, slot, sibling++);
                    }
                    while (at < canvasDraws.Draws.Count && canvasDraws.Draws[at].Slot == slot) at++;
                    if (item != null && !item.Before)
                    {
                        UploadRange(start, at, ref part, ref sibling, true); start = at;
                        item.Upload(player, slot, sibling++);
                    }
                }
                UploadRange(start, at, ref part, ref sibling, true);
            }
            for (int i = part; i < PartCount; i++) parts[i].gameObject.SetActive(false);
            PartCount = part;
        }
        private Material DrawMaterial(Draw draw, bool overlay) => draw.Slot < SlotMaterials.Length && SlotMaterials[draw.Slot] != null
            ? SlotMaterials[draw.Slot] : player.Resources.Material(draw.Texture, draw.Blend, true, overlay);
        private void UploadRange(int start, int count, ref int part, ref int sibling, bool order)
        {
            var draws = canvasDraws.Draws;
            bool overlay = canvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
            var tint = color * canvasRenderer.GetColor();
            while (start < count)
            {
                var first = draws[start]; var material = DrawMaterial(first, overlay); int end = start + 1, vertices = first.VertexCount;
                while (end < count && vertices + draws[end].VertexCount <= CanvasDraws.Limit && DrawMaterial(draws[end], overlay) == material) { vertices += draws[end].VertexCount; end++; }
                if (part == parts.Count)
                {
                    var go = new GameObject("Cane canvas part", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(transform, false);
                    var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.pivot = rectTransform.pivot;
                    var graphic = go.AddComponent<CaneGraphicPart>(); graphic.raycastTarget = false; parts.Add(graphic);
                }
                var target = parts[part++]; target.gameObject.SetActive(true); target.maskable = maskable;
                if (order) { if (target.transform.GetSiblingIndex() != sibling) target.transform.SetSiblingIndex(sibling); sibling++; }
                target.Buffer.Update(draws, player.Resources, player.UnitsPerPixel, slotMaterials: SlotMaterials, start: start, end: end);
                target.material = material; target.SetVerticesDirty(); target.canvasRenderer.SetColor(tint); start = end;
            }
        }
        public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
        {
            if (!base.Raycast(screenPoint, eventCamera) || player.Skeleton?.Frame == null || player.UnitsPerPixel == 0) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var point)) return false;
            var frame = player.Skeleton.Frame;
            for (int i = frame.Draws.Count - 1; i >= 0; i--) if (frame.ContainsPoint(frame.Draws[i].Slot, point.x / player.UnitsPerPixel, point.y / player.UnitsPerPixel)) return true;
            return false;
        }
        protected override void OnDisable()
        {
            if (player != null) player.Rebuilt -= Rebuilt;
            foreach (var part in parts) if (part != null) part.gameObject.SetActive(false);
            foreach (var item in objects) item.Hide(); PartCount = 0; base.OnDisable();
        }
        protected override void OnDestroy()
        { foreach (var part in parts) if (part != null) { if (Application.isPlaying) Destroy(part.gameObject); else DestroyImmediate(part.gameObject); } parts.Clear(); base.OnDestroy(); }
    }
}
