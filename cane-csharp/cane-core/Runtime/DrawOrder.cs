using System;

namespace Cane
{
    public readonly struct DrawOrderKey
    {
        public readonly double Time;
        public readonly int[] Order;
        public DrawOrderKey(double time, int[] order) { Time = time; Order = order; }
    }

    public sealed class DrawOrderGroup
    {
        public readonly int[] Slots;
        internal readonly bool[] Members;
        public DrawOrderGroup(int[] slots, int slotCount)
        {
            Slots = (int[])slots.Clone(); Array.Sort(Slots); Members = new bool[slotCount];
            foreach (int slot in Slots) Members[slot] = true;
        }
        /// <summary>Replaces only positions currently occupied by this group.</summary>
        public void Reorder(Span<int> output, ReadOnlySpan<int> order)
        { for (int i = 0, k = 0; i < output.Length && k < order.Length; i++) if (Members[output[i]]) output[i] = order[k++]; }
    }

    public sealed class DrawOrderTimeline
    {
        public readonly Timeline Timing;
        public readonly DrawOrderGroup Group;
        public readonly DrawOrderKey[] Keys;
        public DrawOrderTimeline(int[] slots, int slotCount, DrawOrderKey[] keys)
        {
            Group = new DrawOrderGroup(slots, slotCount); Keys = keys;
            var times = new Key[keys.Length]; for (int i = 0; i < times.Length; i++) times[i] = new Key(keys[i].Time, i);
            Timing = new Timeline(0, times, ChannelKind.Discrete);
        }
        public void Apply(Span<int> output, int key) => Group.Reorder(output, Keys[(int)Timing.Coefficients[key * Timing.Stride]].Order);
    }
}
