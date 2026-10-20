// Generated from specification/runtime/RUNTIME_V2_BINARY_LAYOUT.json.
namespace Cane.Format
{
    internal static partial class Records
    {
        internal static ModelRecord ReadModel(BinaryInput r)
        {
            return new ModelRecord { Name = r.String(), ReferenceScale = r.Number(), Bones = r.List(x => ReadBone(x)), Constraints = r.List(x => ReadConstraint(x)), Slots = r.List(x => ReadSlot(x)), Keys = r.List(x => ReadKey(x)), Geometries = r.List(x => ReadGeometry(x)), Attachments = r.List(x => ReadAttachment(x)), Sequences = r.List(x => ReadSequence(x)), Skins = r.List(x => ReadSkin(x)), DefaultBindings = r.List(x => new long[] { x.Integer(), x.Integer() }), Animations = r.List(x => ReadAnimation(x)), Curves = r.UInt(), Textures = r.List(x => ReadTexture(x)) };
        }
        internal static BoneRecord ReadBone(BinaryInput r)
        {
            return new BoneRecord { Id = r.String(), Name = r.String(), Parent = (r.Boolean() ? (uint?)r.UInt() : null), Length = r.Number(), Setup = new double[] { r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number() }, SkinRequired = r.Boolean() };
        }
        internal static SlotRecord ReadSlot(BinaryInput r)
        {
            return new SlotRecord { Id = r.String(), Name = r.String(), Bone = r.UInt(), Attachment = (r.Boolean() ? (uint?)r.UInt() : null), Color = new double[] { r.Number(), r.Number(), r.Number(), r.Number() }, Dark = (r.Boolean() ? (double[]?)ReadRGB(r) : null), Blend = r.UInt() };
        }
        internal static double[] ReadRGB(BinaryInput r)
        {
            return new double[] { r.Number(), r.Number(), r.Number() };
        }
        internal static KeyRecord ReadKey(BinaryInput r)
        {
            return new KeyRecord { Slot = r.UInt(), Name = r.String() };
        }
        internal static GeometryRecord ReadGeometry(BinaryInput r)
        {
            return new GeometryRecord { Positions = r.UInt(), Weights = (r.Boolean() ? (WeightsRecord?)ReadWeights(r) : null) };
        }
        internal static WeightsRecord ReadWeights(BinaryInput r)
        {
            return new WeightsRecord { Bones = r.UInt(), Values = r.UInt(), Starts = r.UInt() };
        }
        internal static AttachmentRecord ReadAttachment(BinaryInput r)
        {
            string id = r.String();
            string name = r.String();
            uint geometry = r.UInt();
            return r.UInt() switch
            {
                0 => new MeshAttachmentRecord { Id = id, Name = name, Geometry = geometry, Texture = r.UInt(), Uvs = r.UInt(), Indices = r.UInt(), Color = new double[] { r.Number(), r.Number(), r.Number(), r.Number() }, Sequence = (r.Boolean() ? (uint?)r.UInt() : null), Placement = new double[] { r.Number(), r.Number() } },
                1 => new BoundingAttachmentRecord { Id = id, Name = name, Geometry = geometry },
                2 => new PointAttachmentRecord { Id = id, Name = name, Geometry = geometry, Rotation = r.Number() },
                3 => new PathAttachmentRecord { Id = id, Name = name, Geometry = geometry, Closed = r.Boolean(), ConstantSpeed = r.Boolean(), Lengths = r.List(x => x.Number()) },
                4 => new ClippingAttachmentRecord { Id = id, Name = name, Geometry = geometry, EndSlot = (r.Boolean() ? (uint?)r.UInt() : null), Convex = r.Boolean(), Inverse = r.Boolean() },
                _ => throw r.Error("Unknown Attachment type")
            };
        }
        internal static SequenceRecord ReadSequence(BinaryInput r)
        {
            return new SequenceRecord { SetupIndex = r.UInt(), Frames = r.List(x => ReadSequenceFrame(x)) };
        }
        internal static SequenceFrameRecord ReadSequenceFrame(BinaryInput r)
        {
            return new SequenceFrameRecord { Texture = r.UInt(), Uvs = r.UInt(), Geometry = (r.Boolean() ? (uint?)r.UInt() : null) };
        }
        internal static SkinRecord ReadSkin(BinaryInput r)
        {
            return new SkinRecord { Id = r.String(), Name = r.String(), Bindings = r.List(x => new long[] { x.Integer(), x.Integer() }), Bones = r.List(x => x.UInt()), Constraints = r.List(x => x.UInt()) };
        }
        internal static TextureRecord ReadTexture(BinaryInput r)
        {
            return new TextureRecord { Id = r.String(), Path = r.String(), Width = r.UInt(), Height = r.UInt(), ColorSpace = ReadColorSpace(r), AlphaMode = ReadAlphaMode(r), MinFilter = ReadFilter(r), MagFilter = ReadFilter(r), MipmapFilter = (r.Boolean() ? (FilterRecord?)ReadFilter(r) : null), WrapU = ReadWrap(r), WrapV = ReadWrap(r) };
        }
        internal static ColorSpaceRecord ReadColorSpace(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 2) throw r.Error("Unknown ColorSpace");
            return (ColorSpaceRecord)value;
        }
        internal static AlphaModeRecord ReadAlphaMode(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 2) throw r.Error("Unknown AlphaMode");
            return (AlphaModeRecord)value;
        }
        internal static FilterRecord ReadFilter(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 2) throw r.Error("Unknown Filter");
            return (FilterRecord)value;
        }
        internal static WrapRecord ReadWrap(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 3) throw r.Error("Unknown Wrap");
            return (WrapRecord)value;
        }
        internal static ConstraintRecord ReadConstraint(BinaryInput r)
        {
            string id = r.String();
            string name = r.String();
            bool skinRequired = r.Boolean();
            return r.UInt() switch
            {
                0 => new IkConstraintRecord { Id = id, Name = name, SkinRequired = skinRequired, Bones = r.List(x => x.UInt()), Target = r.UInt(), Mix = r.Number(), Bend = r.Number(), Softness = r.Number(), Compress = r.Boolean(), Stretch = r.Boolean(), ScaleY = r.UInt() },
                1 => new TransformConstraintRecord { Id = id, Name = name, SkinRequired = skinRequired, Source = r.UInt(), Bones = r.List(x => x.UInt()), LocalSource = r.Boolean(), LocalTarget = r.Boolean(), Additive = r.Boolean(), Offsets = new double[] { r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number() }, Mix = new double[] { r.Number(), r.Number(), r.Number(), r.Number(), r.Number(), r.Number() }, Mappings = r.List(x => ReadMapping(x)) },
                2 => new PathConstraintRecord { Id = id, Name = name, SkinRequired = skinRequired, Slot = r.UInt(), Bones = r.List(x => x.UInt()), PositionMode = r.UInt(), SpacingMode = r.UInt(), RotateMode = r.UInt(), Position = r.Number(), Spacing = r.Number(), MixRotate = r.Number(), MixX = r.Number(), MixY = r.Number(), OffsetRotation = r.Number() },
                3 => new PhysicsConstraintRecord { Id = id, Name = name, SkinRequired = skinRequired, Bone = r.UInt(), X = r.Number(), Y = r.Number(), Rotate = r.Number(), ShearX = r.Number(), ScaleX = r.Number(), ScaleY = r.UInt(), Step = r.Number(), Limit = r.Number(), Inertia = r.Number(), Strength = r.Number(), Damping = r.Number(), MassInverse = r.Number(), Wind = r.Number(), Gravity = r.Number(), Mix = r.Number() },
                4 => new SliderConstraintRecord { Id = id, Name = name, SkinRequired = skinRequired, Animation = r.UInt(), Time = r.Number(), Mix = r.Number(), Additive = r.Boolean(), Looped = r.Boolean(), Source = (r.Boolean() ? (SliderSourceRecord?)ReadSliderSource(r) : null) },
                _ => throw r.Error("Unknown Constraint type")
            };
        }
        internal static MappingRecord ReadMapping(BinaryInput r)
        {
            return new MappingRecord { From = r.UInt(), To = r.UInt(), FromOffset = r.Number(), Offset = r.Number(), Scale = r.Number(), Limit = (r.Boolean() ? (double?)r.Number() : null) };
        }
        internal static SliderSourceRecord ReadSliderSource(BinaryInput r)
        {
            return new SliderSourceRecord { Bone = r.UInt(), Property = r.UInt(), Local = r.Boolean(), FromOffset = r.Number(), Offset = r.Number(), Scale = r.Number() };
        }
        internal static AnimationRecord ReadAnimation(BinaryInput r)
        {
            return new AnimationRecord { Id = r.String(), Name = r.String(), Duration = r.Number(), Timelines = r.List(x => ReadTimeline(x)), Deforms = r.List(x => ReadDeform(x)), Sequences = r.List(x => ReadSequenceTimeline(x)), DrawOrders = r.List(x => ReadDrawOrder(x)), PhysicsResets = r.List(x => ReadPhysicsReset(x)), Events = r.List(x => ReadEvent(x)), Skins = r.List(x => ReadSkinKey(x)) };
        }
        internal static uint[] ReadTimeline(BinaryInput r)
        {
            return new uint[] { r.UInt(), r.UInt(), r.UInt(), r.UInt(), r.UInt() };
        }
        internal static DeformRecord ReadDeform(BinaryInput r)
        {
            return new DeformRecord { Slot = r.UInt(), Geometry = r.UInt(), Timing = ReadTimeline(r), Frames = r.UInt(), Vertices = r.List(x => x.UInt()), Offset = (r.Boolean() ? (DeformOffsetRecord?)ReadDeformOffset(r) : null) };
        }
        internal static DeformOffsetRecord ReadDeformOffset(BinaryInput r)
        {
            return new DeformOffsetRecord { Timing = ReadTimeline(r), Direction = r.UInt() };
        }
        internal static SequenceTimelineRecord ReadSequenceTimeline(BinaryInput r)
        {
            return new SequenceTimelineRecord { Slot = r.UInt(), Sequence = r.UInt(), Keys = r.List(x => ReadSequenceKey(x)) };
        }
        internal static SequenceKeyRecord ReadSequenceKey(BinaryInput r)
        {
            return new SequenceKeyRecord { Time = r.Number(), Index = r.Integer(), Mode = r.UInt(), Delay = r.Number() };
        }
        internal static DrawOrderRecord ReadDrawOrder(BinaryInput r)
        {
            return new DrawOrderRecord { Slots = r.List(x => x.UInt()), Keys = r.List(x => ReadDrawKey(x)) };
        }
        internal static DrawKeyRecord ReadDrawKey(BinaryInput r)
        {
            return new DrawKeyRecord { Time = r.Number(), Order = r.List(x => x.UInt()) };
        }
        internal static PhysicsResetRecord ReadPhysicsReset(BinaryInput r)
        {
            return new PhysicsResetRecord { Time = r.Number(), Constraint = (r.Boolean() ? (uint?)r.UInt() : null) };
        }
        internal static EventRecord ReadEvent(BinaryInput r)
        {
            return new EventRecord { Time = r.Number(), Name = r.String(), Integer = r.Integer(), Number = r.Number(), Text = r.String(), Audio = (r.Boolean() ? (string?)r.String() : null), Volume = r.Number(), Balance = r.Number() };
        }
        internal static SkinKeyRecord ReadSkinKey(BinaryInput r)
        {
            return new SkinKeyRecord { Time = r.Number(), Skin = (r.Boolean() ? (uint?)r.UInt() : null) };
        }
        internal static ViewRecord ReadView(BinaryInput r)
        {
            return new ViewRecord { Type = ReadType(r), Encoding = ReadEncoding(r), Offset = r.UInt(), Length = r.UInt(), Bytes = r.UInt() };
        }
        internal static TypeRecord ReadType(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 3) throw r.Error("Unknown Type");
            return (TypeRecord)value;
        }
        internal static EncodingRecord ReadEncoding(BinaryInput r)
        {
            uint value = r.UInt();
            if (value >= 7) throw r.Error("Unknown Encoding");
            return (EncodingRecord)value;
        }
    }
}
