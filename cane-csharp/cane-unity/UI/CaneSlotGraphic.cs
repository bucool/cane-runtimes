using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cane.Unity
{
    /// <summary>Places Canvas content before/after a slot, retaining native UI
    /// layout/materials while inheriting the bone affine, opacity and clipping.</summary>
    [RequireComponent(typeof(CaneGraphic))]
    public sealed class CaneSlotGraphic : MonoBehaviour
    {
        public string Slot = "";
        public RectTransform Content;
        public bool Before, FollowAttachment = true;
        private CaneGraphic graphic;
        private RectTransform attached, left, right, previousRect;
        private Transform previousParent;
        private int previousSibling;
        private readonly SlotObjectGeometry geometry = new SlotObjectGeometry();
        private readonly List<(Graphic Graphic, Material Original)> targets = new List<(Graphic, Material)>();
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private static readonly Dictionary<RectTransform, CaneSlotGraphic> owners = new Dictionary<RectTransform, CaneSlotGraphic>();
        private void OnEnable() { graphic = GetComponent<CaneGraphic>(); Rebind(); }
        private void OnDisable() { if (graphic != null) graphic.Unregister(this); Release(); }
        public void Rebind()
        {
            if (graphic == null) return;
            graphic.Unregister(this); Release();
            if (!isActiveAndEnabled || Content == null || transform.IsChildOf(Content)) return;
            if (owners.TryGetValue(Content, out var owner)) owner.OnDisable();
            attached = Content; previousParent = Content.parent; previousRect = previousParent as RectTransform; previousSibling = Content.GetSiblingIndex(); owners.Add(attached, this);
            left = new GameObject("Cane UI slot", typeof(RectTransform), typeof(CaneSlotGraphicRaycast)) { hideFlags = HideFlags.DontSave, layer = gameObject.layer }.GetComponent<RectTransform>();
            left.SetParent(transform, false); left.sizeDelta = Vector2.zero; left.anchorMin = left.anchorMax = graphic.rectTransform.pivot;
            left.GetComponent<CaneSlotGraphicRaycast>().Owner = this;
            right = new GameObject("Cane UI slot basis", typeof(RectTransform)) { hideFlags = HideFlags.DontSave, layer = gameObject.layer }.GetComponent<RectTransform>(); right.SetParent(left, false);
            UpdateParentRect(); Content.SetParent(right, false);
            var children = new List<Graphic>(); Content.GetComponentsInChildren(true, children);
            foreach (var child in children)
            {
                var original = child.material; bool native = original.shader.name == "UI/Default";
                if (!native && !original.HasProperty(SlotObjectGeometry.AlphaId)) continue;
                targets.Add((child, original));
                if (!materials.TryGetValue(original, out var material))
                {
                    material = new Material(original) { hideFlags = HideFlags.DontSave };
                    if (native) material.shader = Resources.Load<Shader>("Cane/SlotUI");
                    material.EnableKeyword("CANE_SLOT_OBJECT"); materials.Add(original, material);
                }
                child.material = material;
            }
            graphic.Register(this); Hide();
        }
        private void UpdateParentRect()
        {
            right.sizeDelta = previousRect == null ? Vector2.zero : previousRect.rect.size;
            right.pivot = previousRect == null ? new Vector2(.5f, .5f) : previousRect.pivot;
        }
        internal void Hide() { if (left != null) left.gameObject.SetActive(false); }
        internal void Upload(CanePlayer player, int slot, int sibling)
        {
            if (attached == null) { Hide(); return; }
            var frame = player.Skeleton.Frame;
            bool visible = frame.Active[slot] != 0 && (!FollowAttachment || frame.Slots[slot].Attachment >= 0);
            if (left.GetSiblingIndex() != sibling) left.SetSiblingIndex(sibling);
            left.gameObject.SetActive(visible); if (!visible) return;
            UpdateParentRect(); left.anchorMin = left.anchorMax = graphic.rectTransform.pivot;
            SlotObjectGeometry.Follow(left, right, player.Skeleton.World, frame.Data.Slots[slot].Bone * 6, player.UnitsPerPixel);
            geometry.Update(player, slot, Before, graphic.color.a * graphic.canvasRenderer.GetColor().a);
            var canvas = graphic.canvas == null ? null : graphic.canvas.rootCanvas;
            var canvasSpace = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? canvas.transform : null;
            foreach (var target in targets) if (target.Graphic != null)
            {
                geometry.Apply(target.Graphic.material, canvasSpace);
                // MaskableGraphic may use a separate stencil material. Update the
                // final material too, so native masks and the slot mask intersect.
                geometry.Apply(target.Graphic.materialForRendering, canvasSpace);
            }
        }
        internal bool Raycast(Vector2 screen, Camera camera) => left != null && left.gameObject.activeInHierarchy &&
            RectTransformUtility.ScreenPointToWorldPointInRectangle(graphic.rectTransform, screen, camera, out var world) && geometry.ContainsPoint(world);
        private void Release()
        {
            foreach (var target in targets) if (target.Graphic != null) target.Graphic.material = target.Original;
            targets.Clear(); foreach (var material in materials.Values) CaneResources.Destroy(material); materials.Clear(); geometry.Dispose();
            if (!ReferenceEquals(attached, null)) owners.Remove(attached);
            if (attached != null) { attached.SetParent(previousParent, false); attached.SetSiblingIndex(previousSibling); }
            if (left != null) CaneResources.Destroy(left.gameObject);
            attached = left = right = previousRect = null;
        }
    }

    [AddComponentMenu("")]
    internal sealed class CaneSlotGraphicRaycast : MonoBehaviour, ICanvasRaycastFilter
    {
        internal CaneSlotGraphic Owner;
        public bool IsRaycastLocationValid(Vector2 point, Camera camera) => Owner != null && Owner.Raycast(point, camera);
    }
}
