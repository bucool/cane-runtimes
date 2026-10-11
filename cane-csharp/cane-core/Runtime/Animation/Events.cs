using System;
using System.Collections.Generic;
using Cane.Format;

namespace Cane.Animation
{
    // A dry counting pass validates the complete batch before the publishing pass allocates event DTOs.
    internal sealed class EventSink
    {
        internal const int Limit = 1000000;
        internal readonly string Operation;
        internal readonly RuntimeData Data;
        internal int Count { get; private set; }
        private readonly int capacity;
        private readonly List<TimedEvent>? items;
        internal EventSink(RuntimeData data, string operation, bool collect, int capacity = Limit)
        { Data = data; Operation = operation; this.capacity = capacity; if (collect) items = new List<TimedEvent>(); }
        internal void Require(double minimum)
        { if (minimum > capacity - Count) throw new RuntimeException(RuntimeErrorCode.ResourceLimit, Operation, "The operation would emit more than 1,000,000 events.", Operation == "seek" ? "timeSeconds" : "deltaSeconds"); }
        private bool Add()
        { Require(1); Count++; return items != null; }
        internal void Lifecycle(string kind, int track, AnimationPlayback e, double offset, long? entryOrder = null)
        {
            if (!Add()) return;
            items!.Add(new TimedEvent(offset, track, entryOrder ?? e.EventOrder * 2 + (kind == "playbackInterrupted" ? 1 : 0), Count, new RuntimeEvent(kind, track, e.Clip?.Id, 0)));
        }
        internal void User(int track, AnimationPlayback e, Json key, double offset)
        {
            if (!Add()) return;
            string id = key.S("eventId"); Json setup = Data.EventDocuments[id];
            long? integer = key["integerValue"].IsNull ? setup["integerValue"].IsNull ? (long?)null : setup.L("integerValue") : key.L("integerValue");
            float? number = key["numberValue"].IsNull ? setup["numberValue"].IsNull ? (float?)null : setup.F("numberValue") : key.F("numberValue");
            string? text = key["stringValue"].StringOrNull ?? setup["stringValue"].StringOrNull;
            string? audio = key["audioId"].StringOrNull ?? setup["audioId"].StringOrNull;
            var value = new RuntimeEvent("customEvent", track, e.Clip!.Id, key.F("time"), setup.S("name"), id, integer, number, text, audio,
                key.F("volume", setup.F("volume", 1)), key.F("balance", setup.F("balance")));
            items!.Add(new TimedEvent(offset, track, e.EventOrder * 2, Count, value));
        }
        internal RuntimeEvent[] Finish()
        {
            if (items == null) return Array.Empty<RuntimeEvent>();
            items.Sort((a, b) => { int c = a.Offset.CompareTo(b.Offset); if (c != 0) return c; c = a.Track.CompareTo(b.Track); if (c != 0) return c; c = a.EntryOrder.CompareTo(b.EntryOrder); return c == 0 ? a.Order.CompareTo(b.Order) : c; });
            var result = new RuntimeEvent[items.Count]; for (int i = 0; i < result.Length; i++) result[i] = items[i].Value; return result;
        }
        private readonly struct TimedEvent
        {
            internal readonly double Offset;
            internal readonly int Track, Order;
            internal readonly long EntryOrder;
            internal readonly RuntimeEvent Value;
            internal TimedEvent(double offset, int track, long entryOrder, int order, RuntimeEvent value) { Offset = offset; Track = track; EntryOrder = entryOrder; Order = order; Value = value; }
        }
    }

