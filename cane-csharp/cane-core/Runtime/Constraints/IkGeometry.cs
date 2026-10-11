using System;
using System.Collections.Generic;

namespace Cane.Constraints
{
    internal enum IkTransverse { Fixed, Uniform, Area }
    internal static class IkGeometry
    {
        internal static RuntimeException Failure() => new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "IK geometry cannot be represented.");
        internal static DoublePoint Add(DoublePoint a, DoublePoint b) => new DoublePoint(a.X + b.X, a.Y + b.Y);
        internal static DoublePoint Sub(DoublePoint a, DoublePoint b) => new DoublePoint(a.X - b.X, a.Y - b.Y);
        internal static DoublePoint Scale(DoublePoint a, double s) => new DoublePoint(a.X * s, a.Y * s);
        internal static double Dot(DoublePoint a, DoublePoint b) => a.X * b.X + a.Y * b.Y;
        internal static double Cross(DoublePoint a, DoublePoint b) => a.X * b.Y - a.Y * b.X;
        internal static DoublePoint Rotate(DoublePoint a, double angle) => new DoublePoint(Math.Cos(angle) * a.X - Math.Sin(angle) * a.Y, Math.Sin(angle) * a.X + Math.Cos(angle) * a.Y);
        internal static double Radians(double a) { double r = a % (2 * Math.PI); if (r < 0) r += 2 * Math.PI; return r >= Math.PI ? r - 2 * Math.PI : r; }
        internal static BoneLocal Scaled(BoneLocal pose, double multiplier, double mix, IkTransverse mode)
        {
            TransformGeometry.Check(pose.ScaleX, pose.ScaleY, multiplier, mix); if (multiplier < 0 || mix < 0 || mix > 1) throw Failure();
            double factor = (1 - mix) + mix * multiplier; if (mode == IkTransverse.Area && factor == 0) throw Failure();
            double fy = mode == IkTransverse.Uniform ? factor : mode == IkTransverse.Area ? 1 / factor : 1;
            return pose.With(3, TransformGeometry.Narrow(pose.ScaleX * factor, true)).With(4, TransformGeometry.Narrow(pose.ScaleY * fy, true));
        }
        private static double Expm1(double value)
        {
            if (Math.Abs(value) >= .5) return Math.Exp(value) - 1;
            double term = value, result = value; for (int n = 2; n <= 32; n++) { term *= value / n; result += term; } return result;
        }
        internal static double SoftDistance(double distance, double reach, double softness)
        {
            TransformGeometry.Check(distance, reach, softness); if (distance < 0 || reach < 0 || softness < 0) throw Failure();
            double width = Math.Min(softness, reach), start = reach - width;
            return width == 0 || distance <= start ? distance : start - width * Expm1(-(distance - start) / width);
        }
        private static List<double> Unique(List<double> values, double tolerance)
        {
            values.Sort(); var result = new List<double>(); foreach (double v in values) if (result.Count == 0 || Math.Abs(v - result[result.Count - 1]) > tolerance) result.Add(v); return result;
        }
        private static List<double> Roots(List<double> coefficients)
        {
            var c = new List<double>(coefficients); while (c.Count > 1 && c[c.Count - 1] == 0) c.RemoveAt(c.Count - 1); if (c.Count <= 1) return new List<double>();
            double scale = 0; foreach (double v in c) scale = Math.Max(scale, Math.Abs(v)); for (int i = 0; i < c.Count; i++) c[i] /= scale;
            double Evaluate(double x) { double sum = 0; for (int i = c.Count - 1; i >= 0; i--) sum = sum * x + c[i]; return sum; }
            var derivative = new List<double>(); for (int i = 1; i < c.Count; i++) derivative.Add(c[i] * i);
            var intervals = Roots(derivative); intervals.Add(-1); intervals.Add(1); intervals = Unique(intervals, 1e-14);
            double magnitude = 0; foreach (double v in c) magnitude += Math.Abs(v); double tolerance = 64 * 2.220446049250313e-16 * magnitude;
            var roots = new List<double>(); foreach (double x in intervals) if (Math.Abs(Evaluate(x)) <= tolerance) roots.Add(x);
            for (int i = 1; i < intervals.Count; i++)
            {
                double low = intervals[i - 1], high = intervals[i], left = Evaluate(low), right = Evaluate(high);
                if (left == 0 || right == 0 || (left < 0) == (right < 0)) continue;
                for (int j = 0; j < 80; j++)
                {
                    double middle = (low + high) / 2; if (middle == low || middle == high) break;
                    double value = Evaluate(middle); if (value == 0) { low = high = middle; break; }
                    if ((value < 0) == (left < 0)) low = middle; else high = middle;
                }
                roots.Add((low + high) / 2);
            }
            return Unique(roots, 1e-12);
        }
        private sealed class ReachCurve
        {
            private readonly DoublePoint h, u, z;
            internal readonly double Extent;
            private readonly List<double> stationary = new List<double> { -Math.PI, -Math.PI / 2, 0, Math.PI / 2 };
            internal double Minimum = double.PositiveInfinity, Maximum;
            internal ReachCurve(DoublePoint h, DoublePoint u, DoublePoint z)
            {
                TransformGeometry.Check(h.X, h.Y, u.X, u.Y, z.X, z.Y); Extent = Math.Max(h.Length, Math.Max(u.Length, z.Length)); TransformGeometry.Check(Extent);
                DoublePoint Normalize(DoublePoint p) => Extent == 0 ? p : new DoublePoint(p.X / Extent, p.Y / Extent);
                this.h = Normalize(h); this.u = Normalize(u); this.z = Normalize(z);
                foreach (double chart in new double[] { 1, -1 })
                {
                    DoublePoint cu = Scale(this.u, chart), cz = Scale(this.z, chart);
                    DoublePoint[] e = { Add(this.h, cu), Scale(cz, 2), Sub(this.h, cu) }, derivative = { cz, Scale(cu, -2), Scale(cz, -1) };
                    var p = new double[5]; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) p[i + j] += Dot(e[i], derivative[j]);
                    foreach (double t in Roots(new List<double>(p))) stationary.Add(Radians(2 * Math.Atan(t) + (chart < 0 ? Math.PI : 0)));
                }
                foreach (double beta in stationary) { double radius = Vector(beta).Length; Minimum = Math.Min(Minimum, radius); Maximum = Math.Max(Maximum, radius); }
                TransformGeometry.Check(Maximum * Extent);
            }
            private DoublePoint Vector(double beta) => Add(h, Add(Scale(u, Math.Cos(beta)), Scale(z, Math.Sin(beta))));
            internal (DoublePoint Endpoint, double Parent, double Child) Solve(DoublePoint target, DoublePoint preferred, bool positive, bool reflected)
            {
                double distance = target.Length, effective = Math.Max(Minimum * Extent, Math.Min(Maximum * Extent, distance)); TransformGeometry.Check(distance, preferred.X, preferred.Y);
                double radius = Extent == 0 ? 0 : effective / Extent; var angles = new List<double>(stationary) { Radians(preferred.Y) };
                foreach (double chart in new double[] { 1, -1 })
                {
                    DoublePoint cu = Scale(u, chart), cz = Scale(z, chart); DoublePoint[] e = { Add(h, cu), Scale(cz, 2), Sub(h, cu) };
                    var p = new double[5]; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) p[i + j] += Dot(e[i], e[j]);
                    p[0] -= radius * radius; p[2] -= 2 * radius * radius; p[4] -= radius * radius;
                    foreach (double t in Roots(new List<double>(p))) angles.Add(Radians(2 * Math.Atan(t) + (chart < 0 ? Math.PI : 0)));
                }
                angles.RemoveAll(beta => Math.Abs(Vector(beta).Length - radius) > 2e-7); angles = Unique(angles, 1e-12);
                (int, double, double, double)? best = null;
                foreach (double beta in angles)
                {
                    DoublePoint v = Vector(beta); double alpha = distance == 0 || v.Length == 0 ? Radians(preferred.X) : Radians(Math.Atan2(target.Y, target.X) - Math.Atan2(v.Y, v.X));
                    double bend = Cross(h, Sub(v, h)); if (reflected) bend = -bend;
                    double da = Radians(alpha - Radians(preferred.X)), db = Radians(beta - Radians(preferred.Y));
                    var candidate = (bend != 0 && (bend > 0) != positive ? 1 : 0, da * da + db * db, beta, alpha);
                    if (!best.HasValue || candidate.CompareTo(best.Value) < 0) best = candidate;
                }
                if (!best.HasValue) throw Failure(); var chosen = best.Value;
                DoublePoint endpoint = Scale(Rotate(Vector(chosen.Item3), chosen.Item4), Extent); TransformGeometry.Check(endpoint.X, endpoint.Y, Sub(endpoint, target).Length);
                return (endpoint, chosen.Item4, chosen.Item3);
            }
        }
        internal static DoublePoint? InverseTarget(Affine outer, BoneLocal pose, DoublePoint target)
        {
            double a = outer.A, b = outer.B, c = outer.C, d = outer.D, determinant = a * d - b * c; if (determinant == 0) return null;
            double dx = target.X - outer.Tx, dy = target.Y - outer.Ty;
            var result = new DoublePoint((d * dx - c * dy) / determinant - pose.X, (a * dy - b * dx) / determinant - pose.Y);
            if (double.IsNaN(result.X) || double.IsNaN(result.Y) || double.IsInfinity(result.X) || double.IsInfinity(result.Y)) return null; return result;
        }
        internal static DoublePoint Transform(Affine m, DoublePoint p) => new DoublePoint(m.A * p.X + m.C * p.Y + m.Tx, m.B * p.X + m.D * p.Y + m.Ty);
        internal static (BoneLocal[] Poses, DoublePoint Expected)? Pair(BoneLocal parent, BoneLocal child, float childLength, Affine outer, DoublePoint target, float softness, bool positive)
        {
            DoublePoint? converted = InverseTarget(outer, parent, target); if (!converted.HasValue) return null; DoublePoint localTarget = converted.Value;
            Affine shape = Affine.FromLocal(parent.With(2, 0)); double ell = (double)childLength * child.ScaleX;
            var hx = new DoublePoint(shape.A, shape.B); var hy = new DoublePoint(shape.C, shape.D);
            var curve = new ReachCurve(Add(Scale(hx, child.X), Scale(hy, child.Y)), Scale(hx, ell), Scale(hy, ell));
            double distance = localTarget.Length, desired = SoftDistance(distance, curve.Maximum * curve.Extent, softness), ratio = distance == 0 ? 1 : desired / distance;
            var solution = curve.Solve(Scale(localTarget, ratio), new DoublePoint(parent.RotationDegrees * (Math.PI / 180), ((double)child.RotationDegrees + child.ShearXDegrees) * (Math.PI / 180)), positive, (double)outer.A * outer.D - (double)outer.B * outer.C < 0);
            float Angle(float before, double after) => (float)TransformGeometry.Wrap(TransformGeometry.Wrap(before) + TransformGeometry.Wrap(TransformGeometry.Wrap(after) - TransformGeometry.Wrap(before)));
            return (new[] { parent.With(2, Angle(parent.RotationDegrees, solution.Parent * (180 / Math.PI))), child.With(2, Angle(child.RotationDegrees, solution.Child * (180 / Math.PI) - child.ShearXDegrees)) },
                Transform(outer, new DoublePoint(parent.X + solution.Endpoint.X, parent.Y + solution.Endpoint.Y)));
        }
    }
}
