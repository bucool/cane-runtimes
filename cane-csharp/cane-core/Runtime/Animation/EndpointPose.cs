using System;
using System.Collections.Generic;
using Cane.Format;

namespace Cane.Animation
{
    /// <summary>Maps authored channels into an owned pose, without renderer state.</summary>
    internal sealed class EndpointPose
    {
        private readonly Pose pose;
        private readonly AnimationWork work;
        private readonly Dictionary<(int, int, int), int> keys = new Dictionary<(int, int, int), int>();
        private readonly List<Action<PoseValue>> publish = new List<Action<PoseValue>>();
        internal readonly List<PoseChannel> Schema = new List<PoseChannel>();
        internal readonly List<PoseValue> Initial = new List<PoseValue>();
        internal readonly int OrderIndex;
        private static readonly string[] RegionFields = { "x", "y", "rotation", "scaleX", "scaleY" };
        private static readonly string[] ColorFields = { "color_r", "color_g", "color_b", "dark_r", "dark_g", "dark_b" };
        private static readonly Dictionary<string, string[]> ConstraintFields = new Dictionary<string, string[]> {
            ["ik"] = new[]{"targetX","targetY","mix","softness","bendPositive","compress","stretch"},
            ["transform"] = new[]{"mixRotate","mixX","mixY","mixScaleX","mixScaleY","mixShearY"},
            ["path"] = new[]{"position","spacing","mixRotate","mixX","mixY"},
            ["physics"] = new[]{"inertia","strength","damping","mass","wind","gravity","mix"},
            ["slider"] = new[]{"sliderTime","mix"},
        };
        internal EndpointPose(Pose pose, AnimationWork work)
        {
            this.pose = pose; this.work = work; var data = pose.Data;
            for (int i = 0; i < pose.Locals.Length; i++)
            {
                int index = i;
                for (int j = 0; j < 7; j++)
                {
                    int component = j;
                    Add(0, i, j, j == 2 || j >= 5 ? PoseChannelKind.Angle : PoseChannelKind.Scalar,
                        new PoseValue(data.BoneData[i].Setup.Get(j)), new PoseValue(pose.Locals[i].Get(j)), v => pose.Locals[index] = pose.Locals[index].With(component, v.Number));
                }
                Add(0, i, 7, PoseChannelKind.Choice, new PoseValue((object)data.BoneData[i].TransformMode), new PoseValue((object)pose.Modes[i]), v => pose.Modes[index] = (string)v.Choice!);
            }
            for (int i = 0; i < pose.Slots.Length; i++)
            {
                var current = pose.Slots[i]; var setup = data.SlotData[i];
                for (int j = 0; j < 6; j++)
                {
                    int component = j % 3; bool dark = j >= 3;
                    Add(1, i, j, PoseChannelKind.Bounded, new PoseValue(Color(dark ? setup.Dark ?? default : setup.Color, component) / 255f),
                        new PoseValue(Color(dark ? current.Dark ?? default : current.Light, component) / 255f), v => {
                            Rgb before = dark ? current.Dark ?? default : current.Light;
                            var rgb = WithColor(before, component, Byte(v.Number));
                            if (dark) current.Dark = rgb; else current.Light = rgb;
                        });
                }
                Add(1, i, 6, PoseChannelKind.Bounded, new PoseValue(setup.Alpha), new PoseValue(current.Alpha), v => current.Alpha = v.Number);
                Add(1, i, 7, PoseChannelKind.Choice, new PoseValue((object?)setup.Attachment), new PoseValue((object?)current.Key), v => current.Key = (string?)v.Choice);
                Add(1, i, 8, PoseChannelKind.Choice, new PoseValue((object)setup.Dark.HasValue), new PoseValue((object)current.Dark.HasValue), v => { if (!(bool)v.Choice!) current.Dark = null; else if (!current.Dark.HasValue) current.Dark = default(Rgb); });
            }
            for (int i = 0; i < data.AttachmentData.Length; i++)
            {
                int index = i; var a = data.AttachmentData[i];
                if (a.Kind == "region") for (int j = 0; j < 5; j++)
                {
                    int component = j;
                    Add(2, i, j, j == 2 ? PoseChannelKind.Angle : PoseChannelKind.Scalar, new PoseValue(a.Setup.Get(j)), new PoseValue(pose.Regions[i].Get(j)), v => pose.Regions[index] = pose.Regions[index].With(component, v.Number));
                }
                if (a.Kind != "region" && a.Kind != "point" && a.DeformSourceId == a.Id)
                {
                    var source = a.GeometrySource; bool weighted = source.InfluenceStarts.Length != 0;
                    float[] setup = weighted ? new float[source.Influences.Length * 2] : source.Vertices;
                    pose.Deforms.TryGetValue(a.Id, out float[]? current);
                    Add(2, i, 5, PoseChannelKind.Vector, new PoseValue(setup), new PoseValue(current ?? setup), v => pose.Deforms[a.Id] = v.Vector!);
                }
                int count = a.Raw["sequence"]["imageIds"].ArrayOrEmpty.Count;
                if (count != 0)
                {
                    int setup = Math.Max(0, Math.Min(count - 1, a.Raw["sequence"].I("setupIndex")));
                    Add(2, i, 6, PoseChannelKind.Choice, new PoseValue((object)setup), new PoseValue((object)pose.SequenceIndices[i]), v => pose.SequenceIndices[index] = (int)v.Choice!);
                }
            }
            for (int i = 0; i < data.Constraints.Length; i++)
            {
                var setup = data.Constraints[i]; var current = pose.Constraints[i]; string kind = setup.S("type");
                if (kind == "ik" && !current["target"].IsObject) current.Members["target"] = new Json(new Dictionary<string, Json>());
                string[] fields = ConstraintFields[kind];
                for (int j = 0; j < fields.Length; j++)
                {
                    string field = fields[j]; bool boolean = IsBoolean(field);
                    var channelKind = boolean ? PoseChannelKind.Choice : kind == "physics" && (field == "inertia" || field == "damping" || field == "mix" || field == "mass") ? PoseChannelKind.Bounded : PoseChannelKind.Scalar;
                    Add(3, i, j, channelKind, ConstraintValue(setup, field), ConstraintValue(current, field), v => {
                        if (boolean) current.Members[field] = new Json((bool)v.Choice!);
                        else if (field == "targetX" || field == "targetY") Pose.Set(current["target"], field == "targetX" ? "x" : "y", v.Number);
                        else Pose.Set(current, field == "sliderTime" ? "time" : field, v.Number);
                    }, field == "mass" ? float.Epsilon : 0, field == "mass" ? float.MaxValue : 1);
                }
            }
            Add(5, 0, 0, PoseChannelKind.Choice, new PoseValue((object)Array.Empty<string>()), new PoseValue((object)pose.Skins), v => pose.Skins = (string[])v.Choice!);
            var order = new int[data.SlotData.Length]; for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a,b) => { int z = data.SlotData[a].ZIndex.CompareTo(data.SlotData[b].ZIndex); return z == 0 ? a.CompareTo(b) : z; });
            OrderIndex = Add(6, 0, 0, PoseChannelKind.Order, new PoseValue((object)order), new PoseValue((object)pose.Order), v => pose.Order = (int[])v.Choice!);
            Add(6, 0, 1, PoseChannelKind.Choice, new PoseValue((object)false), new PoseValue((object)pose.OrderSampled), v => pose.OrderSampled = (bool)v.Choice!);
        }
        private int Add(int domain, int index, int component, PoseChannelKind kind, PoseValue setup, PoseValue current, Action<PoseValue> setter, float min = 0, float max = 1)
        {
            work.Use(1 + (setup.Vector?.Length ?? 0)); int key = Schema.Count;
            keys.Add((domain,index,component), key); Schema.Add(new PoseChannel(kind,setup,min,max)); Initial.Add(current); publish.Add(setter); return key;
        }
        internal void Finish(PoseValue[] values)
        { work.Use(values.Length); for (int i = 0; i < values.Length; i++) publish[i](values[i]); }
        private static bool IsBoolean(string field) => field == "bendPositive" || field == "compress" || field == "stretch";
        private static PoseValue ConstraintValue(Json constraint, string field)
        {
            if (IsBoolean(field)) return new PoseValue((object)constraint.B(field, field == "bendPositive"));
            if (field == "targetX" || field == "targetY") return new PoseValue(constraint["target"].F(field == "targetX" ? "x" : "y"));
            return new PoseValue(Sampling.ConstraintValue(constraint, field == "sliderTime" ? "time" : field));
        }
        private static byte Color(Rgb color, int component) => component == 0 ? color.R : component == 1 ? color.G : color.B;
        private static Rgb WithColor(Rgb rgb, int component, byte value) => new Rgb(component == 0 ? value : rgb.R, component == 1 ? value : rgb.G, component == 2 ? value : rgb.B);
        private static byte Byte(float value) => (byte)Math.Floor(Numeric.Clamp(value,0,1) * 255f + .5f);
        private static bool Present(Channel channel, float time) => channel.HasKeys && channel.Keys[0].Time <= time;
        internal ChannelSample Sample(Layer? layer)
        {
            var result = new ChannelSample(); if (layer == null) return result;
            var clip = layer.Clip; var data = pose.Data; float time = layer.Time; bool transparent = layer.Alpha == 0;
            result.Weight = layer.Alpha; result.Additive = layer.Additive;
            void Put(int domain, int target, int component, PoseValue value)
            {
                work.Use(); if (!keys.TryGetValue((domain,target,component), out int key)) throw new RuntimeException(RuntimeErrorCode.InvalidArgument,"apply","Animation channel has no setup value.");
                result.Values[key] = transparent ? Schema[key].Setup : value;
            }
            void Scalar(Channel channel, int domain, int target, int component, bool angular = false, bool color = false)
            {
                work.Use(channel.Keys.Length); if (!Present(channel,time)) return;
                if (transparent) { Put(domain,target,component,default); return; }
                float value;
                try { value = channel.Sample(time,angular,layer.Stepped)!.Value; }
                catch (RuntimeException error) when (error.Operation == "sampleCurve")
                {
                    string id = domain == 0 ? data.BoneData[target].Id : domain == 1 ? data.SlotData[target].Id : domain == 2 ? data.AttachmentData[target].Id : data.Constraints[target].S("id");
                    throw new RuntimeException(error.Code, "apply", error.Message, "animationCurve", id);
                }
                Put(domain,target,component,new PoseValue(color ? Byte(value)/255f : value));
            }
            foreach (var t in clip.Bones)
            {
                for (int j=0;j<7;j++) Scalar(t.Channels[j],0,t.Index,j,j==2 || j>=5);
                Json inherit = KeySampling.Discrete(t.Inherit,time); work.Use(t.Inherit.ArrayOrEmpty.Count);
                if (!inherit.IsNull) Put(0,t.Index,7,new PoseValue((object)inherit.S("inherit")));
            }
            foreach (Json t in clip.Raw["slotTimelines"].Items)
            {
                int index = data.SlotIndex[t.S("slotId")]; var channels = clip.Channels[t];
                if (layer.Attachments)
                { Json key=KeySampling.Discrete(t["attachment"],time); work.Use(t["attachment"].ArrayOrEmpty.Count); if(!key.IsNull)Put(1,index,7,new PoseValue((object?)key["attachmentId"].StringOrNull)); }
                for(int j=0;j<6;j++)Scalar(channels[ColorFields[j]],1,index,j,false,true);
                if(Present(channels["color_r"],time))Put(1,index,8,new PoseValue((object)(!transparent && Sampling.SampledDarkExists(t["color"],time))));
                Scalar(channels["alpha"].HasKeys ? channels["alpha"] : channels["color_alpha"],1,index,6);
            }
            foreach(Json t in clip.Raw["attachmentTimelines"].Items)
            {
                int index=data.AttachmentIndex[t.S("attachmentId")]; var a=data.AttachmentData[index]; var channels=clip.Channels[t];
                for(int j=0;j<5;j++)Scalar(channels[RegionFields[j]],2,index,j,j==2);
                Json deform=t["deform"];
                foreach(Json key in deform.ArrayOrEmpty)work.Use(1+key["vertices"].ArrayOrEmpty.Count);
                if(deform.ArrayOrEmpty.Count!=0 && deform[0].F("time")<=time)
                {
                    if(transparent)Put(2,index,5,default);
                    else
                    {
                        if(a.Id!=a.DeformSourceId)throw new RuntimeException(RuntimeErrorCode.InvalidArgument,"apply","Invalid deformation owner.");
                        int length=deform[0]["vertices"].Items.Count; var values=new float[length];
                        for(int j=0;j<length;j++)values[j]=channels["d"+j].Sample(time,false,layer.Stepped)!.Value;
                        if(a.GeometrySource.InfluenceStarts.Length!=0 && t.S("deformSpace","vertexPositions")=="vertexPositions")values=Sampling.CanonicalDeform(a.GeometrySource,values);
                        Put(2,index,5,new PoseValue(values));
                    }
                }
                if(layer.Attachments)
                {
                    Json key=KeySampling.Discrete(t["sequence"],time);work.Use(t["sequence"].ArrayOrEmpty.Count);
                    if(!key.IsNull)
                    {
                        int value=0;
                        if(!transparent)value=SequenceClock.Sample(time,key.F("time"),key.F("delay"),a.Raw["sequence"]["imageIds"].Items.Count,key.I("index"),key.S("mode")) ?? throw new RuntimeException(RuntimeErrorCode.InvalidArgument,"apply","Invalid sequence clock.");
                        Put(2,index,6,new PoseValue((object)value));
                    }
                }
            }
            foreach(Json t in clip.Raw["constraintTimelines"].Items)
            {
                bool wildcard=t.S("type")=="physics" && t.S("constraintId")=="*";
                for(int index=0;index<data.Constraints.Length;index++)
                {
                    Json c=data.Constraints[index];if(c.S("type")!=t.S("type") || (!wildcard && c.S("id")!=t.S("constraintId")))continue;
                    string[] fields=ConstraintFields[c.S("type")];
                    for(int j=0;j<fields.Length;j++)
                    {
                        string field=fields[j];if(wildcard && !c.B(field+"Global"))continue;
                        if(IsBoolean(field))
                        {Json key=KeySampling.Discrete(t["keys"],time,field);work.Use(t["keys"].Items.Count);if(!key.IsNull)Put(3,index,j,new PoseValue((object)key.B(field)));}
                        else if(clip.Channels[t].TryGetValue(field,out Channel? channel))Scalar(channel,3,index,j);
                    }
                }
            }
            if(layer.Attachments)
            {
                Json key=KeySampling.Discrete(clip.Raw["skins"],time);work.Use(clip.Raw["skins"].Items.Count);
                if(!key.IsNull)Put(5,0,0,new PoseValue((object)(key["skinId"].IsNull ? Array.Empty<string>() : new[]{key.S("skinId")})));
            }
            if(layer.DrawOrder)
            {
                void Order(Json rows)
                {
                    work.Use(rows.ArrayOrEmpty.Count);Json key=KeySampling.Discrete(rows,time);if(key.IsNull)return;
                    if(!transparent)
                    {
                        var ids=key["slotIds"].Items;var order=new int[ids.Count];for(int i=0;i<order.Length;i++)order[i]=data.SlotIndex[ids[i].String];result.OrderPatches.Add(order);
                    }
                    Put(6,0,0,new PoseValue((object)Array.Empty<int>()));Put(6,0,1,new PoseValue((object)true));
                }
                Order(clip.Raw["drawOrder"]);foreach(Json folder in clip.Raw["drawOrderFolders"].Items)Order(folder["keys"]);
            }
            return result;
        }
        internal static void ApplyTracks(Pose pose, IEnumerable<Track> tracks, SamplingOptions sampling)
        {
            var work=new AnimationWork();var projection=new EndpointPose(pose,work);PoseValue[] values=projection.Initial.ToArray();
            foreach(var track in tracks)
            {
                var path=new List<(AnimationPlayback Entry,bool Attachments,bool Order)>();bool attachments=true,order=true;
                for(AnimationPlayback? e=track.Current;e!=null;)
                {
                    work.Use();path.Add((e,attachments,order));float progress=e.Progress;if(progress>=1 || e.From==null)break;
                    e=e.From;attachments=progress<e.AttachmentBlendThreshold;order=progress<e.DrawOrderBlendThreshold;
                }
                PoseValue[] underlay=values,outgoing=values;bool first=true;
                for(int i=path.Count;i-->0;)
                {
                    var item=path[i];var e=item.Entry;if(!first && e.Progress==0)continue;
                    var layer=e.Clip==null ? null : new Layer{Clip=e.Clip,Time=sampling.Time(e.PoseTime),Alpha=e.BlendWeight,Additive=e.Additive,Stepped=sampling.Stepped,Attachments=item.Attachments,DrawOrder=item.Order};
                    var sample=projection.Sample(layer);var incoming=PoseChannels.Apply(projection.Schema,underlay,sample,projection.OrderIndex,work);
                    outgoing=first || e.Progress==1 ? incoming : PoseChannels.Transition(projection.Schema,outgoing,incoming,sample,e.Progress,e.Hold,work);first=false;
                }
                values=outgoing;
            }
            projection.Finish(values);
        }
        internal static void ApplyLayer(Pose pose,Layer layer)
        {
            var work=new AnimationWork();var projection=new EndpointPose(pose,work);
            projection.Finish(PoseChannels.Apply(projection.Schema,projection.Initial.ToArray(),projection.Sample(layer),projection.OrderIndex,work));
        }
    }
}
