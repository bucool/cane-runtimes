using System;

namespace Cane
{
    public enum SequenceMode { Hold, Once, Loop, PingPong, OnceReverse, LoopReverse, PingPongReverse }

    public readonly struct SequenceFrame
    {
        public readonly int Texture;
        public readonly float[] Uvs;
        public readonly Geometry? Geometry;
        public SequenceFrame(int texture, float[] uvs, Geometry? geometry = null) { Texture = texture; Uvs = uvs; Geometry = geometry; }
    }

    public sealed class Sequence
    {
        public readonly SequenceFrame[] Frames;
        public readonly int SetupIndex;
        internal readonly Sequence TimelineSource;
        public Sequence(SequenceFrame[] frames, int setupIndex = 0) { Frames = frames; SetupIndex = setupIndex; TimelineSource = this; }
        internal Sequence(SequenceFrame[] frames, Sequence source) { Frames = frames; SetupIndex = source.SetupIndex; TimelineSource = source.TimelineSource; }
        public SequenceFrame Frame(double index) => Frames[(int)Math.Max(0, Math.Min(Frames.Length - 1, index < 0 ? SetupIndex : index))];
    }

    public readonly struct SequenceKey
    {
        public readonly double Time, Delay;
        public readonly long Index;
        public readonly SequenceMode Mode;
        public SequenceKey(double time, long index, SequenceMode mode = SequenceMode.Hold, double delay = 0)
        { Time = time; Index = index; Mode = mode; Delay = delay; }
    }

    public sealed class SequenceTimeline
    {
        public readonly int Slot;
        public readonly Sequence Sequence;
        public readonly SequenceKey[] Keys;
        public readonly Timeline Timing;
        public SequenceTimeline(int slot, Sequence sequence, SequenceKey[] keys)
        {
            Slot = slot; Sequence = sequence.TimelineSource; Keys = keys;
            var times = new Key[keys.Length]; for (int i = 0; i < times.Length; i++) times[i] = new Key(keys[i].Time, i);
            Timing = new Timeline(0, times, ChannelKind.Discrete);
        }
        public double Value(double time, int key)
        {
            var k = Keys[(int)Timing.Coefficients[key * Timing.Stride]]; int count = Sequence.Frames.Length;
            if (k.Mode == SequenceMode.Hold) return k.Index;
            double index = k.Index + (k.Delay > 0 ? Math.Floor((time - k.Time) / k.Delay + 1e-9) : 0);
            if (k.Mode == SequenceMode.Once) return Math.Min(count - 1, index);
            if (k.Mode == SequenceMode.OnceReverse) return Math.Max(0, count - 1 - index);
            if (k.Mode == SequenceMode.Loop) return Animation.Modulo(index, count);
            if (k.Mode == SequenceMode.LoopReverse) return count - 1 - Animation.Modulo(index, count);
            int period = count * 2 - 2;
            if (period == 0) return 0;
            double phase = index + (k.Mode == SequenceMode.PingPongReverse ? count - 1 : 0), frame = Animation.Modulo(phase, period);
            return frame < count ? frame : period - frame;
        }
    }
}
