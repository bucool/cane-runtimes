using System;

namespace Cane
{
    public enum BlendMode { Normal, Additive, Multiply, Screen }
    public enum SlotChannel { Attachment, R, G, B, A, DarkR, DarkG, DarkB }

    public sealed class Slot
    {
        public readonly string Name;
        public readonly int Bone;
        public int Attachment = -1;
        public double[] Color = { 1, 1, 1, 1 };
        public double[]? Dark;
        public BlendMode Blend;
        public Slot(string name, int bone) { Name = name; Bone = bone; }
    }
    public readonly struct AttachmentKey
    {
        public readonly int Slot;
        public readonly string Name;
        public AttachmentKey(int slot, string name) { Slot = slot; Name = name; }
    }
    public readonly struct SkinBinding
    {
        public readonly int Key, Attachment;
        public SkinBinding(int key, int attachment) { Key = key; Attachment = attachment; }
    }
    public sealed class Skin
    {
        public readonly string Name;
        public readonly SkinBinding[] Bindings;
        public int[] Bones = Array.Empty<int>(), Constraints = Array.Empty<int>();
        public Skin(string name, SkinBinding[] bindings) { Name = name; Bindings = bindings; }
    }

    public abstract class Attachment
    {
        public readonly string Name;
        public readonly Geometry Geometry;
        protected Attachment(string name, Geometry geometry) { Name = name; Geometry = geometry; }
    }
    public sealed class MeshAttachment : Attachment
    {
        public int Texture;
        public readonly float[] Uvs;
        public readonly uint[] Indices;
        public double[] Color = { 1, 1, 1, 1 };
        public Sequence? Sequence;
        public double PlacementX, PlacementY;
        public MeshAttachment(string name, Geometry geometry, int texture, float[] uvs, uint[] indices) : base(name, geometry)
        { Texture = texture; Uvs = uvs; Indices = indices; }
        /// <summary>Remaps texture coordinates once, retaining deformation and sequence timeline identity.
        /// Geometry and indices remain shared; the result owns its color and remapped sequence frames.</summary>
        public MeshAttachment RemapTextures(Func<int, float[], (int Texture, float[] Uvs)> map)
        {
            var mapped = map(Texture, Uvs);
            var copy = new MeshAttachment(Name, Geometry, mapped.Texture, mapped.Uvs, Indices) {
                Color = (double[])Color.Clone(), PlacementX = PlacementX, PlacementY = PlacementY, Sequence = Sequence
            };
            if (Sequence != null)
            {
                var frames = new SequenceFrame[Sequence.Frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = Sequence.Frames[i]; mapped = map(frame.Texture, frame.Uvs);
                    frames[i] = new SequenceFrame(mapped.Texture, mapped.Uvs, frame.Geometry);
                }
                copy.Sequence = new Sequence(frames, Sequence);
            }
            return copy;
        }
        internal void Place(Span<float> vertices, double scaleX, double scaleY)
        {
            if (PlacementX == 0 && PlacementY == 0) return;
            double x = PlacementX * scaleX, y = PlacementY * scaleY;
            for (int i = 0; i < vertices.Length; i += 2) { vertices[i] = (float)(vertices[i] + x); vertices[i + 1] = (float)(vertices[i + 1] + y); }
        }
    }
    public sealed class BoundingAttachment : Attachment
    { public BoundingAttachment(string name, Geometry geometry) : base(name, geometry) { } }
    public sealed class PointAttachment : Attachment
    {
        public double Rotation;
        public PointAttachment(string name, Geometry geometry, double rotation = 0) : base(name, geometry) { Rotation = rotation; }
    }
    public sealed class PathAttachment : Attachment
    {
        public bool Closed, ConstantSpeed = true;
        public double[] Lengths = Array.Empty<double>();
        public PathAttachment(string name, Geometry geometry) : base(name, geometry) { }
    }
    public sealed class ClippingAttachment : Attachment
    {
        public int EndSlot = -1;
        public bool Convex, Inverse;
        public ClippingAttachment(string name, Geometry geometry) : base(name, geometry) { }
    }
    public sealed class Appearance
    {
        public readonly Slot[] Slots;
        public readonly AttachmentKey[] Keys;
        public readonly Attachment[] Attachments;
        public Skin[] Skins = Array.Empty<Skin>();
        public SkinBinding[] DefaultBindings = Array.Empty<SkinBinding>();
        public Appearance(Slot[] slots, AttachmentKey[] keys, Attachment[] attachments) { Slots = slots; Keys = keys; Attachments = attachments; }
    }

