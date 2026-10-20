using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Places a scene object before/after a slot. Child local transforms stay
    /// relative to its bone; compatible materials inherit slot opacity and clipping.</summary>
    [RequireComponent(typeof(CaneRenderSeparator))]
    public sealed class CaneSlotObject : MonoBehaviour
    {
        public string Slot = "";
        public Transform Content;
        public bool Before, FollowAttachment = true;
        private CaneRenderSeparator separator;
        private Transform attached, previousParent, left, right;
        private int previousSibling;
        private SortingGroup group;
        private readonly SlotObjectGeometry geometry = new SlotObjectGeometry();
        private readonly List<Target> targets = new List<Target>();
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private static readonly Dictionary<Transform, CaneSlotObject> owners = new Dictionary<Transform, CaneSlotObject>();
        private sealed class Target
        {
            public Renderer Renderer;
            public Material[] Original;
            public readonly MaterialPropertyBlock OriginalBlock = new MaterialPropertyBlock(), Block = new MaterialPropertyBlock();
        }
        private void OnEnable() { separator = GetComponent<CaneRenderSeparator>(); Rebind(); }
        private void OnDisable() { if (separator != null) separator.Unregister(this); Release(); }
        public void Rebind()
        {
            if (separator == null) return;
            separator.Unregister(this); Release();
            if (!isActiveAndEnabled || Content == null || transform.IsChildOf(Content)) return;
            if (owners.TryGetValue(Content, out var owner)) owner.OnDisable();
            attached = Content; previousParent = Content.parent; previousSibling = Content.GetSiblingIndex();
            owners.Add(attached, this);
            left = new GameObject("Cane slot", typeof(SortingGroup)) { hideFlags = HideFlags.DontSave, layer = gameObject.layer }.transform;
            left.SetParent(transform, false); group = left.GetComponent<SortingGroup>();
            right = new GameObject("Cane slot basis") { hideFlags = HideFlags.DontSave, layer = gameObject.layer }.transform; right.SetParent(left, false);
            Content.SetParent(right, false);
            var renderers = new List<Renderer>(); Content.GetComponentsInChildren(true, renderers);
            foreach (var renderer in renderers)
            {
                var target = new Target { Renderer = renderer, Original = renderer.sharedMaterials };
                renderer.GetPropertyBlock(target.OriginalBlock); renderer.GetPropertyBlock(target.Block); targets.Add(target);
                var replacements = (Material[])target.Original.Clone();
                for (int i = 0; i < replacements.Length; i++)
                {
                    var source = replacements[i]; if (source == null) continue;
                    bool sprite = renderer is SpriteRenderer && source.shader.name == "Sprites/Default";
                    if (!sprite && !source.HasProperty(SlotObjectGeometry.AlphaId)) continue;
                    if (!materials.TryGetValue(source, out var material))
                    {
                        material = new Material(source) { hideFlags = HideFlags.DontSave };
                        if (sprite) material.shader = Resources.Load<Shader>("Cane/SlotSprite");
                        material.EnableKeyword("CANE_SLOT_OBJECT"); materials.Add(source, material);
                    }
                    replacements[i] = material;
                }
                renderer.sharedMaterials = replacements;
            }
            separator.Register(this); Hide();
        }
        internal void Hide() { if (left != null) left.gameObject.SetActive(false); }
        internal void Upload(CanePlayer player, int slot, int layer, int order)
        {
            if (attached == null) { Hide(); return; }
            var frame = player.Skeleton.Frame;
            bool visible = frame.Active[slot] != 0 && (!FollowAttachment || frame.Slots[slot].Attachment >= 0);
            left.gameObject.SetActive(visible); if (!visible) return;
            group.sortingLayerID = layer; group.sortingOrder = order;
            SlotObjectGeometry.Follow(left, right, player.Skeleton.World, frame.Data.Slots[slot].Bone * 6, player.UnitsPerPixel);
            geometry.Update(player, slot, Before);
            foreach (var target in targets)
            {
                if (target.Renderer == null) continue;
                geometry.Apply(target.Block); target.Renderer.SetPropertyBlock(target.Block);
            }
        }
        private void Release()
        {
            foreach (var target in targets) if (target.Renderer != null)
            { target.Renderer.sharedMaterials = target.Original; target.Renderer.SetPropertyBlock(target.OriginalBlock); }
            targets.Clear(); foreach (var material in materials.Values) CaneResources.Destroy(material); materials.Clear();
            if (!ReferenceEquals(attached, null)) owners.Remove(attached);
            if (attached != null) { attached.SetParent(previousParent, false); attached.SetSiblingIndex(previousSibling); }
            if (left != null) CaneResources.Destroy(left.gameObject);
            attached = left = right = null; geometry.Dispose();
        }
    }
}
