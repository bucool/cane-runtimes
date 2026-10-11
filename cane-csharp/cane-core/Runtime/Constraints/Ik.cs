using System;
using System.Collections.Generic;
using Cane.Animation;
using Cane.Format;

namespace Cane.Constraints
{
    internal static class Ik
    {
        internal static void Solve(Pose pose, Json c, Affine root)
        {
            var chain = new int[c["chainBoneIds"].Items.Count]; var seen = new HashSet<int>();
            if (chain.Length == 0) return;
            for (int i = 0; i < chain.Length; i++)
            {
                if (!pose.Data.BoneIndex.TryGetValue(c["chainBoneIds"][i].String, out int bone) || !seen.Add(bone)
                    || (i > 0 && pose.Data.BoneData[bone].Parent != chain[i - 1])) return;
                chain[i] = bone;
            }
            DoublePoint target;
            if (c["targetBoneId"].IsNull) target = new DoublePoint(c["target"].F("x"), c["target"].F("y"));
            else { Affine matrix = pose.World[pose.Data.BoneIndex[c.S("targetBoneId")]]; target = new DoublePoint(matrix.Tx, matrix.Ty); }
            float mix = c.F("mix", 1), threshold = c.F("threshold");
            var options = new IkSolveOptions {
                Iterations = c.I("iterations"), Threshold = Numeric.Finite(threshold) ? Math.Max(0, threshold) : 0,
                Mix = Numeric.Finite(mix) ? Numeric.Clamp(mix, 0, 1) : 0, Softness = c.F("softness"),
                BendPositive = c.B("bendPositive", true), Compress = c.B("compress"), Stretch = c.B("stretch"),
                Transverse = c["uniform"].Value is bool uniform ? uniform ? IkTransverse.Uniform : IkTransverse.Fixed : c.S("uniform") == "volume" ? IkTransverse.Area : IkTransverse.Fixed
            };
            var source = new BoneLocal[chain.Length]; var lengths = new float[chain.Length]; var modes = new string[chain.Length];
            for (int i = 0; i < chain.Length; i++) { source[i] = pose.Locals[chain[i]]; lengths[i] = pose.Data.BoneData[chain[i]].Length; modes[i] = pose.Modes[chain[i]]; }
            int enclosing = pose.Data.BoneData[chain[0]].Parent;
            Affine? parent = enclosing < 0 ? (Affine?)null : pose.World[enclosing];
            var result = new IkChain(source, lengths, modes, parent, root, options).Solve(target);
            var diagnostic = new IkConstraintDiagnostic(result.Residual, options.Threshold, result.Iterations, options.Iterations, options.Mix);
            if (options.Mix != 0)
            {
                var locals = new Dictionary<int, BoneLocal>(); var worlds = new Dictionary<int, Affine>(); var affected = new HashSet<int>(chain);
                for (int i = 0; i < chain.Length; i++) locals[chain[i]] = result.Poses[i];
                for (int bone = 0; bone < pose.World.Length; bone++)
                {
                    int p = pose.Data.BoneData[bone].Parent; if (p >= 0 && affected.Contains(p)) affected.Add(bone); if (!affected.Contains(bone)) continue;
                    BoneLocal local = locals.TryGetValue(bone, out BoneLocal staged) ? staged : pose.Locals[bone];
                    Affine outer = p < 0 ? root : worlds.TryGetValue(p, out Affine previous) ? previous : pose.World[p];
                    Affine world = Affine.Child(outer, local, p < 0 ? "inheritAll" : pose.Modes[bone]); if (!world.IsFinite) throw IkGeometry.Failure(); worlds[bone] = world;
                }
                foreach (var entry in locals) pose.Locals[entry.Key] = entry.Value;
                foreach (var entry in worlds) pose.World[entry.Key] = entry.Value;
            }
            pose.Diagnostics[c.S("id")] = diagnostic;
        }
    }
}