    /// <summary>Unrotated pixel dimensions; trim OffsetY is measured from the image bottom.</summary>
    public readonly struct AtlasRegion
    {
        public readonly int Texture, Rotation;
        public readonly double U, V, U2, V2, OriginalWidth, OriginalHeight, Width, Height, OffsetX, OffsetY;
        public AtlasRegion(int texture, double u, double v, double u2, double v2, double originalWidth, double originalHeight,
            double width, double height, double offsetX = 0, double offsetY = 0, int rotation = 0)
        { Texture = texture; U = u; V = v; U2 = u2; V2 = v2; OriginalWidth = originalWidth; OriginalHeight = originalHeight; Width = width; Height = height; OffsetX = offsetX; OffsetY = offsetY; Rotation = rotation; }
        public MeshAttachment CreateAttachment(string name, double? width = null, double? height = null, double x = 0, double y = 0, double rotation = 0)
        {
            double w = width ?? OriginalWidth, h = height ?? OriginalHeight;
            double left = (OffsetX / OriginalWidth - .5) * w, bottom = (OffsetY / OriginalHeight - .5) * h;
            double right = left + Width / OriginalWidth * w, top = bottom + Height / OriginalHeight * h;
            double[] positions = { left, bottom, right, bottom, right, top, left, top };
            double angle = rotation * Math.PI / 180, cosine = Math.Cos(angle), sine = Math.Sin(angle);
            for (int i = 0; i < 8; i += 2)
            { double px = positions[i], py = positions[i + 1]; positions[i] = x + px * cosine - py * sine; positions[i + 1] = y + px * sine + py * cosine; }
            var uvs = new float[8];
            for (int i = 0; i < 4; i++)
            {
                double u = i == 1 || i == 2 ? 1 : 0, v = i < 2 ? 1 : 0;
                for (int turn = 0; turn < Rotation; turn += 90) { double old = u; u = 1 - v; v = old; }
                uvs[i * 2] = (float)(U + u * (U2 - U)); uvs[i * 2 + 1] = (float)(V + v * (V2 - V));
            }
            return new MeshAttachment(name, new Geometry(positions), Texture, uvs, new uint[] { 0, 1, 2, 2, 3, 0 });
        }
    }

    /// <summary>Final render inputs, owned and reused by one instance until its next evaluation.</summary>
    public sealed class Draw
    {
        public int Slot, Attachment = -1, Texture, VertexCount, IndexCount;
        public BlendMode Blend;
        public float[] Vertices = Array.Empty<float>(), Uvs = Array.Empty<float>();
        public uint[] Indices = Array.Empty<uint>();
        public readonly float[] Light = new float[4], Dark = new float[4];
        public bool Clipped;
    }

    public sealed class ClippingBoundary
    {
        public int Slot = -1, Attachment = -1;
        public bool Inverse;
        private readonly Clipper clipper = new Clipper();
        public ReadOnlySpan<float> Vertices => clipper.Boundary;
        public int PolygonCount => clipper.PolygonCount;
        /// <summary>Triangle indices into Vertices, using the same partition as Core clipping.</summary>
        public ReadOnlySpan<int> Triangles => clipper.BoundaryTriangles();
        public bool ContainsPoint(double x, double y)
        {
            var vertices = Vertices; var triangles = Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i] * 2, b = triangles[i + 1] * 2, c = triangles[i + 2] * 2;
                double ab = GeometryQueries.Cross(vertices[a], vertices[a + 1], vertices[b], vertices[b + 1], x, y);
                double bc = GeometryQueries.Cross(vertices[b], vertices[b + 1], vertices[c], vertices[c + 1], x, y);
                double ca = GeometryQueries.Cross(vertices[c], vertices[c + 1], vertices[a], vertices[a + 1], x, y);
                if (GeometryQueries.Cross(vertices[a], vertices[a + 1], vertices[b], vertices[b + 1], vertices[c], vertices[c + 1]) != 0 &&
                    (ab >= 0 && bc >= 0 && ca >= 0 || ab <= 0 && bc <= 0 && ca <= 0)) return !Inverse;
            }
            return Inverse;
        }
        internal void Prepare(float[] vertices, bool convex, bool inverse)
        { Inverse = inverse; clipper.Active = false; clipper.Start(vertices, -1, convex, inverse); }
    }
}