    internal static class EventCrossings
    {
        internal static void Collect(AnimationPlayback entry, AnimationPlayback? incoming, int track, float previous, float current, double offset, double delta, EventSink sink)
        {
            if (previous == current) return;
            bool initialize = !entry.CursorInitialized;
            entry.CursorInitialized = true;
            if (entry.Clip == null) return;
            double duration = entry.Duration;
            bool forward = current > previous;
            var keys = entry.Clip.Raw["events"].Items;
            double requested=delta*entry.Rate;
            double RelativeWall(double raw) => requested==0?0:Math.Max(0,Math.Min(1,(raw-previous)/requested))*delta;
            double Wall(double raw) => offset + RelativeWall(raw);
            bool Enabled(double relativeWall)
            {
                if (incoming == null) return true;
                double progress = incoming.BlendDuration == 0 ? 1 : Math.Max(0, Math.Min(1, (incoming.BlendElapsed + relativeWall) / incoming.BlendDuration));
                return progress < entry.EventBlendThreshold;
            }
            void User(Json key, double raw)
            { double relative = RelativeWall(raw); if (Enabled(relative)) sink.User(track, entry, key, offset + relative); }
            float initial = entry.Loop && duration>0 ? previous % entry.Duration : Numeric.Clamp(previous,0,entry.Duration); if(initial<0)initial+=entry.Duration;initial+=entry.Start;
            if (initialize && Enabled(0)) foreach (Json k in keys) if (k.F("time") == initial) sink.User(track, entry, k, offset);
            if (entry.Duration == 0) return;
            if (!entry.Loop)
            {
                if (forward)
                {
                    foreach (Json k in keys)
                    { double raw = (float)(k.F("time") - entry.Start); if (raw >= 0 && raw <= duration && raw > previous && raw <= current) User(k, raw); }
                    if (previous < duration && current >= duration) sink.Lifecycle("playbackCompleted", track, entry, Wall(duration));
                }
                else
                {
                    for (int i = keys.Count - 1; i >= 0; i--)
                    { Json k = keys[i]; double raw = (float)(k.F("time") - entry.Start); if (raw >= 0 && raw <= duration && raw >= current && raw < previous) User(k, raw); }
                    if (previous > 0 && current <= 0) sink.Lifecycle("playbackCompleted", track, entry, Wall(0));
                }
                return;
            }
            var bounds=new EventBounds(Math.Min(previous,current),!forward,Math.Max(previous,current),forward);
            EventBounds? timeline=bounds;
            if(incoming!=null){
                double remaining=entry.EventBlendThreshold*(double)incoming.BlendDuration-incoming.BlendElapsed;
                if(incoming.BlendDuration==0 || remaining<=0)timeline=null;
                else if(remaining<=delta){double threshold=previous+requested*(remaining/delta);
                    if(forward){if(threshold<=bounds.Low)timeline=null;else if(threshold<=bounds.High)timeline=new EventBounds(bounds.Low,bounds.IncludeLow,threshold,false);}
                    else {if(threshold>=bounds.High)timeline=null;else if(threshold>=bounds.Low)timeline=new EventBounds(threshold,false,bounds.High,bounds.IncludeHigh);}}
            }
            var candidates=new List<(double Cycle,double Offset,int Phase,int Declaration,Json? Key)>();
            void Add(EventBounds? b,double keyOffset,int phase,int declaration,Json? key){
                if(!b.HasValue)return;var indices=EventLattice.Indices(b.Value,keyOffset,duration);if(!indices.HasValue)return;
                double count=indices.Value.Last-indices.Value.First+1;sink.Require(count+candidates.Count);
                for(int n=0;n<(int)count;n++){double cycle=indices.Value.First+n,position=keyOffset;if(position==duration){cycle++;position=0;}candidates.Add((cycle,position,phase,declaration,key));}
            }
            Add(bounds,0,1,0,null);
            for(int i=0;i<keys.Count;i++){Json k=keys[i];float time=k.F("time");if(time<entry.Start || time>entry.End)continue;Add(timeline,(float)(time-entry.Start),time==entry.End?0:time==entry.Start?2:1,i,k);}
            candidates.Sort((a,b)=>{int c=a.Cycle.CompareTo(b.Cycle);if(c==0)c=a.Offset.CompareTo(b.Offset);if(c==0)c=a.Phase.CompareTo(b.Phase);if(c==0)c=a.Declaration.CompareTo(b.Declaration);return forward?c:-c;});
            foreach(var candidate in candidates){double relative=EventLattice.Relative(candidate.Cycle,candidate.Offset,duration,previous);
                double wall=offset+(requested==0?0:Math.Max(0,Math.Min(1,relative/requested))*delta);
                if(candidate.Key==null)sink.Lifecycle("playbackCompleted",track,entry,wall);else sink.User(track,entry,candidate.Key,wall);
            }
        }
    }
}
