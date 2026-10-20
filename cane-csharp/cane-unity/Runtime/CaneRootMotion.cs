using System;
using UnityEngine;

namespace Cane.Unity
{
    [RequireComponent(typeof(CanePlayer)), DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class CaneRootMotion : MonoBehaviour
    {
        public string Bone = "";
        public int[] Tracks = { 0 };
        public bool X = true, Y, AutoApply = true;
        public Vector2 Scale = Vector2.one;
#if CANE_PHYSICS_2D
        public Rigidbody2D Body2D;
#endif
#if CANE_PHYSICS_3D
        public Rigidbody Body;
#endif
        public RootMotion Motion { get; private set; }
        public Vector3 DeltaPosition { get; private set; }
        public event Action<CaneRootMotion> Moved;
        private CanePlayer player;
        private AnimationState bound;
        private Vector3 pending;

        private void OnEnable()
        {
            player = GetComponent<CanePlayer>(); player.Rebuilt += Bind; player.MotionDriver = this; Rebind();
        }
        private void OnDisable()
        {
            if (player != null) { player.Rebuilt -= Bind; if (player.MotionDriver == this) player.MotionDriver = null; }
            Release(); player = null;
        }
        private void Release()
        {
            if (bound != null && bound.Motion == Motion) bound.Motion = null;
            bound = null; Motion = null; pending = DeltaPosition = Vector3.zero;
        }
        public void Rebind() { if (player != null) Bind(player); }
        private void Bind(CanePlayer source)
        {
            Release(); if (!isActiveAndEnabled || source.Skeleton == null) return;
            int bone = Bone.Length == 0 ? 0 : Array.IndexOf(source.Resources.Asset.BoneIds, Bone);
            if (bone < 0 && !source.Skeleton.Data.BoneNames.TryGetValue(Bone, out bone)) return;
            if (bone >= source.Skeleton.Data.Parents.Length) return;
            Motion = new RootMotion(source.Skeleton.Data, bone) { Tracks = Tracks };
            bound = source.State; bound.Motion = Motion;
        }
        internal void Restore(CanePlayer source)
        {
            if (Motion == null || source.State.Motion != Motion) return;
            int at = Motion.Bone * 8;
            if (X) source.Skeleton.Pose[at] = Motion.PoseX;
            if (Y) source.Skeleton.Pose[at + 1] = Motion.PoseY;
        }
        internal void Move(CanePlayer source)
        {
            if (Motion == null || source.State.Motion != Motion) { DeltaPosition = Vector3.zero; return; }
            DeltaPosition = ToWorld(X ? Motion.DeltaX * Scale.x : 0, Y ? Motion.DeltaY * Scale.y : 0);
            if (AutoApply)
            {
                bool body = false;
#if CANE_PHYSICS_2D
                body = Body2D != null;
#endif
#if CANE_PHYSICS_3D
                body |= Body != null;
#endif
                if (body) pending += DeltaPosition; else source.transform.position += DeltaPosition;
            }
            Moved?.Invoke(this);
        }
        private Vector3 ToWorld(double x, double y)
        {
            var skeleton = player.Skeleton;
            int parent = skeleton.Data.Parents[Motion.Bone];
            if (parent < 0) { x *= skeleton.ScaleX; y *= skeleton.ScaleY; }
            else
            {
                int at = parent * 6; var world = skeleton.World; double nextX = world[at] * x + world[at + 2] * y;
                y = world[at + 1] * x + world[at + 3] * y; x = nextX;
            }
            return player.transform.TransformVector(new Vector3((float)x * player.UnitsPerPixel, (float)y * player.UnitsPerPixel, 0));
        }
        /// <summary>Scale one entry's remaining translation to a world-space distance.
        /// Uses the current parent basis, without predicting future constraints or mixes.
        /// Returns false without changing Scale if the selected axes cannot reach it.</summary>
        public bool TryFitToDistance(TrackEntry entry, double toTime, Vector3 worldDistance)
        {
            if (Motion == null || player.State.Motion != Motion || player.UnitsPerPixel == 0 || player.transform.localToWorldMatrix.determinant == 0) return false;
            var skeleton = player.Skeleton; int parent = skeleton.Data.Parents[Motion.Bone];
            Span<double> basis = stackalloc double[6] { skeleton.ScaleX, 0, 0, skeleton.ScaleY, 0, 0 };
            if (parent >= 0) skeleton.World.AsSpan(parent * 6, 4).CopyTo(basis);
            var local = player.transform.InverseTransformVector(worldDistance);
            if (!Affine.WorldToLocal(basis, 0, (double)local.x / player.UnitsPerPixel, (double)local.y / player.UnitsPerPixel, out double x, out double y)) return false;
            var delta = Motion.Translation(entry, entry.Time, toTime); var scale = Scale;
            if (X && delta.X != 0) scale.x = (float)(x / delta.X);
            if (Y && delta.Y != 0) scale.y = (float)(y / delta.Y);
            var fitted = ToWorld(X ? delta.X * scale.x : 0, Y ? delta.Y * scale.y : 0);
            if (!((fitted - worldDistance).sqrMagnitude <= 1e-10f * Mathf.Max(1, worldDistance.sqrMagnitude))) return false;
            Scale = scale; return true;
        }
        private void FixedUpdate() => ApplyPendingMotion();
        /// <summary>Apply accumulated movement before a manually simulated physics step.
        /// Normal Unity physics calls this from FixedUpdate.</summary>
        public void ApplyPendingMotion()
        {
            var movement = pending; pending = Vector3.zero;
            if (movement == Vector3.zero) return;
#if CANE_PHYSICS_2D
            if (Body2D != null) { Body2D.MovePosition(Body2D.position + (Vector2)movement); return; }
#endif
#if CANE_PHYSICS_3D
            if (Body != null) Body.MovePosition(Body.position + movement);
#endif
        }
    }
}
