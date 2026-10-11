using System;
using System.Collections.Generic;

namespace Cane
{
    internal static class CubicRoots
    {
        private static double Evaluate(double[] p, double u)
        {
            double Pair(double a, double b) => a * (1 - u) + b * u;
            double a0 = Pair(p[0], p[1]), a1 = Pair(p[1], p[2]), a2 = Pair(p[2], p[3]);
            return Pair(Pair(a0, a1), Pair(a1, a2));
        }
        private static double[] Boundaries(double[] p)
        {
            double d0 = p[1] - p[0], d1 = p[2] - p[1], d2 = p[3] - p[2];
            double a = d0 - 2 * d1 + d2, b = 2 * (d1 - d0), c = d0;
            if (!Finite(a) || !Finite(b) || !Finite(c)) throw Failure();
            double scale = Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(c)));
            var points = new List<double> { 0, .5 };
            void Add(double u) { if (u > 0 && u < .5 && !points.Contains(u)) points.Add(u); }
            if (scale != 0)
            {
                a /= scale; b /= scale; c /= scale;
                if (a == 0) { if (b != 0) Add(-c / b); }
                else
                {
                    double discriminant = b * b - 4 * a * c;
                    if (discriminant >= 0)
                    {
                        double root = Math.Sqrt(discriminant);
                        double q = -.5 * (b + (BitConverter.DoubleToInt64Bits(b) < 0 ? -root : root));
                        if (q == 0) Add(-b / (2 * a));
                        else { Add(q / a); Add(c / q); }
                    }
                }
            }
            points.Sort(); return points.ToArray();
        }
        private static double? FirstHalf(double[] p, double target, bool reverse)
        {
            double[] points = Boundaries(p);
            for (int index = 0; index + 1 < points.Length; index++)
            {
                double a = points[reverse ? points.Length - 1 - index : index];
                double b = points[reverse ? points.Length - 2 - index : index + 1];
                double fa = Evaluate(p, a), fb = Evaluate(p, b);
                if (fa == target) return a; if (fb == target) return b;
                if ((fa < target) == (fb < target)) continue;
                for (int step = 0; step < 384; step++)
                {
                    double mid = a + (b - a) * .5;
                    if (mid == a || mid == b) return Math.Abs(fa - target) <= Math.Abs(Evaluate(p, b) - target) ? a : b;
                    double fm = Evaluate(p, mid);
                    if (fm == target) return mid;
                    if ((fm < target) == (fa < target)) { a = mid; fa = fm; } else b = mid;
                }
                throw Failure();
            }
            return null;
        }
        internal static double Sample(double[] x, double[] y, double target)
        {
            if (!Finite(target)) throw Failure();
            foreach (double v in x) if (!Finite(v)) throw Failure();
            foreach (double v in y) if (!Finite(v)) throw Failure();
            double? u = FirstHalf(x, target, false); double result;
            if (u.HasValue) result = Evaluate(y, u.Value);
            else
            {
                double? v = FirstHalf(new[] { x[3], x[2], x[1], x[0] }, target, true);
                if (!v.HasValue) throw Failure();
                result = Evaluate(new[] { y[3], y[2], y[1], y[0] }, v.Value);
            }
            if (!Finite(result)) throw Failure(); return result;
        }
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        private static RuntimeException Failure() => new RuntimeException(RuntimeErrorCode.NonFinite, "sampleCurve", "Cubic intersection cannot be represented or resolved.");
    }
}
