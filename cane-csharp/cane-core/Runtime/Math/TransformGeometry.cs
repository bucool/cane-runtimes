using System;
using System.Collections.Generic;

namespace Cane
{
    internal readonly struct DoublePoint
    {
        internal readonly double X, Y;
        internal DoublePoint(double x, double y) { X = x; Y = y; }
        internal double Length => Math.Sqrt(X * X + Y * Y);
    }
    internal readonly struct TransformRoute
    {
        internal readonly int From, To;
        internal readonly double SourceOrigin, TargetOrigin, Gain;
        internal readonly double? Limit;
        internal TransformRoute(int from, int to, double sourceOrigin, double targetOrigin, double gain, double? limit)
        { From = from; To = to; SourceOrigin = sourceOrigin; TargetOrigin = targetOrigin; Gain = gain; Limit = limit; }
    }
    internal static class TransformGeometry
    {
        private const double Roundoff = 64 * 1.1920928955078125e-7, Minimum = 1.401298464324817e-45;
        internal static RuntimeException Failure() => new RuntimeException(RuntimeErrorCode.InvalidState, "apply", "Transform geometry cannot be represented through the parent basis.");
        internal static void Check(params double[] values) { foreach (double v in values) if (double.IsNaN(v) || double.IsInfinity(v)) throw new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Transform arithmetic must be finite."); }
        internal static float Narrow(double value, bool scale = false)
        { Check(value); float result = (float)value; if (!Numeric.Finite(result) || (scale && value != 0 && result == 0)) throw Failure(); return result; }
        internal static double Wrap(double value) { double r = value % 360; if (r < 0) r += 360; return r >= 180 ? r - 360 : r; }
        internal static double[] Local(BoneLocal local)
        { var result = new double[] { local.RotationDegrees, local.X, local.Y, local.ScaleX, local.ScaleY, local.ShearYDegrees }; Check(result); Check(local.ShearXDegrees); return result; }
        internal static BoneLocal ToLocal(double[] v, BoneLocal previous)
        { Check(v); return new BoneLocal(Narrow(v[1]), Narrow(v[2]), Narrow(v[0]), Narrow(v[3], true), Narrow(v[4], true), previous.ShearXDegrees, Narrow(v[5])); }
        internal static double[] World(Affine m)
        {
            Check(m.A, m.B, m.C, m.D, m.Tx, m.Ty);
            double a = m.A, b = m.B, c = m.C, d = m.D, sx = Math.Sqrt(a * a + b * b), sy = Math.Sqrt(c * c + d * d), degrees = 180 / Math.PI;
            if (sx == 0) return new[] { sy == 0 ? 0 : Wrap(Math.Atan2(d, c) * degrees - 90), m.Tx, m.Ty, 0, sy, 0 };
            double sign = a * d - b * c < 0 ? -1 : 1, along = a / sx * c + b / sx * d, normal = a / sx * d - b / sx * c;
            return new[] { Wrap(Math.Atan2(b, a) * degrees), m.Tx, m.Ty, sx, sign * sy, sy == 0 ? 0 : Math.Atan2(-sign * along, sign * normal) * degrees };
        }
        internal static Affine ToWorld(double[] v)
        {
            Check(v); double t = Wrap(v[0]) * (Math.PI / 180), p = Wrap(v[5]) * (Math.PI / 180);
            double si = Math.Sin(t), co = Math.Cos(t), sp = Math.Sin(p), cp = Math.Cos(p);
            var m = new Affine(Narrow(v[3] * co), Narrow(v[3] * si), Narrow(-v[4] * (si * cp + co * sp)), Narrow(v[4] * (co * cp - si * sp)), Narrow(v[1]), Narrow(v[2]));
            if ((v[3] != 0 && m.A == 0 && m.B == 0) || (v[4] != 0 && m.C == 0 && m.D == 0)) throw Failure(); CheckWorld(m); return m;
        }
        internal static void CheckWorld(Affine m) { if (!m.IsFinite || !Numeric.Finite(Numeric.Hypot(m.A, m.B)) || !Numeric.Finite(Numeric.Hypot(m.C, m.D))) throw Failure(); }
        internal static bool Same(Affine a, Affine b) => a.A == b.A && a.B == b.B && a.C == b.C && a.D == b.D && a.Tx == b.Tx && a.Ty == b.Ty;
        internal static bool Same(double[] a, double[] b) { for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        internal static DoublePoint InversePoint(Affine m, DoublePoint point) => Inverse(m, new DoublePoint(point.X - m.Tx, point.Y - m.Ty), false, 0);
        private static DoublePoint Inverse(Affine m, DoublePoint rhs, bool roundoff, double scale)
        {
            double a = m.A, b = m.B, c = m.C, d = m.D, det = a * d - b * c; Check(a, b, c, d, rhs.X, rhs.Y);
            DoublePoint result;
            if (det != 0) result = new DoublePoint((d * rhs.X - c * rhs.Y) / det, (a * rhs.Y - b * rhs.X) / det);
            else
            {
                double squared = a * a + b * b + c * c + d * d;
                if (squared == 0) { if (rhs.X == 0 && rhs.Y == 0) return new DoublePoint(0, 0); throw Failure(); }
                result = new DoublePoint((a * rhs.X + b * rhs.Y) / squared, (c * rhs.X + d * rhs.Y) / squared);
            }
            Check(result.X, result.Y);
            double x1 = a * result.X, x2 = c * result.Y, y1 = b * result.X, y2 = d * result.Y;
            double tx = 64 * 2.220446049250313e-16 * (Math.Abs(x1) + Math.Abs(x2) + Math.Abs(rhs.X));
            double ty = 64 * 2.220446049250313e-16 * (Math.Abs(y1) + Math.Abs(y2) + Math.Abs(rhs.Y));
            if (roundoff && det == 0) { tx = Math.Max(tx, Math.Max(Roundoff * scale, Minimum)); ty = Math.Max(ty, Math.Max(Roundoff * scale, Minimum)); }
            if (Math.Abs(x1 + x2 - rhs.X) > tx || Math.Abs(y1 + y2 - rhs.Y) > ty) throw Failure();
            return result;
        }
        private static DoublePoint NearestPoint(Affine m, DoublePoint wanted, DoublePoint reference)
        {
            if ((double)m.A * m.D != (double)m.B * m.C) return InversePoint(m, wanted);
            var image = new DoublePoint(m.A * reference.X + m.C * reference.Y + m.Tx, m.B * reference.X + m.D * reference.Y + m.Ty);
            var delta = Inverse(m, new DoublePoint(wanted.X - image.X, wanted.Y - image.Y), true, Math.Max(wanted.Length, image.Length));
            return new DoublePoint(reference.X + delta.X, reference.Y + delta.Y);
        }
        private static DoublePoint NearestAxis(Affine parent, string mode, DoublePoint wanted, DoublePoint reference)
        {
            if (mode == "inheritAll") return NearestPoint(new Affine(parent.A, parent.B, parent.C, parent.D, 0, 0), wanted, reference);
            if (mode == "translationOnly") return wanted;
            InheritedBasis basis = InheritedBasis.From(parent, mode);
            if (mode != "ignoreRotationAndReflection") return new DoublePoint(basis.A * wanted.X + basis.B * wanted.Y, basis.C * wanted.X + basis.D * wanted.Y);
            bool full = (double)parent.A * parent.D != (double)parent.B * parent.C;
            var image = new DoublePoint(basis.A * reference.X + basis.C * reference.Y, basis.B * reference.X + basis.D * reference.Y);
            var rhs = full ? wanted : new DoublePoint(wanted.X - image.X, wanted.Y - image.Y);
            DoublePoint preimage = Inverse(parent, rhs, true, Math.Max(wanted.Length, image.Length));
            InheritedBasis q = InheritedBasis.From(parent, "ignoreScale");
            var delta = new DoublePoint(q.A * preimage.X + q.C * preimage.Y, q.B * preimage.X + q.D * preimage.Y);
            return full ? delta : new DoublePoint(reference.X + delta.X, reference.Y + delta.Y);
        }
        private static DoublePoint LocalAxis(BoneLocal p, bool y)
        {
            double angle = ((double)p.RotationDegrees + (y ? p.ShearYDegrees : p.ShearXDegrees) + (y ? 90 : 0)) * (Math.PI / 180), scale = y ? p.ScaleY : p.ScaleX;
            return new DoublePoint(scale * Math.Cos(angle), scale * Math.Sin(angle));
        }
        internal static BoneLocal Recover(Affine requested, Affine parent, string mode, BoneLocal previous)
        {
            Local(previous); World(requested);
            DoublePoint origin = NearestPoint(parent, new DoublePoint(requested.Tx, requested.Ty), new DoublePoint(previous.X, previous.Y));
            DoublePoint x = NearestAxis(parent, mode, new DoublePoint(requested.A, requested.B), LocalAxis(previous, false));
            DoublePoint y = NearestAxis(parent, mode, new DoublePoint(requested.C, requested.D), LocalAxis(previous, true));
            double sx = previous.ScaleX < 0 ? -1 : 1, sy = previous.ScaleY < 0 ? -1 : 1;
            float rotation = previous.RotationDegrees, shear = previous.ShearYDegrees;
            if (x.X != 0 || x.Y != 0) rotation = Narrow(Wrap(Math.Atan2(sx * x.Y, sx * x.X) * (180 / Math.PI) - previous.ShearXDegrees));
            if (y.X != 0 || y.Y != 0) shear = Narrow(Wrap(Math.Atan2(sy * y.Y, sy * y.X) * (180 / Math.PI) - rotation - 90));
            var result = new BoneLocal(Narrow(origin.X), Narrow(origin.Y), rotation, Narrow(sx * x.Length, true), Narrow(sy * y.Length, true), previous.ShearXDegrees, shear);
            Affine actual = Affine.Child(parent, result, mode); CheckWorld(actual);
            CheckAxis(new DoublePoint(actual.A, actual.B), new DoublePoint(requested.A, requested.B));
            CheckAxis(new DoublePoint(actual.C, actual.D), new DoublePoint(requested.C, requested.D));
            double ex = Math.Abs((double)parent.A * result.X) + Math.Abs((double)parent.C * result.Y) + Math.Abs(parent.Tx);
            double ey = Math.Abs((double)parent.B * result.X) + Math.Abs((double)parent.D * result.Y) + Math.Abs(parent.Ty);
            if (Math.Abs((double)actual.Tx - requested.Tx) > Math.Max(Roundoff * Math.Max(ex, Math.Abs(requested.Tx)), Minimum)
                || Math.Abs((double)actual.Ty - requested.Ty) > Math.Max(Roundoff * Math.Max(ey, Math.Abs(requested.Ty)), Minimum)) throw Failure();
            return result;
        }
        private static void CheckAxis(DoublePoint a, DoublePoint b)
        {
            if ((b.X != 0 || b.Y != 0) && a.X == 0 && a.Y == 0) throw Failure();
            double tolerance = Math.Max(Roundoff * Math.Max(a.Length, b.Length), Minimum);
            if (Math.Abs(a.X - b.X) > tolerance || Math.Abs(a.Y - b.Y) > tolerance) throw Failure();
        }
        internal static double[] Evaluate(double[] source, double[] current, IReadOnlyList<TransformRoute> routes, float[] mixes, bool relative)
        {
            Check(source); Check(current); foreach (float m in mixes) Check(m); var output = (double[])current.Clone();
            foreach (TransformRoute route in routes)
            {
                Check(route.SourceOrigin, route.TargetOrigin, route.Gain); if (route.Limit.HasValue) Check(route.Limit.Value);
                int i = route.To; double m = mixes[i]; if (m == 0) continue;
                double z = route.TargetOrigin + (source[route.From] - route.SourceOrigin) * route.Gain; Check(z);
                if (route.Limit.HasValue) z = Math.Max(Math.Min(route.TargetOrigin, route.Limit.Value), Math.Min(Math.Max(route.TargetOrigin, route.Limit.Value), z));
                double q = output[i], next;
                if (i == 0 || i == 5) { double delta = relative ? z : Wrap(z - q); next = delta == 0 ? q : Wrap(Wrap(q) + m * delta); }
                else if (relative && (i == 3 || i == 4)) next = z == 1 ? q : q * ((1 - m) + m * z);
                else if (relative) next = z == 0 ? q : q + m * z;
                else next = q == z ? q : (1 - m) * q + m * z;
                Check(next); output[i] = next;
            }
            return output;
        }
    }
}
