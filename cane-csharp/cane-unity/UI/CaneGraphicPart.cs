using UnityEngine;
using UnityEngine.UI;

namespace Cane.Unity
{
    [AddComponentMenu(""), RequireComponent(typeof(CanvasRenderer))]
    public sealed class CaneGraphicPart : MaskableGraphic
    {
        public MeshBuffer Buffer { get; private set; }
        public override Texture mainTexture => material == null ? Texture2D.whiteTexture : material.mainTexture;
        protected override void Awake() { base.Awake(); Buffer = new MeshBuffer(canvas: true); }
        protected override void UpdateGeometry() { if (Buffer != null) canvasRenderer.SetMesh(Buffer.Mesh); }
        protected override void OnDestroy() { Buffer?.Dispose(); base.OnDestroy(); }
    }
}
