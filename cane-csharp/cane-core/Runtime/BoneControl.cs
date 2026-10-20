using System;

namespace Cane
{
    /// <summary>Reusable external affine input, applied before constraints.
    /// Matrix is [a,b,c,d,x,y] in skeleton world or bone-parent local coordinates.</summary>
    public sealed class BoneControl
    {
        public bool World = true;
        public readonly double[] Matrix = { 1, 0, 0, 1, 0, 0 };
        public double PositionMix = 1, RotationMix = 1, ScaleMix = 1, ShearMix = 1;
        internal void Apply(Skeleton skeleton, int bone)
        {
            if (PositionMix == 0 && RotationMix == 0 && ScaleMix == 0 && ShearMix == 0) return;
            int at = bone * 8, parent = skeleton.Data.Parents[bone];
            var pose = skeleton.Working.AsSpan(at, 8); Span<double> local = stackalloc double[8];
            bool resolved = World
                ? Affine.BoneLocalForWorld(parent < 0 ? default : skeleton.World.AsSpan(parent * 6, 6), Matrix, pose, local, skeleton.ScaleX, skeleton.ScaleY, skeleton.X, skeleton.Y)
                : Affine.BoneLocalForWorld(default, Matrix, pose, local);
            if (!resolved) return;
            pose[0] += (local[0] - pose[0]) * PositionMix;
            pose[1] += (local[1] - pose[1]) * PositionMix;
            pose[2] += Affine.WrapAngle(local[2] - pose[2]) * RotationMix;
            pose[3] += (local[3] - pose[3]) * ScaleMix;
            pose[4] += (local[4] - pose[4]) * ScaleMix;
            pose[5] += Affine.WrapAngle(local[5] - pose[5]) * ShearMix;
            pose[6] += Affine.WrapAngle(local[6] - pose[6]) * ShearMix;
        }
    }
}
