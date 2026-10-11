using System;
using System.Collections.Generic;

namespace Cane.Constraints
{
    internal readonly struct IkObservation
    {
        internal readonly double Residual;
        internal readonly DoublePoint Error;
        internal readonly bool BendMatches;
        internal IkObservation(DoublePoint error, bool bendMatches) { Error = error; Residual = error.Length; BendMatches = bendMatches; }
    }
    internal static class IkSearch
    {
        internal static T? Feasible<T>(Func<T> action) where T : struct
        { try { return action(); } catch (RuntimeException error) when (error.Code == RuntimeErrorCode.NonFinite) { return null; } }
        private static double Clamp(double x, double low, double high) => Math.Max(low, Math.Min(high, x));
        private static void Coordinate(double[] values, int coordinate, (double Low, double High) bounds, Func<double[], IkObservation> evaluate)
        {
            double lower = bounds.Low, upper = bounds.High; if (lower == upper) return;
            double bestX = values[coordinate], bestError = evaluate(values).Residual, step = (upper - lower) / 32;
            var probes = new List<(double X, double Error)> { (bestX, bestError) };
            double Sample(double x) { values[coordinate] = x; double error = Feasible(() => evaluate(values))?.Residual ?? double.PositiveInfinity; if (error < bestError) { bestX = x; bestError = error; } return error; }
            for (int i = 0; i <= 32; i++) { double x = lower + i * step; probes.Add((x, Sample(x))); }
            probes.Sort((a, b) => { int comparison = a.Error.CompareTo(b.Error); return comparison == 0 ? a.X.CompareTo(b.X) : comparison; });
            double fraction = (Math.Sqrt(5) - 1) / 2;
            for (int candidate = 0; candidate < 2; candidate++)
            {
                double x = Clamp(probes[candidate].X, lower, upper), low = Math.Max(x - step, lower), high = Math.Min(x + step, upper);
                double left = high - fraction * (high - low), right = low + fraction * (high - low), fl = Sample(left), fr = Sample(right);
                for (int i = 0; i < 40; i++)
                {
                    if (fl <= fr) { high = right; right = left; fr = fl; left = high - fraction * (high - low); fl = Sample(left); }
                    else { low = left; left = right; fl = fr; right = low + fraction * (high - low); fr = Sample(right); }
                }
            }
            values[coordinate] = bestX;
        }
        private static IkObservation Coupled(double[] values, (double Low, double High)[] bounds, int angularCount, IkObservation baseline, Func<double[], IkObservation> evaluate)
        {
            var jacobian = new DoublePoint[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double original = values[i], low = bounds[i].Low, high = bounds[i].High; if (low == high || original < low || original > high) continue;
                double step = i < angularCount ? .05 : .001, left = Math.Max(original - step, low), right = Math.Min(original + step, high);
                values[i] = left; IkObservation? a = Feasible(() => evaluate(values)); values[i] = right; IkObservation? b = Feasible(() => evaluate(values)); values[i] = original;
                if (a.HasValue && b.HasValue) jacobian[i] = new DoublePoint((b.Value.Error.X - a.Value.Error.X) / (right - left), (b.Value.Error.Y - a.Value.Error.Y) / (right - left));
            }
            double aa = 0, bb = 0, dd = 0; foreach (DoublePoint j in jacobian) { aa += j.X * j.X; bb += j.X * j.Y; dd += j.Y * j.Y; }
            double damping = (aa + dd) * 1e-6; if (damping == 0 || double.IsInfinity(damping) || double.IsNaN(damping)) return baseline;
            aa += damping; dd += damping; double determinant = aa * dd - bb * bb;
            if (determinant <= 0 || double.IsInfinity(determinant) || double.IsNaN(determinant)) return baseline;
            var v = new DoublePoint((dd * baseline.Error.X - bb * baseline.Error.Y) / determinant, (aa * baseline.Error.Y - bb * baseline.Error.X) / determinant);
            var delta = new double[values.Length]; double maximum = 1;
            for (int i = 0; i < delta.Length; i++) { delta[i] = -jacobian[i].X * v.X - jacobian[i].Y * v.Y; maximum = Math.Max(maximum, Math.Abs(delta[i]) / (i < angularCount ? 30 : 1)); }
            if (double.IsInfinity(maximum) || double.IsNaN(maximum)) return baseline;
            var originalValues = (double[])values.Clone(); double factor = 1 / maximum;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                for (int i = 0; i < values.Length; i++) values[i] = Clamp(originalValues[i] + delta[i] * factor, bounds[i].Low, bounds[i].High);
                IkObservation? candidate = Feasible(() => evaluate(values)); if (candidate.HasValue && candidate.Value.Residual < baseline.Residual) return candidate.Value; factor *= .5;
            }
            Array.Copy(originalValues, values, values.Length); return baseline;
        }
        internal static (double[] Parameters, IkObservation Observation, int Passes) Minimize(double[] initial, (double Low, double High)[] bounds, int angularCount, int iterations, double threshold, Func<double[], IkObservation> evaluate)
        {
            if (initial.Length == 0 || initial.Length != bounds.Length || angularCount > initial.Length || threshold < 0) throw IkGeometry.Failure();
            TransformGeometry.Check(initial); TransformGeometry.Check(threshold); foreach (var bound in bounds) { TransformGeometry.Check(bound.Low, bound.High); if (bound.Low > bound.High) throw IkGeometry.Failure(); }
            var winner = (Parameters: (double[])initial.Clone(), Observation: evaluate(initial), Passes: 0);
            if (winner.Observation.Residual <= threshold && winner.Observation.BendMatches) return winner;
            foreach (double offset in angularCount == 2 ? new double[] { 0, 90, -90, 180 } : new double[] { 0 })
            {
                var values = (double[])initial.Clone(); if (angularCount == 2) values[1] = TransformGeometry.Wrap(values[1] + offset);
                IkObservation? possible = Feasible(() => evaluate(values)); if (!possible.HasValue) continue; IkObservation current = possible.Value; int passes = 0;
                for (int pass = 0; pass < iterations; pass++)
                {
                    if (current.Residual <= threshold && current.BendMatches) break; double before = current.Residual;
                    for (int coordinate = values.Length - 1; coordinate >= 0; coordinate--) { Coordinate(values, coordinate, bounds[coordinate], evaluate); current = evaluate(values); }
                    current = Coupled(values, bounds, angularCount, current, evaluate); passes = pass + 1; if (current.Residual >= before) break;
                }
                IkObservation old = winner.Observation;
                bool better = current.Residual <= threshold && old.Residual <= threshold && current.BendMatches != old.BendMatches ? current.BendMatches : current.Residual < old.Residual;
                if (better) winner = (values, current, passes);
                if (winner.Observation.Residual <= threshold && winner.Observation.BendMatches) break;
            }
            return winner;
        }
    }
}
