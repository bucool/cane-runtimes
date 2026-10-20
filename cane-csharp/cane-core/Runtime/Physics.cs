using System;
using static Cane.Affine;

namespace Cane
{
    public sealed class PhysicsState
    {
        public readonly double[] Offset = new double[4], Velocity = new double[4], Lag = new double[4], Previous = new double[6];
        public double Remaining;
        public bool Initialized;
        public void Reset()
        { Array.Clear(Offset, 0, 4); Array.Clear(Velocity, 0, 4); Array.Clear(Lag, 0, 4); Remaining = 0; Initialized = false; }
        public void Translate(double x, double y)
        { for (int i = 0; i < 6; i += 2) { Previous[i] -= x; Previous[i + 1] -= y; } }
        public void Rotate(double x, double y, double degrees)
        {
            double cosine = Math.Cos(-degrees * Degrees), sine = Math.Sin(-degrees * Degrees);
            for (int i = 0; i < 6; i += 2)
            { double dx = Previous[i] - x, dy = Previous[i + 1] - y; Previous[i] = x + dx * cosine - dy * sine; Previous[i + 1] = y + dx * sine + dy * cosine; }
        }
        internal void Apply(Skeleton s, PhysicsConstraint c, int offset, double delta, PhysicsMode mode)
        {
            if (mode == PhysicsMode.None) return;
            var m = s.World; var pose = s.Working; int w = c.Bone * 6; double length = s.Data.Lengths[c.Bone];
            double x = m[w + 4], y = m[w + 5], tipX = x + m[w] * length, tipY = y + m[w + 1] * length;
            if (mode == PhysicsMode.Reset || delta < 0) Reset();
            if (!Initialized)
            { Previous[0] = Previous[2] = x; Previous[1] = Previous[3] = y; Previous[4] = tipX; Previous[5] = tipY; Initialized = true; }
            if (mode == PhysicsMode.Reset || delta < 0) return;
            double px = c.X, py = c.Y, rotation = c.Rotate, shear = c.ShearX, scaleX = c.ScaleX;
            var q = Offset; var v = Velocity;
            double h = c.Step, angle = Math.Atan2(m[w + 1], m[w]), worldLength = length * Hypot(m[w], m[w + 1]);
            double inertia = pose[offset], strength = pose[offset + 1], mass = pose[offset + 3], mix = pose[offset + 6], oldLag = 1 - Remaining / h;
            double steps = 0, damping = 1, fx = 0, fy = 0, limit = 0;
            if (mode == PhysicsMode.Update && delta > 0)
            {
                limit = c.Limit * delta;
                double dx = Math.Max(-limit * Math.Abs(s.ScaleX), Math.Min(limit * Math.Abs(s.ScaleX), x - Previous[0]));
                double dy = Math.Max(-limit * Math.Abs(s.ScaleY), Math.Min(limit * Math.Abs(s.ScaleY), y - Previous[1]));
                if (px != 0) q[0] -= dx * inertia;
                if (py != 0) q[1] -= dy * inertia;
                Previous[0] = x; Previous[1] = y;
                double time = Remaining + delta; steps = Math.Floor(time / h + 1e-10); Remaining = Math.Max(0, time - steps * h);
                damping = Math.Pow(pose[offset + 2], 60 * h); double wind = pose[offset + 4], gravity = pose[offset + 5];
                fx = wind * s.Wind[0] + gravity * s.Gravity[0]; fy = wind * s.Wind[1] + gravity * s.Gravity[1];
                for (double step = 0; step < steps; step++) for (int axis = 0; axis < 2; axis++)
                {
                    if (axis == 0 ? px == 0 : py == 0) continue;
                    double force = (axis == 0 ? fx * Math.Abs(s.ScaleX) : fy * Math.Abs(s.ScaleY)) * s.Data.ReferenceScale;
                    v[axis] += (force - strength * q[axis]) * mass * h;
                    Lag[axis] = v[axis] * h; q[axis] += Lag[axis]; v[axis] *= damping;
                }
            }
            double lag = 1 - Remaining / h;
            m[w + 4] = x + (q[0] - Lag[0] * lag) * px * mix; m[w + 5] = y + (q[1] - Lag[1] * lag) * py * mix;
            if (mode == PhysicsMode.Update && delta > 0 && worldLength != 0 && (rotation != 0 || shear != 0 || scaleX != 0))
            {
                double dx = Math.Max(-limit * Math.Abs(s.ScaleX), Math.Min(limit * Math.Abs(s.ScaleX), Previous[2] - m[w + 4]));
                double dy = Math.Max(-limit * Math.Abs(s.ScaleY), Math.Min(limit * Math.Abs(s.ScaleY), Previous[3] - m[w + 5]));
                double previousTurn = (q[2] - Lag[2] * oldLag) * (rotation + shear) * mix;
                if (rotation != 0 || shear != 0)
                { double target = Math.Atan2(Previous[5] - Previous[3] + dy, Previous[4] - Previous[2] + dx); q[2] += WrapAngle((target - angle - previousTurn) * Radians) * Degrees * inertia; }
                if (scaleX != 0)
                { double a = angle + (q[2] - Lag[2] * oldLag) * (rotation + shear) * mix; q[3] += (dx * Math.Cos(a) + dy * Math.Sin(a)) * inertia / worldLength; }
                for (double step = 0; step < steps; step++)
                {
                    double a = angle + q[2] * (rotation + shear) * mix, cosine = Math.Cos(a), sine = Math.Sin(a);
                    for (int axis = 2; axis < 4; axis++)
                    {
                        if (axis == 2 ? rotation == 0 && shear == 0 : scaleX == 0) continue;
                        double force = axis == 2 ? (fy * cosine - fx * sine) * length / s.Data.ReferenceScale : fx * cosine + fy * sine;
                        v[axis] += (force - strength * q[axis]) * mass * h;
                        Lag[axis] = v[axis] * h; q[axis] += Lag[axis]; v[axis] *= damping;
                    }
                }
            }
            double turn = (q[2] - Lag[2] * lag) * mix;
            for (int column = 0; column < 4; column += 2)
            {
                double radians = turn * (column == 0 ? rotation + shear : rotation), cosine = Math.Cos(radians), sine = Math.Sin(radians);
                double a = m[w + column], b = m[w + column + 1]; m[w + column] = a * cosine - b * sine; m[w + column + 1] = a * sine + b * cosine;
            }
            double scale = 1 + (q[3] - Lag[3] * lag) * scaleX * mix;
            m[w] *= scale; m[w + 1] *= scale;
            double scaleY = c.ScaleY == ScaleYMode.PreserveAspect ? scale : c.ScaleY == ScaleYMode.PreserveArea && scale != 0 ? 1 / scale : 1;
            m[w + 2] *= scaleY; m[w + 3] *= scaleY;
            if (mode == PhysicsMode.Update && delta > 0)
            { Previous[2] = m[w + 4]; Previous[3] = m[w + 5]; Previous[4] = Previous[2] + m[w] * length; Previous[5] = Previous[3] + m[w + 1] * length; }
            s.InvalidateLocal(c.Bone);
        }
    }
}
