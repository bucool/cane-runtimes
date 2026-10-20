using System;
using System.Collections.Generic;

namespace Cane
{
    public enum EventType { Start, Interrupt, End, Dispose, Complete, Event }
    public readonly struct RuntimeEvent
    {
        public readonly EventType Type;
        public readonly int Track;
        public readonly TrackEntry Entry;
        public readonly EventKey? Key;
        public RuntimeEvent(EventType type, int track, TrackEntry entry, EventKey? key = null) { Type = type; Track = track; Entry = entry; Key = key; }
    }
    internal static class EventCollector
    {
        public static void Collect(TrackEntry entry, int track, double previous, bool keysEnabled, List<RuntimeEvent> output)
        {
            double duration = Math.Max(0, entry.End - entry.Start), time = entry.Time;
            if (time < previous) return;
            var keys = entry.Animation?.Events ?? Array.Empty<EventKey>();
            double startCycle = duration > 0 && entry.Loop ? Math.Floor(Math.Max(0, previous) / duration) : 0;
            double endCycle = duration > 0 && entry.Loop ? Math.Floor(Math.Max(0, time) / duration) : 0;
            for (double cycle = startCycle; cycle <= endCycle; cycle++)
            {
                if (keysEnabled) for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[entry.Reverse ? keys.Length - 1 - i : i];
                    if (key.Time < entry.Start || key.Time > entry.End) continue;
                    double phase = entry.Reverse ? entry.End - key.Time : key.Time - entry.Start, occurrence = cycle * duration + phase;
                    if (occurrence > previous && occurrence <= time) output.Add(new RuntimeEvent(EventType.Event, track, entry, key));
                }
                double complete = (cycle + 1) * duration;
                if (complete > previous && complete <= time && (duration > 0 || cycle == 0)) output.Add(new RuntimeEvent(EventType.Complete, track, entry));
            }
        }
        public static void PhysicsResets(TrackEntry entry, double previous, HashSet<int> output)
        {
            if (entry.Time < previous) return;
            double duration = Math.Max(0, entry.End - entry.Start);
            foreach (var key in entry.Animation?.PhysicsResets ?? Array.Empty<PhysicsResetKey>())
            {
                if (key.Time < entry.Start || key.Time > entry.End) continue;
                double phase = entry.Reverse ? entry.End - key.Time : key.Time - entry.Start;
                double occurrence = entry.Loop && duration > 0 ? phase + Math.Max(0, Math.Floor((previous - phase) / duration) + 1) * duration : phase;
                if (occurrence > previous && occurrence <= entry.Time) output.Add(key.Constraint);
            }
        }
    }
}
