using System;
using System.Collections.Generic;
using Cane.Animation;
using Cane.Format;

namespace Cane.Constraints
{
    internal static class Transform
    {
        private static readonly string[] Properties = { "rotate", "x", "y", "scaleX", "scaleY", "shearY" };
        private static readonly string[] Mixes = { "mixRotate", "mixX", "mixY", "mixScaleX", "mixScaleY", "mixShearY" };
        private static readonly string[] Offsets = { "rotation", "x", "y", "scaleX", "scaleY", "shearY" };
        internal static void Solve(Pose pose, Json c, Affine root)
        {
            int remaining = 1000000;
            void Charge(int n) { if (n > remaining) throw new RuntimeException(RuntimeErrorCode.ResourceLimit, "apply", "Transform work budget is exhausted."); remaining -= n; }
            int target = pose.Data.BoneIndex[c.S("targetBoneId")];
            Affine sourceWorld = pose.World[target]; Json mapping = c["mapping"];
            bool relative = c.B("relative"), localSource = mapping.IsNull ? c.B("local") : mapping.B("localSource"), localTarget = mapping.IsNull ? c.B("local") : mapping.B("localTarget");
            double[] source = localSource ? TransformGeometry.Local(pose.Locals[target]) : TransformGeometry.World(sourceWorld);
            for (int i = 0; i < 6; i++) source[i] += c.F(Offsets[i]);
            if (!localSource) { source[1] = (double)sourceWorld.A * c.F("x") + (double)sourceWorld.C * c.F("y") + sourceWorld.Tx; source[2] = (double)sourceWorld.B * c.F("x") + (double)sourceWorld.D * c.F("y") + sourceWorld.Ty; }
            TransformGeometry.Check(source); TransformGeometry.CheckWorld(root);
            var mixes = new float[6]; var full = new float[6]; var routes = new List<TransformRoute>();
            for (int i = 0; i < 6; i++) { mixes[i] = c.F(Mixes[i], 1); full[i] = mixes[i] == 0 ? 0 : 1; TransformGeometry.Check(mixes[i]); }
            if (mapping.IsNull) for (int i = 0; i < 6; i++) { Charge(1); routes.Add(new TransformRoute(i, i, 0, 0, 1, null)); }
            else foreach (Json from in mapping["properties"].Items) foreach (Json to in from["targets"].Items)
            { Charge(1); routes.Add(new TransformRoute(Array.IndexOf(Properties, from.S("property")), Array.IndexOf(Properties, to.S("property")), from.F("offset"), to.F("offset"), to.F("scale", 1), mapping.B("clamp") ? (double?)to.F("max", 1) : null)); }
            TransformGeometry.Evaluate(source, new double[] { 0, 0, 0, 1, 1, 0 }, routes, new float[6], relative);
            var localWrites = new Dictionary<int, BoneLocal>(); var worldWrites = new Dictionary<int, Affine>();
            var children = new List<int>[pose.World.Length]; for (int i = 0; i < children.Length; i++) children[i] = new List<int>();
            for (int i = 0; i < children.Length; i++) { int parent = pose.Data.BoneData[i].Parent; if (parent >= 0) children[parent].Add(i); }
            Affine WorldAt(int bone) => worldWrites.TryGetValue(bone, out Affine m) ? m : pose.World[bone];
            BoneLocal LocalAt(int bone) => localWrites.TryGetValue(bone, out BoneLocal p) ? p : pose.Locals[bone];
            Affine Parent(int bone) => pose.Data.BoneData[bone].Parent < 0 ? root : WorldAt(pose.Data.BoneData[bone].Parent);
            string Mode(int bone) => pose.Data.BoneData[bone].Parent < 0 ? "inheritAll" : pose.Modes[bone];
            (Affine, BoneLocal?) Candidate(BoneLocal local, Affine world, Affine parent, string mode, float[] amounts)
            {
                double[] current = localTarget ? TransformGeometry.Local(local) : TransformGeometry.World(world);
                double[] next = TransformGeometry.Evaluate(source, current, routes, amounts, relative);
                if (TransformGeometry.Same(current, next)) return (world, local);
                if (!localTarget) return (TransformGeometry.ToWorld(next), null);
                BoneLocal p = TransformGeometry.ToLocal(next, local); Affine m = Affine.Child(parent, p, mode); TransformGeometry.CheckWorld(m); return (m, p);
            }
            float? translationResidual = null, rotationResidual = null, scaleResidual = null, shearResidual = null; int driven = 0;
            void Include(ref float? maximum, double value) { float narrowed = (float)value; if (Numeric.Finite(narrowed)) maximum = Math.Max(maximum ?? 0, narrowed); }
            foreach (Json id in c["boneIds"].Items)
            {
                int bone = pose.Data.BoneIndex[id.String]; if (bone == target) continue;
                Charge(1 + 2 * routes.Count);
                BoneLocal current = LocalAt(bone); Affine world = WorldAt(bone), parent = Parent(bone); string mode = Mode(bone);
                var (nextWorld, projectedLocal) = Candidate(current, world, parent, mode, mixes);
                BoneLocal nextLocal = projectedLocal ?? (TransformGeometry.Same(nextWorld, world) ? current : TransformGeometry.Recover(nextWorld, parent, mode, current));
                driven++;
                try
                {
                    Affine ideal = Candidate(current, world, parent, mode, full).Item1;
                    if (mixes[1] != 0 || mixes[2] != 0) { double dx = mixes[1] == 0 ? 0 : (double)nextWorld.Tx - ideal.Tx, dy = mixes[2] == 0 ? 0 : (double)nextWorld.Ty - ideal.Ty; Include(ref translationResidual, Math.Sqrt(dx * dx + dy * dy)); }
                    if (mixes[0] != 0) Include(ref rotationResidual, Math.Abs(TransformGeometry.Wrap((double)Solvers.Angle(ideal) - Solvers.Angle(nextWorld))));
                    if (mixes[3] != 0 || mixes[4] != 0) Include(ref scaleResidual, Math.Max(mixes[3] == 0 ? 0 : Math.Abs((double)Solvers.Sx(nextWorld) - Solvers.Sx(ideal)), mixes[4] == 0 ? 0 : Math.Abs((double)Solvers.Sy(nextWorld) - Solvers.Sy(ideal))));
                    if (mixes[5] != 0) Include(ref shearResidual, Math.Abs(TransformGeometry.Wrap((double)(float)TransformGeometry.World(ideal)[5] - (float)TransformGeometry.World(nextWorld)[5])));
                }
                catch (RuntimeException) { }
                if (TransformGeometry.Same(nextWorld, world) && nextLocal.Equals(current)) continue;
                localWrites[bone] = nextLocal; worldWrites[bone] = nextWorld;
                var queue = new List<int>(children[bone]);
                for (int cursor = 0; cursor < queue.Count; cursor++)
                {
                    Charge(1); int child = queue[cursor]; Affine matrix = Affine.Child(Parent(child), LocalAt(child), Mode(child)); TransformGeometry.CheckWorld(matrix);
                    worldWrites[child] = matrix; queue.AddRange(children[child]);
                }
            }
            foreach (var pair in localWrites) pose.Locals[pair.Key] = pair.Value;
            foreach (var pair in worldWrites) pose.World[pair.Key] = pair.Value;
            pose.Diagnostics[c.S("id")] = new TransformConstraintDiagnostic(driven, mixes, translationResidual, rotationResidual, scaleResidual, shearResidual);
        }
        internal static TransformConstraintOffsets Match(Pose pose, Json c)
        {
            const string op = "queryMatchedTransformConstraintOffsets"; string id = c.S("id"); Json mapping = c["mapping"];
            if (!mapping.IsNull && !Canonical(mapping)) throw new RuntimeException(RuntimeErrorCode.InvalidState, op, "Match requires canonical property routing.", "mapping", id);
            bool localSource = mapping.IsNull ? c.B("local") : mapping.B("localSource"), localTarget = mapping.IsNull ? c.B("local") : mapping.B("localTarget");
            int sourceBone = pose.Data.BoneIndex[c.S("targetBoneId")], constrained = pose.Data.BoneIndex[c["boneIds"][0].String];
            bool relative = c.B("relative"); Affine sourceWorld = pose.World[sourceBone];
            double[] source = localSource ? TransformGeometry.Local(pose.Locals[sourceBone]) : TransformGeometry.World(sourceWorld);
            double[] desired = relative ? new double[] { 0, 0, 0, 1, 1, 0 } : localTarget ? TransformGeometry.Local(pose.Locals[constrained]) : TransformGeometry.World(pose.World[constrained]);
            var offsets = new double[6]; for (int i = 0; i < 6; i++) offsets[i] = desired[i] - source[i];
            if (!relative) { offsets[0] = TransformGeometry.Wrap(offsets[0]); offsets[5] = TransformGeometry.Wrap(offsets[5]); }
            if (!localSource) { DoublePoint point = TransformGeometry.InversePoint(sourceWorld, new DoublePoint(desired[1], desired[2])); offsets[1] = point.X; offsets[2] = point.Y; }
            var result = new float[6]; for (int i = 0; i < 6; i++) result[i] = TransformGeometry.Narrow(offsets[i]);
            return new TransformConstraintOffsets(id, result[0], result[1], result[2], result[3], result[4], result[5]);
        }
        private static bool Canonical(Json mapping)
        {
            if (mapping.B("clamp") || mapping["properties"].Items.Count != 6) return false;
            var seen = new bool[6];
            foreach (Json from in mapping["properties"].Items)
            {
                int index = Array.IndexOf(Properties, from.S("property"));
                if (index < 0 || seen[index] || from.F("offset") != 0 || from["targets"].Items.Count != 1) return false;
                Json to = from["targets"][0]; if (to.S("property") != from.S("property") || to.F("offset") != 0 || to.F("scale", 1) != 1) return false;
                seen[index] = true;
            }
            return true;
        }
    }
}
