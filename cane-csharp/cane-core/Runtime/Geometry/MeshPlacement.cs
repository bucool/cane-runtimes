using Cane.Animation;

namespace Cane.Geometry
{
    internal static class MeshPlacement
    {
        internal static Point World(Pose pose, AttachmentData attachment, string operation = "apply")
        {
            Point p = attachment.Placement;
            if (p.X == 0 && p.Y == 0) return p;
            Affine root = pose.Root;
            return Finite((double)root.A * p.X + (double)root.C * p.Y,
                (double)root.B * p.X + (double)root.D * p.Y, operation);
        }

        internal static Point Place(Point point, Point delta)
            => delta.X == 0 && delta.Y == 0 ? point : Finite((double)point.X + delta.X, (double)point.Y + delta.Y);

        internal static Point Unplace(Pose pose, AttachmentData attachment, Point point, string operation = "apply")
        {
            Point delta = World(pose, attachment, operation);
            return delta.X == 0 && delta.Y == 0 ? point : Finite((double)point.X - delta.X, (double)point.Y - delta.Y, operation);
        }

        internal static Affine Place(Affine matrix, Point delta)
        {
            if (delta.X == 0 && delta.Y == 0) return matrix;
            Point translation = Finite((double)matrix.Tx + delta.X, (double)matrix.Ty + delta.Y);
            return new Affine(matrix.A, matrix.B, matrix.C, matrix.D, translation.X, translation.Y);
        }

        private static Point Finite(double x, double y, string operation = "apply")
        {
            var point = new Point((float)x, (float)y);
            if (!Numeric.Finite(point.X) || !Numeric.Finite(point.Y))
                throw new RuntimeException(RuntimeErrorCode.NonFinite, operation, "Mesh placement produces non-finite coordinates.", "placement");
            return point;
        }
    }
}
