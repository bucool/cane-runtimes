using System;

namespace Cane
{
    internal readonly struct InheritedBasis
    {
        internal readonly double A, B, C, D;
        private InheritedBasis(double a, double b, double c, double d) { A = a; B = b; C = c; D = d; }
        private static (double, double) Unit(double x, double y)
        { double length = Math.Sqrt(x * x + y * y); return length == 0 ? (1, 0) : (x / length, y / length); }
        internal static InheritedBasis From(Affine parent, string mode)
        {
            if (!parent.IsFinite) throw new RuntimeException(RuntimeErrorCode.NonFinite, "composeBoneWorld", "Parent basis must be finite.");
            double a = parent.A, b = parent.B, c = parent.C, d = parent.D;
            if (mode == "inheritAll") return new InheritedBasis(a, b, c, d);
            if (mode == "translationOnly") return new InheritedBasis(1, 0, 0, 1);
            var (rx, ry) = Unit(a + d, b - c);
            if (mode == "ignoreScaleAndReflection") return new InheritedBasis(rx, ry, -ry, rx);
            double qa = rx, qb = ry, qc = -ry, qd = rx;
            if (a * d - b * c < 0) { var (fx, fy) = Unit(a - d, b + c); qa = fx; qb = fy; qc = fy; qd = -fx; }
            if (mode == "ignoreScale") return new InheritedBasis(qa, qb, qc, qd);
            if (mode != "ignoreRotationAndReflection") throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "composeBoneWorld", "Unknown bone inheritance mode.");
            double xy = ((a * qb + c * qd) + (b * qa + d * qc)) * .5;
            return new InheritedBasis(a * qa + c * qc, xy, xy, b * qb + d * qd);
        }
    }
}
