using System;

namespace Cane
{
    public enum ChannelKind { Number, Angle, Scale, Discrete, Attachment }

    public readonly struct Curve
    {
        internal readonly int Kind;
        internal readonly double X1, Y1, X2, Y2;
        private Curve(int kind, double x1 = 0, double y1 = 0, double x2 = 0, double y2 = 0)
        { Kind = kind; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
        public static Curve Linear => default;
        public static Curve Stepped => new Curve(1);
        public static Curve Bezier(double x1, double y1, double x2, double y2) => new Curve(2, x1, y1, x2, y2);
        public static Curve ValueBezier(double x1, double y1, double x2, double y2) => new Curve(3, x1, y1, x2, y2);
    }

    public readonly struct Key
    {
        public readonly double Time, Value;
        public readonly Curve Curve;
        public Key(double time, double value, Curve curve = default) { Time = time; Value = value; Curve = curve; }
    }

    public sealed class Timeline
    {
        public int Channel { get; }
        public ChannelKind Kind { get; }
        public int Count { get; }
        public int Stride { get; }
        private readonly double[] pool;
        private readonly int timeOffset, coefficientOffset;
        public ReadOnlySpan<double> Times => pool.AsSpan(timeOffset, Count);
        internal ReadOnlySpan<double> Coefficients => pool.AsSpan(coefficientOffset, Count * Stride);
        public bool IsDiscrete => Kind == ChannelKind.Discrete || Kind == ChannelKind.Attachment;

        // Serialized timelines borrow slices of the one decoded coefficient pool.
        internal Timeline(int channel, ChannelKind kind, double[] pool, int offset, int count, int stride)
        {
            if (offset < 0 || count < 0 || stride != 2 && stride != 8 || (long)offset + (long)count * (1 + stride) > pool.Length)
                throw new ArgumentException("Invalid timeline slice");
            for (int i = 1; i < count; i++) if (!(pool[offset + i] > pool[offset + i - 1])) throw new ArgumentException("Unordered timeline");
            Channel = channel; Kind = kind; this.pool = pool; timeOffset = offset;
            coefficientOffset = offset + count; Count = count; Stride = stride;
        }

        public Timeline(int channel, ReadOnlySpan<Key> keys, ChannelKind kind = ChannelKind.Number)
        {
            Channel = channel; Kind = kind; Stride = 8;
            pool = new double[keys.Length * 9]; coefficientOffset = keys.Length;
            int written = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                Key key = keys[i];
                if (double.IsNaN(key.Time) || double.IsInfinity(key.Time) || i > 0 && key.Time < keys[i - 1].Time)
                    throw new ArgumentException("Invalid key time");
                int next = i + 1;
                if (next < keys.Length && keys[next].Time == key.Time) continue;
                int at = coefficientOffset + written * 8;
                pool[written++] = key.Time; pool[at] = key.Value;
                Curve curve = key.Curve;
                if (next == keys.Length || IsDiscrete || curve.Kind == 1) continue;
                double dt = keys[next].Time - key.Time, dv = keys[next].Value - key.Value;
                if (curve.Kind == 0 || curve.Kind == 2 && dv == 0) { pool[at + 5] = dv / dt; continue; }
                double x1 = curve.X1, x2 = curve.X2;
                double y1 = curve.Kind == 2 ? curve.Y1 * dv : curve.Y1;
                double y2 = curve.Kind == 2 ? curve.Y2 * dv : dv + curve.Y2;
                pool[at + 1] = (x1 >= 0 && x1 <= 1 && x2 >= 0 && x2 <= 1 ? 1 : -1) / dt;
                pool[at + 2] = 3 * x1; pool[at + 3] = 3 * x2 - 6 * x1; pool[at + 4] = 1 + 3 * x1 - 3 * x2;
                pool[at + 5] = 3 * y1; pool[at + 6] = 3 * y2 - 6 * y1; pool[at + 7] = dv + 3 * y1 - 3 * y2;
            }
            Count = written;
        }

        public int Locate(double time, int cursor = 0)
        {
            if (Count == 0 || time < pool[timeOffset]) return -1;
            cursor = Math.Max(0, Math.Min(Count - 1, cursor));
            if (time >= pool[timeOffset + cursor] && (cursor + 1 == Count || time < pool[timeOffset + cursor + 1])) return cursor;
            if (cursor + 1 < Count && time >= pool[timeOffset + cursor + 1] &&
                (cursor + 2 == Count || time < pool[timeOffset + cursor + 2])) return cursor + 1;
            int low = 0, high = Count;
            while (low < high) { int middle = (low + high) >> 1; if (pool[timeOffset + middle] <= time) low = middle + 1; else high = middle; }
            return low - 1;
        }

        public double Value(double time, int cursor)
        {
            double dt = time - pool[timeOffset + cursor];
            int i = coefficientOffset + cursor * Stride;
            if (Stride == 2) return pool[i] + dt * pool[i + 1];
            if (pool[i + 1] == 0) return pool[i] + dt * pool[i + 5];
            double u = CurveTime(dt * Math.Abs(pool[i + 1]), pool[i + 4], pool[i + 3], pool[i + 2], pool[i + 1] > 0);
            return pool[i] + ((pool[i + 7] * u + pool[i + 6]) * u + pool[i + 5]) * u;
        }

        private static double CurveTime(double x, double a, double b, double d, bool monotone)
        {
            if (x == 0) return 0;
            if (monotone)
            {
                double u = x;
                for (int n = 0; n < 8; n++)
                {
                    double error = ((a * u + b) * u + d) * u - x;
                    if (error == 0) return u;
                    double slope = (3 * a * u + 2 * b) * u + d;
                    double next = u - error * slope / (slope * slope - error * (3 * a * u + b));
                    if (!(next > 0 && next < 1)) break;
                    if (Math.Abs(error) < 1e-12 * Math.Abs(slope)) return next;
                    u = next;
                }
            }
            return BracketedCurveTime(x, a, b, d, monotone);
        }

        private static double BracketedCurveTime(double x, double a, double b, double d, bool monotone)
        {
            double low = 0, high = 1;
            if (!monotone)
            {
                double first = 1, second = 1;
                if (a == 0) { if (b != 0) first = -d / (2 * b); }
                else
                {
                    double discriminant = b * b - 3 * a * d;
                    if (discriminant > 0)
                    {
                        double root = Math.Sqrt(discriminant);
                        first = (-b - root) / (3 * a); second = (-b + root) / (3 * a);
                        if (first > second) { double swap = first; first = second; second = swap; }
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    double cut = i == 0 ? first : i == 1 ? second : 1;
                    if (cut <= 0 || cut > 1) continue;
                    if (((a * cut + b) * cut + d) * cut >= x) { high = cut; break; }
                    low = cut;
                }
            }
            double u = x > low && x < high ? x : (low + high) / 2;
            for (int n = 0; n < 64; n++)
            {
                double error = ((a * u + b) * u + d) * u - x;
                if (error == 0) break;
                if (error > 0) high = u; else low = u;
                double next = u - error / ((3 * a * u + 2 * b) * u + d);
                if (!(next > low && next < high)) next = (low + high) / 2;
                if (Math.Abs(next - u) < 1e-12) { u = next; break; }
                u = next;
            }
            return u;
        }
    }
}
