using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Splits final draws after selected slots so other Unity renderers can be interleaved.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CaneRenderer), typeof(SortingGroup))]
    public sealed class CaneRenderSeparator : MonoBehaviour
    {
        public string[] Slots = Array.Empty<string>();
        public IReadOnlyList<MeshRenderer> Parts => parts;
        public int PartCount { get; private set; }
        private readonly List<MeshRenderer> parts = new List<MeshRenderer>();
        private readonly List<MeshBuffer> buffers = new List<MeshBuffer>();
        private CaneRenderer renderer;
        private CanePlayer player;
        private MeshRenderer source;
        private bool[] separators = Array.Empty<bool>();
        private int[] orders = Array.Empty<int>();
        private bool dirty = true;
        private readonly List<CaneSlotObject> objects = new List<CaneSlotObject>();
        private CaneSlotObject[] slotObjects = Array.Empty<CaneSlotObject>();

        private void OnEnable()
        {
            renderer = GetComponent<CaneRenderer>(); player = GetComponent<CanePlayer>(); source = GetComponent<MeshRenderer>();
            renderer.Separator = this; player.Rebuilt += Rebuilt; dirty = true;
        }
        private void OnValidate() => dirty = true;
        private void Rebuilt(CanePlayer _) => dirty = true;
        public void SetSlots(params string[] slots) { Slots = slots; dirty = true; }
        internal void Register(CaneSlotObject value) { objects.Add(value); dirty = true; }
        internal void Unregister(CaneSlotObject value) { objects.Remove(value); dirty = true; }
        /// <summary>Sorting order immediately after this slot; update external objects after renderer.Uploaded.</summary>
        public int OrderAfterSlot(int slot) => orders[slot];

        internal void Upload(AttachmentFrame frame)
        {
            if (dirty)
            {
                separators = new bool[frame.Slots.Length]; orders = new int[frame.Slots.Length]; slotObjects = new CaneSlotObject[frame.Slots.Length];
                foreach (var name in Slots)
                {
                    int slot = player.FindSlot(name);
                    if (slot >= 0) separators[slot] = true;
                }
                foreach (var item in objects)
                {
                    item.Hide(); int slot = player.FindSlot(item.Slot);
                    if (slot >= 0) slotObjects[slot] = item;
                }
                dirty = false;
            }
            int at = 0, start = 0, part = 0;
            foreach (int slot in frame.AppliedOrder)
            {
                var item = slotObjects[slot];
                if (item != null && item.Before)
                {
                    UploadPart(frame, part++, start, at); start = at;
                    if (renderer.enabled) item.Upload(player, slot, source.sortingLayerID, part * 2 - 1); else item.Hide();
                }
                if (at < frame.Draws.Count && frame.Draws[at].Slot == slot) at++;
                orders[slot] = part * 2 + 1;
                if (!separators[slot] && (item == null || item.Before)) continue;
                UploadPart(frame, part++, start, at); start = at;
                if (item != null && !item.Before) { if (renderer.enabled) item.Upload(player, slot, source.sortingLayerID, part * 2 - 1); else item.Hide(); }
            }
            UploadPart(frame, part++, start, at);
            for (int i = part; i < PartCount; i++) parts[i].enabled = false;
            PartCount = part;
        }
        private void UploadPart(AttachmentFrame frame, int index, int start, int end)
        {
            if (index == parts.Count)
            {
                var go = new GameObject("Cane render part", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
                go.transform.SetParent(transform, false); var buffer = new MeshBuffer(); go.GetComponent<MeshFilter>().sharedMesh = buffer.Mesh;
                buffers.Add(buffer); parts.Add(go.GetComponent<MeshRenderer>());
            }
            var part = parts[index]; var mesh = buffers[index];
            if (mesh.Update(frame.Draws, player.Resources, player.UnitsPerPixel, renderer.ZSpacing, renderer.SlotMaterials, start, end, renderer.MaskInteraction, renderer.AddNormals, renderer.SolveTangents)) part.SetSharedMaterials(mesh.Materials);
            part.enabled = renderer.enabled; part.gameObject.layer = gameObject.layer;
            part.sortingLayerID = source.sortingLayerID; part.sortingOrder = index * 2;
            part.shadowCastingMode = source.shadowCastingMode; part.receiveShadows = source.receiveShadows;
            part.lightProbeUsage = source.lightProbeUsage; part.reflectionProbeUsage = source.reflectionProbeUsage; part.probeAnchor = source.probeAnchor;
        }
        internal void Hide() { foreach (var part in parts) if (part != null) part.enabled = false; foreach (var item in objects) item.Hide(); }
        private void OnDisable()
        {
            if (player != null) player.Rebuilt -= Rebuilt;
            if (renderer != null) renderer.Separator = null;
            Hide(); if (renderer != null && renderer.isActiveAndEnabled) renderer.Upload();
        }
        private void OnDestroy()
        {
            foreach (var buffer in buffers) buffer.Dispose();
            foreach (var part in parts) if (part != null) CaneResources.Destroy(part.gameObject);
        }
    }
}
