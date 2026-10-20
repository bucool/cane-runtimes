using UnityEngine;

namespace Cane.Unity
{
    /// <summary>Follows the currently selected point attachment without resampling the skeleton.</summary>
    [ExecuteAlways]
    public sealed class CanePointFollower : MonoBehaviour
    {
        public CanePlayer Player;
        public string Slot = "";
        public bool FollowRotation = true;
        public bool HasPoint { get; private set; }
        private CanePlayer subscribed;
        private int index = -1;
        private void OnEnable() => Rebind();
        private void OnDisable()
        { if (subscribed != null) { subscribed.Rebuilt -= Bind; subscribed.AfterGeometry -= Follow; } subscribed = null; HasPoint = false; }
        public void Rebind()
        {
            OnDisable(); index = -1; subscribed = isActiveAndEnabled ? Player : null;
            if (subscribed != null) { subscribed.Rebuilt += Bind; subscribed.AfterGeometry += Follow; Bind(subscribed); }
        }
        private void Bind(CanePlayer player) { index = player.FindSlot(Slot); HasPoint = false; }
        private void Follow(CanePlayer player)
        {
            var point = index >= 0 ? player.Skeleton.Point(index) : null; HasPoint = point.HasValue;
            if (!HasPoint) return;
            var value = point.Value;
            transform.position = player.transform.TransformPoint(new Vector3((float)value.X * player.UnitsPerPixel, (float)value.Y * player.UnitsPerPixel, 0));
            if (FollowRotation)
            {
                float radians = (float)value.Rotation * Mathf.Deg2Rad;
                var direction = player.transform.TransformVector(new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0));
                CaneBoneFollower.AlignX(transform, direction, Vector3.Cross(player.transform.forward, direction), player.transform.forward);
            }
        }
    }
}
