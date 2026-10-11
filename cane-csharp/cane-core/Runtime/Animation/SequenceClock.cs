using System;
using System.Numerics;

namespace Cane.Animation
{
    internal static class SequenceClock
    {
        internal static BigInteger Units(float value)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value) & 0x7fffffff;
            int exponent = (int)(bits >> 23); uint fraction = bits & 0x7fffff;
            return exponent == 0 ? new BigInteger(fraction) : new BigInteger(fraction | 0x800000) << (exponent - 1);
        }
        internal static bool PeriodicResetCrossed(float from, float to, float offset, float period)
        {
            if (!(to > from) || !(period > 0) || to < offset) return false;
            if (from < offset) return true;
            BigInteger origin = Units(offset), step = Units(period);
            return (Units(to) - origin) / step > (Units(from) - origin) / step;
        }
        internal static int? Sample(float time, float start, float interval, int count, int initial, string mode)
        {
            if (!Numeric.Finite(time) || !Numeric.Finite(start) || !Numeric.Finite(interval) || time < 0 || start < 0 || interval < 0
                || (interval == 0 && mode != "fixedFrame") || count < 1 || initial < 0 || initial >= count)
                throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "sampleSequence", "Invalid sequence interval, index, or time.");
            if (time < start) return null;
            if (mode == "fixedFrame" || count == 1) return initial;
            BigInteger steps = (Units(time) - Units(start)) / Units(interval);
            int last = count - 1, forward;
            switch (mode)
            {
                case "playOnce": case "playOnceReverse": forward = steps < last - initial ? initial + (int)steps : last; break;
                case "repeat": case "repeatReverse": forward = (int)((steps % count + initial) % count); break;
                case "backAndForthLoop": case "backAndForthLoopReverse":
                    long period = 2L * last; long position = (long)((steps % period + initial) % period);
                    forward = (int)Math.Min(position, period - position); break;
                default: throw new RuntimeException(RuntimeErrorCode.InvalidArgument, "sampleSequence", "Unknown sequence mode.");
            }
            bool reverse = mode == "playOnceReverse" || mode == "repeatReverse" || mode == "backAndForthLoopReverse";
            return reverse ? last - forward : forward;
        }
    }
}
