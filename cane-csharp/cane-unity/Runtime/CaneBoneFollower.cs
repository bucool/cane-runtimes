using System;
using UnityEngine;

namespace Cane.Unity
{
    [ExecuteAlways]
    public sealed class CaneBoneFollower : MonoBehaviour
    {
        public CanePlayer Player;
        public string Bone = "";
        public Vector2 Offset;
        public bool FollowRotation = true, FollowScale = true;
        private int index = -1;
        private CanePlayer subscribed;
        private void OnEnable() => Rebind();
        private void OnDisable() { if (subscribed != null) { subscribed.Rebuilt -= Bind; subscribed.AfterWorld -= Follow; } subscribed = null; }
        public void Rebind()
        {
            OnDisable(); index = -1; subscribed = isActiveAndEnabled ? Player : null;
            if (subscribed != null) { subscribed.Rebuilt += Bind; subscribed.AfterWorld += Follow; Bind(subscribed); }
        }
        private void Bind(CanePlayer player)
        {
            index = -1; if (player.Resources == null) return;
            index = Array.IndexOf(player.Resources.Asset.BoneIds, Bone);
            if (index < 0 && !player.Skeleton.Data.BoneNames.TryGetValue(Bone, out index)) index = -1;
        }
        internal static void AlignX(Transform target, Vector3 x, Vector3 y, Vector3 forward)
        {
            var parent = target.parent;
            var localX = parent == null ? x : parent.InverseTransformVector(x);
            var localY = parent == null ? y : parent.InverseTransformVector(y);
            if (parent != null) forward = parent.InverseTransformDirection(forward);
            var normal = Vector3.Cross(localX, localY);
            if (normal.sqrMagnitude == 0) normal = forward;
            else if (Vector3.Dot(normal, forward) < 0) normal = -normal;
            if (localX.sqrMagnitude > 0) target.localRotation = Quaternion.LookRotation(normal, Vector3.Cross(normal, localX));
        }
        private void Follow(CanePlayer player)
        {
            if (index < 0) return;
            var world = player.Skeleton.World; int p = index * 6;
            var position = player.Skeleton.LocalToWorld(index, Offset.x, Offset.y);
            var matrix = player.transform.localToWorldMatrix;
            var x = matrix.MultiplyVector(new Vector3((float)world[p], (float)world[p + 1], 0));
            var y = matrix.MultiplyVector(new Vector3((float)world[p + 2], (float)world[p + 3], 0));
            transform.position = matrix.MultiplyPoint3x4(new Vector3((float)position.X * player.UnitsPerPixel, (float)position.Y * player.UnitsPerPixel, 0));
            var parent = transform.parent;
            if (FollowRotation) AlignX(transform, x, y, player.transform.forward);
            if (FollowScale)
            {
                // A Transform cannot represent shear; preserve axis lengths and handedness.
                var axisX = transform.localRotation * Vector3.right; var axisY = transform.localRotation * Vector3.up;
                if (parent != null) { axisX = parent.TransformVector(axisX); axisY = parent.TransformVector(axisY); }
                var scale = new Vector3(axisX.magnitude == 0 ? 0 : x.magnitude / axisX.magnitude, axisY.magnitude == 0 ? 0 : y.magnitude / axisY.magnitude, 1);
                if (Vector3.Dot(Vector3.Cross(x, y), Vector3.Cross(axisX, axisY)) < 0) scale.y = -scale.y;
                transform.localScale = scale;
            }
        }
    }
}
