// Generated from specification/runtime/RUNTIME_V2_BINARY_LAYOUT.json.
namespace Cane.Format
{
    internal sealed class ModelRecord
    {
        public string Name = null!;
        public double ReferenceScale;
        public BoneRecord[] Bones = null!;
        public ConstraintRecord[] Constraints = null!;
        public SlotRecord[] Slots = null!;
        public KeyRecord[] Keys = null!;
        public GeometryRecord[] Geometries = null!;
        public AttachmentRecord[] Attachments = null!;
        public SequenceRecord[] Sequences = null!;
        public SkinRecord[] Skins = null!;
        public long[][] DefaultBindings = null!;
        public AnimationRecord[] Animations = null!;
        public uint Curves;
        public TextureRecord[] Textures = null!;
    }
    internal sealed class BoneRecord
    {
        public string Id = null!;
        public string Name = null!;
        public uint? Parent;
        public double Length;
        public double[] Setup = null!;
        public bool SkinRequired;
    }
    internal sealed class SlotRecord
    {
        public string Id = null!;
        public string Name = null!;
        public uint Bone;
        public uint? Attachment;
        public double[] Color = null!;
        public double[]? Dark;
        public uint Blend;
    }
    internal sealed class KeyRecord
    {
        public uint Slot;
        public string Name = null!;
    }
    internal sealed class GeometryRecord
    {
        public uint Positions;
        public WeightsRecord? Weights;
    }
    internal sealed class WeightsRecord
    {
        public uint Bones;
        public uint Values;
        public uint Starts;
    }
    internal abstract class AttachmentRecord
    {
        public abstract string Type { get; }
        public string Id = null!;
        public string Name = null!;
        public uint Geometry;
    }
    internal sealed class MeshAttachmentRecord : AttachmentRecord
    {
        public override string Type => "mesh";
        public uint Texture;
        public uint Uvs;
        public uint Indices;
        public double[] Color = null!;
        public uint? Sequence;
        public double[] Placement = null!;
    }
    internal sealed class BoundingAttachmentRecord : AttachmentRecord
    {
        public override string Type => "bounding";
    }
    internal sealed class PointAttachmentRecord : AttachmentRecord
    {
        public override string Type => "point";
        public double Rotation;
    }
    internal sealed class PathAttachmentRecord : AttachmentRecord
    {
        public override string Type => "path";
        public bool Closed;
        public bool ConstantSpeed;
        public double[] Lengths = null!;
    }
    internal sealed class ClippingAttachmentRecord : AttachmentRecord
    {
        public override string Type => "clipping";
        public uint? EndSlot;
        public bool Convex;
        public bool Inverse;
    }
    internal sealed class SequenceRecord
    {
        public uint SetupIndex;
        public SequenceFrameRecord[] Frames = null!;
    }
    internal sealed class SequenceFrameRecord
    {
        public uint Texture;
        public uint Uvs;
        public uint? Geometry;
    }
    internal sealed class SkinRecord
    {
        public string Id = null!;
        public string Name = null!;
        public long[][] Bindings = null!;
        public uint[] Bones = null!;
        public uint[] Constraints = null!;
    }
    internal sealed class TextureRecord
    {
        public string Id = null!;
        public string Path = null!;
        public uint Width;
        public uint Height;
        public ColorSpaceRecord ColorSpace;
        public AlphaModeRecord AlphaMode;
        public FilterRecord MinFilter;
        public FilterRecord MagFilter;
        public FilterRecord? MipmapFilter;
        public WrapRecord WrapU;
        public WrapRecord WrapV;
    }
    internal enum ColorSpaceRecord { Srgb, Linear }
    internal enum AlphaModeRecord { Straight, Premultiplied }
    internal enum FilterRecord { Nearest, Linear }
    internal enum WrapRecord { Clamp, Repeat, Mirror }
    internal abstract class ConstraintRecord
    {
        public abstract string Type { get; }
        public string Id = null!;
        public string Name = null!;
        public bool SkinRequired;
    }
    internal sealed class IkConstraintRecord : ConstraintRecord
    {
        public override string Type => "ik";
        public uint[] Bones = null!;
        public uint Target;
        public double Mix;
        public double Bend;
        public double Softness;
        public bool Compress;
        public bool Stretch;
        public uint ScaleY;
    }
    internal sealed class TransformConstraintRecord : ConstraintRecord
    {
        public override string Type => "transform";
        public uint Source;
        public uint[] Bones = null!;
        public bool LocalSource;
        public bool LocalTarget;
        public bool Additive;
        public double[] Offsets = null!;
        public double[] Mix = null!;
        public MappingRecord[] Mappings = null!;
    }
    internal sealed class PathConstraintRecord : ConstraintRecord
    {
        public override string Type => "path";
        public uint Slot;
        public uint[] Bones = null!;
        public uint PositionMode;
        public uint SpacingMode;
        public uint RotateMode;
        public double Position;
        public double Spacing;
        public double MixRotate;
        public double MixX;
        public double MixY;
        public double OffsetRotation;
    }
    internal sealed class PhysicsConstraintRecord : ConstraintRecord
    {
        public override string Type => "physics";
        public uint Bone;
        public double X;
        public double Y;
        public double Rotate;
        public double ShearX;
        public double ScaleX;
        public uint ScaleY;
        public double Step;
        public double Limit;
        public double Inertia;
        public double Strength;
        public double Damping;
        public double MassInverse;
        public double Wind;
        public double Gravity;
        public double Mix;
    }
    internal sealed class SliderConstraintRecord : ConstraintRecord
    {
        public override string Type => "slider";
        public uint Animation;
        public double Time;
        public double Mix;
        public bool Additive;
        public bool Looped;
        public SliderSourceRecord? Source;
    }
    internal sealed class MappingRecord
    {
        public uint From;
        public uint To;
        public double FromOffset;
        public double Offset;
        public double Scale;
        public double? Limit;
    }
    internal sealed class SliderSourceRecord
    {
        public uint Bone;
        public uint Property;
        public bool Local;
        public double FromOffset;
        public double Offset;
        public double Scale;
    }
    internal sealed class AnimationRecord
    {
        public string Id = null!;
        public string Name = null!;
        public double Duration;
        public uint[][] Timelines = null!;
        public DeformRecord[] Deforms = null!;
        public SequenceTimelineRecord[] Sequences = null!;
        public DrawOrderRecord[] DrawOrders = null!;
        public PhysicsResetRecord[] PhysicsResets = null!;
        public EventRecord[] Events = null!;
        public SkinKeyRecord[] Skins = null!;
    }
    internal sealed class DeformRecord
    {
        public uint Slot;
        public uint Geometry;
        public uint[] Timing = null!;
        public uint Frames;
        public uint[] Vertices = null!;
        public DeformOffsetRecord? Offset;
    }
    internal sealed class DeformOffsetRecord
    {
        public uint[] Timing = null!;
        public uint Direction;
    }
    internal sealed class SequenceTimelineRecord
    {
        public uint Slot;
        public uint Sequence;
        public SequenceKeyRecord[] Keys = null!;
    }
    internal sealed class SequenceKeyRecord
    {
        public double Time;
        public long Index;
        public uint Mode;
        public double Delay;
    }
    internal sealed class DrawOrderRecord
    {
        public uint[] Slots = null!;
        public DrawKeyRecord[] Keys = null!;
    }
    internal sealed class DrawKeyRecord
    {
        public double Time;
        public uint[] Order = null!;
    }
    internal sealed class PhysicsResetRecord
    {
        public double Time;
        public uint? Constraint;
    }
    internal sealed class EventRecord
    {
        public double Time;
        public string Name = null!;
        public long Integer;
        public double Number;
        public string Text = null!;
        public string? Audio;
        public double Volume;
        public double Balance;
    }
    internal sealed class SkinKeyRecord
    {
        public double Time;
        public uint? Skin;
    }
    internal sealed class ViewRecord
    {
        public TypeRecord Type;
        public EncodingRecord Encoding;
        public uint Offset;
        public uint Length;
        public uint Bytes;
    }
    internal enum TypeRecord { F64, F32, U32 }
    internal enum EncodingRecord { F64, F32, U32, U8, U16, Packed, Curves }
}
