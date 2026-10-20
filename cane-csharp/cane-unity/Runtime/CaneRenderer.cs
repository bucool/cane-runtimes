using System;
using UnityEngine;

namespace Cane.Unity
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CanePlayer), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CaneRenderer : MonoBehaviour
    {
        public float ZSpacing;
        public Material[] SlotMaterials = Array.Empty<Material>();
        public SpriteMaskInteraction MaskInteraction;
        public bool Manual;
        public bool AddNormals, SolveTangents;
        public MeshBuffer Buffer { get; private set; }
        public event Action<CaneRenderer> Uploaded;
        private CanePlayer player;
        private MeshRenderer target;
        internal CaneRenderSeparator Separator;
        private void Awake()
        {
            player = GetComponent<CanePlayer>(); target = GetComponent<MeshRenderer>();
            Buffer = new MeshBuffer(); GetComponent<MeshFilter>().sharedMesh = Buffer.Mesh;
        }
        private void OnEnable() { if (target != null) target.enabled = true; }
        private void OnDisable() { if (target != null) target.enabled = false; Separator?.Hide(); }
        private void LateUpdate() { if (!Manual) Upload(); }
        public void Upload()
        {
            var frame = player.Skeleton?.Frame;
            if (frame == null) { target.enabled = false; Separator?.Hide(); return; }
            target.enabled = enabled && Separator == null;
            if (Separator != null) Separator.Upload(frame);
            else if (Buffer.Update(frame.Draws, player.Resources, player.UnitsPerPixel, ZSpacing, SlotMaterials, mask: MaskInteraction, addNormals: AddNormals, solveTangents: SolveTangents)) target.SetSharedMaterials(Buffer.Materials);
            Uploaded?.Invoke(this);
        }
        private void OnDestroy() => Buffer?.Dispose();
    }
}
