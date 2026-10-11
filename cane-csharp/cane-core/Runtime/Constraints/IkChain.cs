using System;

namespace Cane.Constraints
{
    internal sealed class IkSolveOptions
    {
        internal int Iterations;
        internal float Threshold, Mix;
        internal double Softness;
        internal bool BendPositive, Compress, Stretch;
        internal IkTransverse Transverse;
    }
    internal sealed class IkChain
    {
        private readonly BoneLocal[] source;
        private readonly float[] lengths;
        private readonly string[] modes;
        private readonly Affine? parent;
        private readonly Affine root;
        private readonly IkSolveOptions options;
        private int remaining = 1000000;
        private readonly struct Evaluation
        {
            internal readonly DoublePoint Tip, Pivot;
            internal readonly double Reach, Bend;
            internal Evaluation(DoublePoint tip, DoublePoint pivot, double reach, double bend) { Tip = tip; Pivot = pivot; Reach = reach; Bend = bend; }
        }
        internal IkChain(BoneLocal[] source, float[] lengths, string[] modes, Affine? parent, Affine root, IkSolveOptions options)
        { this.source = source; this.lengths = lengths; this.modes = modes; this.parent = parent; this.root = root; this.options = options; }
        private Affine Outer => parent ?? root;
        private bool Ordinary() { for (int i = 0; i < modes.Length; i++) if (!(i == 0 && !parent.HasValue) && modes[i] != "inheritAll") return false; return true; }
        private Evaluation Evaluate(BoneLocal[] poses)
        {
            if (poses.Length == 0 || poses.Length != lengths.Length || poses.Length != modes.Length) throw IkGeometry.Failure();
            Affine? outer = parent; var origins = new DoublePoint[poses.Length];
            for (int i = 0; i < poses.Length; i++)
            {
                if (remaining == 0) throw new RuntimeException(RuntimeErrorCode.ResourceLimit, "apply", "IK bone evaluation budget is exhausted."); remaining--;
                Affine world = Affine.Child(outer ?? root, poses[i], outer.HasValue ? modes[i] : "inheritAll");
                if (!world.IsFinite) throw IkGeometry.Failure(); origins[i] = new DoublePoint(world.Tx, world.Ty); outer = world;
            }
            Point p = outer!.Value.Transform(lengths[lengths.Length - 1], 0); TransformGeometry.Check(p.X, p.Y); var tip = new DoublePoint(p.X, p.Y);
            double reach = 0; for (int i = 1; i < origins.Length; i++) reach += IkGeometry.Sub(origins[i], origins[i - 1]).Length;
            reach += IkGeometry.Sub(tip, origins[origins.Length - 1]).Length;
            double bend = origins.Length == 2 ? IkGeometry.Cross(IkGeometry.Sub(origins[1], origins[0]), IkGeometry.Sub(tip, origins[1])) : 0;
            return new Evaluation(tip, origins[0], reach, bend);
        }
        private IkObservation Observe(Evaluation value, DoublePoint target) => new IkObservation(IkGeometry.Sub(value.Tip, target), value.Bend == 0 || (value.Bend > 0) == options.BendPositive);
        private static bool Agrees(DoublePoint expected, Evaluation observed)
        {
            double extent = Math.Max(Math.Abs(expected.X), Math.Max(Math.Abs(expected.Y), observed.Reach));
            return IkGeometry.Sub(expected, observed.Tip).Length <= 64 * 1.1920928955078125e-7 * extent + 2 * 1.401298464324817e-45;
        }
        private (BoneLocal[] Poses, double Factor) Parameters(BoneLocal[] basis, double[] parameters)
        {
            var poses = (BoneLocal[])basis.Clone(); for (int i = 0; i < poses.Length; i++) poses[i] = poses[i].With(2, TransformGeometry.Narrow(parameters[i]));
            double factor = parameters.Length > basis.Length ? Math.Exp(parameters[basis.Length]) : 1;
            BoneLocal scale = IkGeometry.Scaled(basis[0], factor, 1, options.Transverse); poses[0] = poses[0].With(3, scale.ScaleX).With(4, scale.ScaleY); return (poses, factor);
        }
        private (double Low, double High) ScaleBounds(BoneLocal pose)
        {
            double min = Math.Log(1.401298464324817e-45), max = Math.Log(float.MaxValue), low = min - max, high = max - min;
            for (int i = 0; i < 2; i++)
            {
                float axis = i == 0 ? pose.ScaleX : pose.ScaleY; if (axis == 0 || (i == 1 && options.Transverse == IkTransverse.Fixed)) continue;
                double log = Math.Log(Math.Abs((double)axis)); bool inverse = i == 1 && options.Transverse == IkTransverse.Area;
                low = Math.Max(low, inverse ? log - max : min - log); high = Math.Min(high, inverse ? log - min : max - log);
            }
            return (options.Compress ? Math.Min(low, 0) : 0, options.Stretch ? Math.Max(high, 0) : 0);
        }
        private (BoneLocal[] Poses, double Factor, int Passes) Search(BoneLocal[] basis, BoneLocal[] seed, DoublePoint target, bool scale)
        {
            int count = basis.Length; var values = new double[count + (scale ? 1 : 0)]; var bounds = new (double, double)[values.Length];
            for (int i = 0; i < count; i++) { values[i] = seed[i].RotationDegrees; bounds[i] = (-180, 180); }
            if (scale) bounds[count] = ScaleBounds(basis[0]);
            var result = IkSearch.Minimize(values, bounds, count, count <= 2 ? Math.Max(options.Iterations, 1) : options.Iterations, options.Threshold,
                parameters => Observe(Evaluate(Parameters(basis, parameters).Poses), target));
            var poses = Parameters(basis, result.Parameters); return (poses.Poses, poses.Factor, result.Passes);
        }
        private (BoneLocal[] Poses, double Factor)? Single(BoneLocal basis, DoublePoint target)
        {
            DoublePoint? point = IkGeometry.InverseTarget(Outer, basis, target); if (!point.HasValue) return null; DoublePoint v = point.Value;
            double length = (double)lengths[0] * basis.ScaleX, requested = v.Length; if (length == 0) return (new[] { basis }, 1);
            double ratio = requested / Math.Abs(length), factor = (ratio < 1 && options.Compress) || (ratio > 1 && options.Stretch) ? ratio : 1;
            BoneLocal pose = IkGeometry.Scaled(basis, factor, 1, options.Transverse);
            if (requested != 0) pose = pose.With(2, TransformGeometry.Narrow(TransformGeometry.Wrap(Math.Atan2(v.Y, v.X) * (180 / Math.PI) - basis.ShearXDegrees - (length < 0 ? 180 : 0))));
            Evaluation observed = Evaluate(new[] { pose }); if (requested == 0) return (new[] { pose }, factor);
            double unit = Math.Abs(length) * factor / requested;
            DoublePoint expected = IkGeometry.Transform(Outer, new DoublePoint(basis.X + v.X * unit, basis.Y + v.Y * unit));
            return Agrees(expected, observed) ? (new[] { pose }, factor) : ((BoneLocal[], double)?)null;
        }
        private (BoneLocal[] Poses, int Passes) Rotate(BoneLocal[] basis, DoublePoint target, double softness)
        {
            if (basis.Length == 2 && Ordinary())
            {
                try
                {
                    var candidate = IkGeometry.Pair(basis[0], basis[1], lengths[1], Outer, target, (float)softness, options.BendPositive);
                    if (candidate.HasValue)
                    {
                        Evaluation? observed = IkSearch.Feasible(() => Evaluate(candidate.Value.Poses));
                        if (observed.HasValue && Agrees(candidate.Value.Expected, observed.Value)) return (candidate.Value.Poses, 1);
                    }
                }
                catch (RuntimeException error) when (error.Code == RuntimeErrorCode.NonFinite) { }
            }
            Evaluation before = Evaluate(basis); double requested = IkGeometry.Sub(target, before.Pivot).Length;
            double distance = IkGeometry.SoftDistance(requested, before.Reach, softness), factor = requested == 0 ? 1 : distance / requested;
            var result = Search(basis, basis, IkGeometry.Add(before.Pivot, IkGeometry.Scale(IkGeometry.Sub(target, before.Pivot), factor)), false); return (result.Poses, result.Passes);
        }
        internal (BoneLocal[] Poses, int Iterations, float Residual) Solve(DoublePoint target)
        {
            TransformGeometry.Check(target.X, target.Y, options.Threshold, options.Softness, options.Mix);
            if (options.Threshold < 0 || options.Softness < 0 || options.Mix < 0 || options.Mix > 1) throw IkGeometry.Failure();
            Evaluation baseline = Evaluate(source);
            if (options.Mix == 0) return ((BoneLocal[])source.Clone(), 0, TransformGeometry.Narrow(Observe(baseline, target).Residual));
            BoneLocal[] full = (BoneLocal[])source.Clone(); double factor = 1; int iterations = 0;
            if (source.Length == 1 && Ordinary())
            {
                var candidate = Single(source[0], target); if (candidate.HasValue) { full = candidate.Value.Poses; factor = candidate.Value.Factor; iterations = 1; }
            }
            if (iterations == 0)
            {
                var rotated = Rotate(full, target, 0); full = rotated.Poses; iterations = rotated.Passes;
                if (source.Length <= 2 && (options.Compress || options.Stretch) && Observe(Evaluate(full), target).Residual > options.Threshold)
                {
                    var scaled = Search(source, full, target, true); full = scaled.Poses; factor = scaled.Factor; iterations = Math.Max(iterations, scaled.Passes);
                    if (options.Compress && options.Transverse != IkTransverse.Area)
                    {
                        var collapsed = (BoneLocal[])source.Clone(); collapsed[0] = IkGeometry.Scaled(source[0], 0, 1, options.Transverse);
                        var candidate = IkSearch.Feasible(() => Rotate(collapsed, target, 0));
                        if (candidate.HasValue)
                        {
                            double before = Observe(Evaluate(full), target).Residual, after = Observe(Evaluate(candidate.Value.Poses), target).Residual;
                            if (after < before) { full = candidate.Value.Poses; factor = 0; iterations = Math.Max(iterations, candidate.Value.Passes); }
                        }
                    }
                }
            }
            if (options.Softness > 0 && source.Length > 1) { var softened = Rotate(full, target, options.Softness); full = softened.Poses; iterations = Math.Max(iterations, softened.Passes); }
            var mixed = (BoneLocal[])source.Clone();
            for (int i = 0; i < mixed.Length; i++)
            {
                if (mixed[i].RotationDegrees == full[i].RotationDegrees) continue;
                double original = TransformGeometry.Wrap(mixed[i].RotationDegrees);
                mixed[i] = mixed[i].With(2, TransformGeometry.Narrow(TransformGeometry.Wrap(original + options.Mix * TransformGeometry.Wrap(full[i].RotationDegrees - original))));
            }
            BoneLocal axes = IkGeometry.Scaled(source[0], factor, options.Mix, options.Transverse); mixed[0] = mixed[0].With(3, axes.ScaleX).With(4, axes.ScaleY);
            return (mixed, iterations, TransformGeometry.Narrow(Observe(Evaluate(mixed), target).Residual));
        }
    }
}
