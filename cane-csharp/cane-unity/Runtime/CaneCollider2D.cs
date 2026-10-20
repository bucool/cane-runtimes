#if CANE_PHYSICS_2D
using System.Collections.Generic;
using UnityEngine;

namespace Cane.Unity
{
    /// <summary>Projects the selected bounding attachment into a native 2D collider.</summary>
    [ExecuteAlways, RequireComponent(typeof(PolygonCollider2D))]
    public sealed class CaneCollider2D : MonoBehaviour
    {
        public CanePlayer Player;
        public string Slot = "";
        private CanePlayer subscribed;
        private PolygonCollider2D target;
        private int index = -1;
        private readonly List<Vector2> points = new List<Vector2>();
        private void OnEnable() { target = GetComponent<PolygonCollider2D>(); Rebind(); }
        private void OnDisable()
        { if (subscribed != null) { subscribed.Rebuilt -= Bind; subscribed.AfterGeometry -= Follow; } subscribed = null; if (target != null) target.enabled = false; }
        public void Rebind()
        {
            OnDisable(); index = -1; subscribed = isActiveAndEnabled ? Player : null;
            if (subscribed != null) { subscribed.Rebuilt += Bind; subscribed.AfterGeometry += Follow; Bind(subscribed); }
        }
        private void Bind(CanePlayer player) { index = player.FindSlot(Slot); if (index < 0) target.enabled = false; }
        private void Follow(CanePlayer player)
        {
            var frame = player.Skeleton.Frame; var state = index < 0 ? null : frame.Slots[index];
            target.enabled = state != null && state.Attachment >= 0 && frame.Attachments[state.Attachment] is BoundingAttachment && state.Vertices.Length >= 6;
            if (!target.enabled) return;
            var matrix = transform.worldToLocalMatrix * player.transform.localToWorldMatrix;
            var vertices = state.Vertices; points.Clear();
            for (int i = 0; i < vertices.Length; i += 2)
                points.Add((Vector2)matrix.MultiplyPoint3x4(new Vector3(vertices[i] * player.UnitsPerPixel, vertices[i + 1] * player.UnitsPerPixel, 0)) - target.offset);
            target.pathCount = 1; target.SetPath(0, points);
        }
    }
}
#endif
