using System;

namespace Cane
{
    internal sealed class SliderState
    {
        private readonly int[] cursors;
        public double? SourceValue;
        public double Time;
        public bool Sampled;
        public SliderState(Animation animation)
        { cursors = new int[animation.Timelines.Length + animation.DrawOrders.Length + animation.Sequences.Length + animation.Deforms.Length]; }
        public void Apply(Skeleton s, SliderConstraint c, int offset)
        {
            Sampled = false; double mix = s.Working[offset + 1];
            if (mix == 0) return;
            double time = s.Working[offset]; double? sourceValue = null;
            if (c.Source != null)
            {
                var from = c.Source;
                if (!from.Local && (s.ScaleX == 0 || s.ScaleY == 0)) return;
                double value = TransformSolver.ReadProperty(s, from.Bone, from.Property, from.Local);
                if (!from.Local && from.Property == TransformProperty.Rotation && value < 0) value += 360;
                sourceValue = value; time = (value - from.FromOffset) * from.Scale + from.Offset;
                if (!c.Loop) time = Math.Max(0, time);
            }
            s.Working[offset] = time; var animation = c.Animation;
            if (c.Loop && animation.Duration > 0) time = Animation.Modulo(time, animation.Duration);
            SourceValue = sourceValue; Time = time; Sampled = true;
            animation.Apply(s.Working, s.Data.Setup, time, mix, c.Additive, false, cursors);
            var frame = s.Frame; if (frame == null) return;
            int cursor = animation.Timelines.Length;
            foreach (var t in animation.DrawOrders)
            {
                int key = t.Timing.Locate(time, cursors[cursor]); cursors[cursor++] = key;
                if (key >= 0) t.Apply(frame.AppliedOrder, key);
            }
            foreach (var t in animation.Sequences)
            {
                frame.Resolve(t.Slot, (int)s.Working[s.Data.SlotOffset + t.Slot * 8]);
                var state = frame.Slots[t.Slot]; int key = t.Timing.Locate(time, cursors[cursor]); cursors[cursor++] = key;
                if (key >= 0 && state.Sequence == t.Sequence) state.SequenceIndex = t.Value(time, key);
            }
            foreach (var t in animation.Deforms)
            {
                frame.Resolve(t.Slot, (int)s.Working[s.Data.SlotOffset + t.Slot * 8]);
                int key = t.Timing.Locate(time, cursors[cursor]); cursors[cursor++] = key;
                if (key >= 0 && frame.Slots[t.Slot].Geometry == t.Geometry) t.Apply(frame.Deform(t.Slot, true)!, time, key, mix, c.Additive);
            }
        }
    }
}
