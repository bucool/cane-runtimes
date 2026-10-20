using System;
using UnityEngine;

namespace Cane.Unity
{
    /// <summary>Supplies a Transform (including a physics body's Transform) as a
    /// bone input. Core resolves it against the current parent before constraints.</summary>
    [ExecuteAlways]
    public sealed class CaneBoneDriver : MonoBehaviour
    {
        public CanePlayer Player;
        public string Bone = "";
        public Transform Target;
        public bool WorldSpace = true, Position = true, Rotation = true, Scale = true, Shear = true;
        [Range(0, 1)] public float Mix = 1;
        private readonly BoneControl control = new BoneControl();
        private CanePlayer subscribed;
        private Skeleton bound;
        private int index = -1;
        private void OnEnable() => Rebind();
        private void OnDisable()
        {
            if (subscribed != null) { subscribed.Rebuilt -= Bind; subscribed.BeforeWorld -= Drive; }
            subscribed = null; Release();
        }
        private void Release()
        { if (bound != null && index >= 0 && bound.GetBoneControl(index) == control) bound.SetBoneControl(index, null); bound = null; index = -1; }
        public void Rebind()
        {
            OnDisable(); subscribed = isActiveAndEnabled ? Player : null;
            if (subscribed != null) { subscribed.Rebuilt += Bind; subscribed.BeforeWorld += Drive; Bind(subscribed); }
        }
        private void Bind(CanePlayer player)
        {
            Release(); if (player.Skeleton == null) return;
            index = Array.IndexOf(player.Resources.Asset.BoneIds, Bone);
            if (index < 0 && !player.Skeleton.Data.BoneNames.TryGetValue(Bone, out index)) { index = -1; return; }
            bound = player.Skeleton; bound.SetBoneControl(index, control); Drive(player);
        }
        private void Drive(CanePlayer player)
        {
            if (bound == null || bound.GetBoneControl(index) != control) return;
            var target = Target == null ? transform : Target;
            var matrix = WorldSpace ? player.transform.worldToLocalMatrix * target.localToWorldMatrix
                : Matrix4x4.TRS(target.localPosition, target.localRotation, target.localScale);
            var values = control.Matrix;
            values[0] = matrix.m00; values[1] = matrix.m10; values[2] = matrix.m01; values[3] = matrix.m11;
            if (player.UnitsPerPixel != 0) { values[4] = (double)matrix.m03 / player.UnitsPerPixel; values[5] = (double)matrix.m13 / player.UnitsPerPixel; }
            control.World = WorldSpace;
            control.PositionMix = Position && player.UnitsPerPixel != 0 ? Mix : 0;
            control.RotationMix = Rotation ? Mix : 0; control.ScaleMix = Scale ? Mix : 0; control.ShearMix = Shear ? Mix : 0;
        }
    }
}
