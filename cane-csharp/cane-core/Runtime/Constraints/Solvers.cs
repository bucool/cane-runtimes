using System;
using Cane.Animation;
using Cane.Format;

namespace Cane.Constraints
{
    internal static class Solvers
    {
        internal static float Degrees(float value) { float r = value % 360; if (r < 0) r += 360; return r > 180 ? r - 360 : r; }
        internal static float Positive(float value) { float r = value % 360; return r < 0 ? r + 360 : r; }
        internal static float Angle(Affine m) => Numeric.Atan2(m.B, m.A) * Numeric.RadToDeg;
        internal static float YAngle(Affine m) => Numeric.Atan2(m.D, m.C) * Numeric.RadToDeg;
        internal static float Sx(Affine m) => Numeric.Hypot(m.A, m.B);
        internal static float Sy(Affine m) => Numeric.Hypot(m.C, m.D);
        internal static float ShearY(Affine m) => Degrees(Numeric.Atan2(-m.C, m.D) * Numeric.RadToDeg - Angle(m));
        internal static void IncludeMax(ref float? maximum, float value) => maximum = maximum.HasValue ? Math.Max(maximum.Value, value) : value;
        internal static bool Inverse(Affine m, out Affine inverse)
        { if (Math.Abs(m.Determinant) <= Numeric.Epsilon) { inverse = default; return false; } return m.TryInverse(out inverse); }
        internal static Affine Parent(Pose pose, int bone, Affine root) => pose.Data.BoneData[bone].Parent < 0 ? root : pose.World[pose.Data.BoneData[bone].Parent];
        internal static void Solve(Pose pose, Affine root, SamplingOptions sampling, PhysicsStore physics, float delta, PhysicsEnvironment environment, float rootScaleX, float rootScaleY, PhysicsBudget budget)
        {
            foreach (Json constraint in pose.Constraints)
            {
                if (!pose.IsActive(constraint.S("id"), "constraintIds")) continue;
                string? target = constraint["targetBoneId"].StringOrNull;
                if (target != null && !pose.IsActive(target, "boneIds")) continue;
                try
                {
                switch (constraint.S("type"))
                {
                    case "ik": Ik.Solve(pose, constraint, root); break;
                    case "transform": Transform.Solve(pose, constraint, root); break;
                    case "path":
                        PathConstraint.Solve(pose, constraint, root);
                        break;
                    case "physics": Physics.Solve(pose, constraint, root, physics, delta, environment, rootScaleX, rootScaleY, budget); break;
                    case "slider": if (constraint.F("mix", 1) != 0) Slider.Solve(pose, Pose.CopyConstraintState(constraint), root, sampling); break;
                }
                }
                catch (RuntimeException error) when (error.EntityId == null && (constraint.S("type") == "ik" || constraint.S("type") == "path" || constraint.S("type") == "transform"))
                {
                    throw new RuntimeException(error.Code, error.Operation, error.Message, error.Field, constraint.S("id"));
                }
            }
        }
        internal static void WriteLocal(Pose pose, int bone, BoneLocal local, Affine root)
        {
            pose.Locals[bone] = local; int parent = pose.Data.BoneData[bone].Parent;
            pose.World[bone] = parent < 0 ? root * Affine.FromLocal(local) : Affine.Child(pose.World[parent], local, pose.Modes[bone]);
            RefreshDescendants(pose, bone);
        }
        internal static void WriteWorld(Pose pose, int bone, Affine matrix, Affine root)
        { SetWorld(pose, bone, matrix, root); RefreshDescendants(pose, bone); }
        internal static void SetWorld(Pose pose, int bone, Affine matrix, Affine root)
        {
            if (!matrix.IsFinite) throw new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Constraint produced a non-finite matrix.", "bones", pose.Data.BoneData[bone].Id);
            pose.World[bone] = matrix;
            if (Reconstruct(pose.Locals[bone], matrix, Parent(pose, bone, root), pose.Data.BoneData[bone].Parent < 0 ? "inheritAll" : pose.Modes[bone], out BoneLocal local)) pose.Locals[bone] = local;
        }
        internal static void RefreshDescendants(Pose pose, int start)
        {
            var changed = new bool[pose.World.Length]; changed[start] = true;
            for (int i = start + 1; i < changed.Length; i++)
            {
                int parent = pose.Data.BoneData[i].Parent;
                if (parent >= 0 && changed[parent]) { changed[i] = true; pose.World[i] = Affine.Child(pose.World[parent], pose.Locals[i], pose.Modes[i]); }
            }
            for (int i = start; i < changed.Length; i++) if (changed[i] && !pose.World[i].IsFinite)
                throw new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Constraint hierarchy overflowed.", "bones", pose.Data.BoneData[i].Id);
        }
        private static bool Reconstruct(BoneLocal previous, Affine world, Affine parent, string mode, out BoneLocal local)
        {
            local = previous;
            try { local = TransformGeometry.Recover(world, parent, mode, previous); return true; }
            catch (RuntimeException) { return false; }
        }
        internal static Affine Rotate(Affine m, float angle)
        {
            float co = Numeric.Cos(angle * Numeric.DegToRad), si = Numeric.Sin(angle * Numeric.DegToRad);
            return new Affine(co * m.A - si * m.B, si * m.A + co * m.B, co * m.C - si * m.D, si * m.C + co * m.D, m.Tx, m.Ty);
        }
    }
}
