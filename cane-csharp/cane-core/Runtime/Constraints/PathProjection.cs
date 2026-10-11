using System;

namespace Cane.Constraints
{
    internal sealed partial class PathSampler
    {
        internal float? ProjectDistance(Point point)
        {
            var target = new DoublePoint(point.X, point.Y); var budget = new ArcBudget(); double bestError = double.PositiveInfinity, bestDistance = 0;
            void Consider(DoublePoint p, double distance)
            {
                double error = new DoublePoint(target.X - p.X, target.Y - p.Y).Length;
                if (error < bestError || (error == bestError && distance < bestDistance)) { bestError = error; bestDistance = distance; }
            }
            for (int i = 0; i < curves.Length; i++)
            {
                CubicArc curve = curves[i]; double t = curve.ClosestParameter(target, budget), start = i == 0 ? 0 : lengths[i - 1];
                Consider(curve.Point(t), start + (constant ? curve.DistanceAtParameter(t) : t * (lengths[i] - start)));
            }
            if (!closed) for (int end = 0; end < 2; end++)
            {
                CubicArc curve = end == 0 ? curves[0] : curves[curves.Length - 1]; DoublePoint p = curve.Point(end), tangent = curve.Tangent(end); double norm = tangent.Length;
                double ux = tangent.X / norm, uy = tangent.Y / norm, along = (target.X - p.X) * ux + (target.Y - p.Y) * uy;
                if ((end == 0 && along < 0) || (end == 1 && along > 0)) Consider(new DoublePoint(p.X + along * ux, p.Y + along * uy), end == 0 ? along : total + along);
            }
            if (closed && total > 0) { bestDistance %= total; if (bestDistance < 0) bestDistance += total; }
            return FiniteFloat(bestDistance);
        }
    }
}
