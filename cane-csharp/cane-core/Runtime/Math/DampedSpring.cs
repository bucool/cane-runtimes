using System;

namespace Cane
{
    internal static class DampedSpring
    {
        private static double Expm1(double x)
        {
            if (Math.Abs(x) >= .5) return Math.Exp(x) - 1;
            double sum = x, term = x;
            for (int order = 2; order <= 32; order++) { term *= x / order; sum += term; }
            return sum;
        }
        private static double Exprel(double x) => x == 0 ? 1 : Expm1(x) / x;
        internal static (double Position, double Velocity) Advance(double position, double velocity, double seconds, double acceleration, double stiffness, double drag)
        {
            if (!Finite(position) || !Finite(velocity) || !Finite(seconds) || !Finite(acceleration) || !Finite(stiffness) || !Finite(drag) || seconds < 0 || stiffness < 0 || drag < 0) throw Failure();
            if (seconds == 0 || (velocity == 0 && acceleration == stiffness * position)) return (position, velocity);
            double nextPosition, nextVelocity;
            if (Math.Max(drag * seconds, Math.Sqrt(stiffness) * seconds) < .5)
            {
                double pt = velocity * seconds, vt = (acceleration - stiffness * position - drag * velocity) * seconds, dp = 0, dv = 0;
                for (int order = 1; order <= 24; order++)
                { dp += pt; dv += vt; double interval = seconds / (order + 1), next = vt * interval; vt = (-stiffness * pt - drag * vt) * interval; pt = next; }
                nextPosition = position + dp; nextVelocity = velocity + dv;
            }
            else
            {
                double h00, h01, h10, h11, forced;
                if (stiffness == 0)
                {
                    double decay = Math.Exp(-drag * seconds), integral = -Expm1(-drag * seconds) / drag;
                    h00 = 1; h01 = integral; h10 = 0; h11 = decay; forced = (seconds - integral) / drag;
                }
                else
                {
                    double half = drag * .5, discriminant = half * half - stiffness;
                    if (discriminant >= 0)
                    {
                        double gap = Math.Sqrt(discriminant), fast = -half - gap, slow = -stiffness / (half + gap);
                        double decay = Math.Exp(slow * seconds), response = decay * seconds * Exprel((fast - slow) * seconds);
                        h00 = decay - slow * response; h01 = response; h10 = -stiffness * response;
                        h11 = Math.Exp(fast * seconds) + slow * response; forced = (seconds * Exprel(slow * seconds) - response) / -fast;
                    }
                    else
                    {
                        double frequency = Math.Sqrt(-discriminant), phase = frequency * seconds, decay = Math.Exp(-half * seconds);
                        double response = decay * Math.Sin(phase) / frequency, cosine = decay * Math.Cos(phase), sineHalf = Math.Sin(phase * .5);
                        h00 = cosine + half * response; h01 = response; h10 = -stiffness * response; h11 = cosine - half * response;
                        forced = (-Expm1(-half * seconds) + decay * 2 * sineHalf * sineHalf - half * response) / stiffness;
                    }
                }
                nextPosition = h00 * position + h01 * velocity + forced * acceleration;
                nextVelocity = h10 * position + h11 * velocity + h01 * acceleration;
            }
            if (!Finite(nextPosition) || !Finite(nextVelocity)) throw Failure();
            return (nextPosition, nextVelocity);
        }
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static RuntimeException Failure() => new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Spring state cannot be represented.");
    }
}
