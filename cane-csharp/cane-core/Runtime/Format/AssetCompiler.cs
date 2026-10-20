using System;
using System.IO;

namespace Cane.Format
{
    internal static class AssetCompiler
    {
        private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
        private static int Index(long value, int count)
        {
            if (value < 0 || value >= count) throw new InvalidDataException($"Runtime index {value} is outside {count} entries");
            return (int)value;
        }
        public static RuntimeAsset Compile(RuntimeDocument document)
        {
            var m = document.Model;
            T[] Buffer<T>(uint index)
            {
                if (!(document.Buffers[Index(index, document.Buffers.Length)].Values is T[] values)) throw new InvalidDataException("Wrong numeric buffer type");
                return values;
            }
            int BoneIndex(uint value) => Index(value, m.Bones.Length);
            int SlotIndex(uint value) => Index(value, m.Slots.Length);
            var bones = new Bone[m.Bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = m.Bones[i]; var p = b.Setup;
                Check(p.Length == 8 && p[7] == Math.Truncate(p[7]) && p[7] >= 0 && p[7] < 5, "Invalid bone setup");
                bones[i] = new Bone(b.Name, b.Parent.HasValue ? Index(b.Parent.Value, i) : -1)
                { Length = b.Length, SkinRequired = b.SkinRequired, X = p[0], Y = p[1], Rotation = p[2], ScaleX = p[3], ScaleY = p[4], ShearX = p[5], ShearY = p[6], Inherit = (Inherit)p[7] };
            }
            var geometries = Array.ConvertAll(m.Geometries, g =>
            {
                var positions = Buffer<double>(g.Positions); Check(positions.Length % 2 == 0, "Odd geometry coordinate count");
                if (g.Weights == null) return new Geometry(positions);
                var w = g.Weights; var indices = Buffer<uint>(w.Bones); var values = Buffer<double>(w.Values); var starts = Buffer<uint>(w.Starts);
                Check(indices.Length == values.Length && indices.Length * 2 == positions.Length && starts.Length > 0 && starts[0] == 0 && starts[starts.Length - 1] == indices.Length, "Invalid weighted geometry");
                for (int i = 1; i < starts.Length; i++) Check(starts[i] > starts[i - 1], "Empty or unordered influence range");
                foreach (uint bone in indices) BoneIndex(bone);
                return new Geometry(positions, indices, values, starts);
            });
            Geometry Geometry(uint value) => geometries[Index(value, geometries.Length)];
            var sequences = Array.ConvertAll(m.Sequences, s =>
            {
                int setup = Index(s.SetupIndex, s.Frames.Length);
                var frames = Array.ConvertAll(s.Frames, f => new SequenceFrame(Index(f.Texture, m.Textures.Length), Buffer<float>(f.Uvs), f.Geometry.HasValue ? Geometry(f.Geometry.Value) : null));
                return new Sequence(frames, setup);
            });
            var attachments = Array.ConvertAll<AttachmentRecord, Attachment>(m.Attachments, a =>
            {
                var g = Geometry(a.Geometry);
                switch (a)
                {
                    case MeshAttachmentRecord mesh:
                        var uv = Buffer<float>(mesh.Uvs); var indices = Buffer<uint>(mesh.Indices);
                        Check(uv.Length == g.VertexCount * 2 && indices.Length % 3 == 0, "Invalid mesh indices or UVs");
                        foreach (uint i in indices) Index(i, g.VertexCount);
                        var sequence = mesh.Sequence.HasValue ? sequences[Index(mesh.Sequence.Value, sequences.Length)] : null;
                        if (sequence != null) foreach (var frame in sequence.Frames) Check(frame.Uvs.Length == uv.Length && (frame.Geometry == null || frame.Geometry.VertexCount == g.VertexCount), "Sequence changes mesh topology");
                        return new MeshAttachment(a.Name, g, Index(mesh.Texture, m.Textures.Length), uv, indices)
                        { Color = mesh.Color, Sequence = sequence, PlacementX = mesh.Placement[0], PlacementY = mesh.Placement[1] };
                    case PointAttachmentRecord point:
                        Check(g.VertexCount == 1, "Point must have one vertex"); return new PointAttachment(a.Name, g, point.Rotation);
                    case BoundingAttachmentRecord _: return new BoundingAttachment(a.Name, g);
                    case ClippingAttachmentRecord clip: return new ClippingAttachment(a.Name, g)
                        { EndSlot = clip.EndSlot.HasValue ? SlotIndex(clip.EndSlot.Value) : -1, Convex = clip.Convex, Inverse = clip.Inverse };
                    case PathAttachmentRecord path:
                        int count = g.VertexCount;
                        Check(count == 0 || count >= (path.Closed ? 3 : 4) && (count - (path.Closed ? 0 : 1)) % 3 == 0, "Invalid cubic path layout");
                        Check(path.ConstantSpeed || path.Lengths.Length >= count / 3, "Missing path lengths");
                        return new PathAttachment(a.Name, g) { Closed = path.Closed, ConstantSpeed = path.ConstantSpeed, Lengths = path.Lengths };
                    default: throw new InvalidDataException("Unsupported attachment type");
                }
            });
            var slots = Array.ConvertAll(m.Slots, s => new Slot(s.Name, BoneIndex(s.Bone))
            { Color = s.Color, Dark = s.Dark, Blend = (BlendMode)Index(s.Blend, 4), Attachment = s.Attachment.HasValue ? Index(s.Attachment.Value, m.Keys.Length) : -1 });
            var keys = Array.ConvertAll(m.Keys, k => new AttachmentKey(SlotIndex(k.Slot), k.Name));
            SkinBinding[] Bindings(long[][] source) => Array.ConvertAll(source, b => new SkinBinding(Index(b[0], keys.Length), b[1] == -1 ? -1 : Index(b[1], attachments.Length)));
            var skins = Array.ConvertAll(m.Skins, s => new Skin(s.Name, Bindings(s.Bindings))
            { Bones = Array.ConvertAll(s.Bones, BoneIndex), Constraints = Array.ConvertAll(s.Constraints, c => Index(c, m.Constraints.Length)) });
            var appearance = new Appearance(slots, keys, attachments) { Skins = skins, DefaultBindings = Bindings(m.DefaultBindings) };
            int channels = bones.Length * 8 + slots.Length * 8;
            foreach (var c in m.Constraints) channels += c is SliderConstraintRecord ? 2 : c is PathConstraintRecord ? 5 : c is PhysicsConstraintRecord ? 7 : 6;
            var curves = Buffer<double>(m.Curves);
            Timeline Timeline(uint[] t, bool auxiliary = false)
            {
                int channel = Index(t[0], auxiliary ? Math.Max(channels, 1) : channels), kind = Index(t[1], 5), offset = Index(t[2], curves.Length + 1);
                Check((t[4] == 2 || t[4] == 8) && (long)offset + (long)t[3] * (1 + t[4]) <= curves.Length, "Invalid timeline slice");
                return new Timeline(channel, (ChannelKind)kind, curves, offset, (int)t[3], (int)t[4]);
            }
            var animations = Array.ConvertAll(m.Animations, a =>
            {
                var timelines = Array.ConvertAll(a.Timelines, t => Timeline(t));
                var deforms = Array.ConvertAll(a.Deforms, d =>
                {
                    var g = Geometry(d.Geometry); var timing = Timeline(d.Timing, true); var frames = Buffer<uint>(d.Frames);
                    var vertices = Array.ConvertAll(d.Vertices, Buffer<double>);
                    Check(frames.Length == timing.Count, "Invalid deform frame count");
                    foreach (var v in vertices) Check(v.Length == g.Positions.Length, "Invalid deform vertex count");
                    foreach (uint f in frames) Index(f, vertices.Length);
                    DeformOffset? offset = d.Offset == null ? null : new DeformOffset(Timeline(d.Offset.Timing, true), Buffer<double>(d.Offset.Direction));
                    Check(offset == null || offset.Direction.Length == g.Positions.Length, "Invalid deform offset direction");
                    return new DeformTimeline(SlotIndex(d.Slot), g, timing, frames, vertices, offset);
                });
                var sequenceTimelines = Array.ConvertAll(a.Sequences, s => new SequenceTimeline(SlotIndex(s.Slot), sequences[Index(s.Sequence, sequences.Length)],
                    Array.ConvertAll(s.Keys, k => new SequenceKey(k.Time, k.Index, (SequenceMode)Index(k.Mode, 7), k.Delay))));
                var orders = Array.ConvertAll(a.DrawOrders, o =>
                {
                    var group = Array.ConvertAll(o.Slots, SlotIndex); var sorted = (int[])group.Clone(); Array.Sort(sorted);
                    for (int i = 1; i < sorted.Length; i++) Check(sorted[i] != sorted[i - 1], "Duplicate draw order member");
                    var orderKeys = Array.ConvertAll(o.Keys, k =>
                    {
                        var order = Array.ConvertAll(k.Order, SlotIndex); var comparison = (int[])order.Clone(); Array.Sort(comparison);
                        Check(comparison.AsSpan().SequenceEqual(sorted), "Draw order key is not a permutation");
                        return new DrawOrderKey(k.Time, order);
                    });
                    return new DrawOrderTimeline(group, slots.Length, orderKeys);
                });
                var resets = Array.ConvertAll(a.PhysicsResets, k =>
                {
                    int target = k.Constraint.HasValue ? Index(k.Constraint.Value, m.Constraints.Length) : -1;
                    Check(target == -1 || m.Constraints[target] is PhysicsConstraintRecord, "Reset targets non-physics constraint");
                    return new PhysicsResetKey(k.Time, target);
                });
                var events = Array.ConvertAll(a.Events, e =>
                {
                    Check(e.Integer >= int.MinValue && e.Integer <= int.MaxValue, "Event integer out of range");
                    return new EventKey(e.Time, e.Name, (int)e.Integer, e.Number, e.Text, e.Audio, e.Volume, e.Balance);
                });
                var skinKeys = Array.ConvertAll(a.Skins, k => new SkinKey(k.Time, k.Skin.HasValue ? Index(k.Skin.Value, skins.Length) : -1));
                return new Animation(a.Name, timelines, a.Duration, deforms, sequenceTimelines, orders, events, resets, skinKeys);
            });
            var constraints = Array.ConvertAll<ConstraintRecord, Constraint>(m.Constraints, c =>
            {
                Constraint value;
                switch (c)
                {
                    case IkConstraintRecord ik:
                        var chain = Array.ConvertAll(ik.Bones, BoneIndex); Check(chain.Length > 0, "Empty IK chain");
                        for (int i = 1; i < chain.Length; i++) Check(bones[chain[i]].Parent == chain[i - 1], "Disconnected IK chain");
                        value = new IkConstraint(c.Name, chain, BoneIndex(ik.Target))
                        { Mix = ik.Mix, Bend = ik.Bend, Softness = ik.Softness, Compress = ik.Compress, Stretch = ik.Stretch, ScaleY = (ScaleYMode)Index(ik.ScaleY, 3) }; break;
                    case TransformConstraintRecord transform:
                        value = new TransformConstraint(c.Name, BoneIndex(transform.Source), Array.ConvertAll(transform.Bones, BoneIndex))
                        {
                            LocalSource = transform.LocalSource, LocalTarget = transform.LocalTarget, Additive = transform.Additive, Offsets = transform.Offsets, Mix = transform.Mix,
                            Mappings = Array.ConvertAll(transform.Mappings, p => new PropertyMapping((TransformProperty)Index(p.From, 6), (TransformProperty)Index(p.To, 6), p.FromOffset, p.Offset, p.Scale, p.Limit))
                        }; break;
                    case PathConstraintRecord path:
                        value = new PathConstraint(c.Name, SlotIndex(path.Slot), Array.ConvertAll(path.Bones, BoneIndex))
                        {
                            PositionMode = (PathPosition)Index(path.PositionMode, 2), SpacingMode = (PathSpacing)Index(path.SpacingMode, 4), RotateMode = (PathRotate)Index(path.RotateMode, 3),
                            Position = path.Position, Spacing = path.Spacing, MixRotate = path.MixRotate, MixX = path.MixX, MixY = path.MixY, OffsetRotation = path.OffsetRotation
                        }; break;
                    case PhysicsConstraintRecord physics:
                        Check(physics.Step > 0 && !double.IsInfinity(physics.Step), "Physics step must be positive");
                        value = new PhysicsConstraint(c.Name, BoneIndex(physics.Bone))
                        {
                            X = physics.X, Y = physics.Y, Rotate = physics.Rotate, ShearX = physics.ShearX, ScaleX = physics.ScaleX, ScaleY = (ScaleYMode)Index(physics.ScaleY, 3),
                            Step = physics.Step, Limit = physics.Limit, Inertia = physics.Inertia, Strength = physics.Strength, Damping = physics.Damping,
                            MassInverse = physics.MassInverse, Wind = physics.Wind, Gravity = physics.Gravity, Mix = physics.Mix
                        }; break;
                    case SliderConstraintRecord slider:
                        var source = slider.Source;
                        value = new SliderConstraint(c.Name, animations[Index(slider.Animation, animations.Length)])
                        {
                            Time = slider.Time, Mix = slider.Mix, Additive = slider.Additive, Loop = slider.Looped,
                            Source = source == null ? null : new SliderSource(BoneIndex(source.Bone), (TransformProperty)Index(source.Property, 6))
                            { Local = source.Local, FromOffset = source.FromOffset, Offset = source.Offset, Scale = source.Scale }
                        }; break;
                    default: throw new InvalidDataException("Unsupported constraint type");
                }
                value.SkinRequired = c.SkinRequired; return value;
            });
            var textures = Array.ConvertAll(m.Textures, t => new TextureInfo(t.Id, t.Path, t.Width, t.Height, (ColorSpace)t.ColorSpace,
                (AlphaMode)t.AlphaMode, (TextureFilter)t.MinFilter, (TextureFilter)t.MagFilter, (TextureFilter?)t.MipmapFilter, (TextureWrap)t.WrapU, (TextureWrap)t.WrapV));
            return new RuntimeAsset(m, new SkeletonData(bones, animations, constraints, appearance, m.ReferenceScale), textures, animations);
        }
    }
}
