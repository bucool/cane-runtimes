using System;
using System.Collections.Generic;

namespace Cane
{
    public sealed class AttachmentState
    {
        public int Attachment = -1;
        public Geometry? Geometry;
        public Sequence? Sequence;
        public double SequenceIndex = -1;
        public double[] Deform = Array.Empty<double>();
        public float[] Vertices = Array.Empty<float>();
        internal MeshBuffer? Clipped;
        public readonly Draw Draw;
        internal AttachmentState(int slot) { Draw = new Draw { Slot = slot }; }
    }

    public sealed class AttachmentFrame
    {
        public readonly Appearance Data;
        public readonly List<Draw> Draws;
        public readonly AttachmentState[] Slots, PoseSlots;
        public readonly int[] Bindings, Overrides, Order, AppliedOrder;
        public readonly byte[] Active;
        public Attachment[] Attachments;
        private readonly int[] constrainedSlots;
        private readonly Clipper clipper = new Clipper();
        private Clipper? queryClipper;

        public AttachmentFrame(Appearance data, int[]? constrainedSlots = null, bool constrainedOrder = false)
        {
            Data = data; Attachments = data.Attachments; this.constrainedSlots = constrainedSlots ?? Array.Empty<int>();
            Active = new byte[data.Slots.Length]; Array.Fill(Active, (byte)1);
            Bindings = new int[data.Keys.Length]; Overrides = new int[data.Slots.Length]; Array.Fill(Overrides, -2);
            Order = new int[data.Slots.Length]; Slots = new AttachmentState[data.Slots.Length]; Draws = new List<Draw>(Slots.Length);
            for (int i = 0; i < Order.Length; i++) { Order[i] = i; Slots[i] = new AttachmentState(i); }
            AppliedOrder = constrainedOrder ? (int[])Order.Clone() : Order;
            PoseSlots = this.constrainedSlots.Length > 0 ? (AttachmentState[])Slots.Clone() : Slots;
            foreach (int slot in this.constrainedSlots) PoseSlots[slot] = new AttachmentState(slot);
            SetSkins(default);
        }

        public void SetSkins(ReadOnlySpan<Skin> skins)
        {
            Array.Fill(Bindings, -1);
            foreach (var binding in Data.DefaultBindings) Bindings[binding.Key] = binding.Attachment;
            foreach (var skin in skins) foreach (var binding in skin.Bindings) Bindings[binding.Key] = binding.Attachment;
        }
        public void SetupPose()
        {
            Array.Fill(Overrides, -2);
            for (int i = 0; i < PoseSlots.Length; i++) { Order[i] = i; Array.Clear(PoseSlots[i].Deform, 0, PoseSlots[i].Deform.Length); PoseSlots[i].SequenceIndex = -1; }
        }
        /// <summary>-2 restores skin selection; -1 hides the slot.</summary>
        public void SetAttachment(int slot, int attachment = -2) => Overrides[slot] = attachment;
        public double[]? Deform(int slot, bool applied = false)
        {
            var state = (applied ? Slots : PoseSlots)[slot];
            if (state.Geometry == null) return null;
            if (state.Deform.Length != state.Geometry.Positions.Length) state.Deform = new double[state.Geometry.Positions.Length];
            return state.Deform;
        }
        public Attachment? Resolve(int slot, int key, bool local = false)
        {
            var state = (local ? PoseSlots : Slots)[slot]; int selected = Overrides[slot];
            int attachment = Active[slot] == 0 ? -1 : selected == -2 ? (key < 0 ? -1 : Bindings[key]) : selected;
            state.Attachment = attachment;
            if (attachment < 0) { state.Geometry = null; state.Sequence = null; state.SequenceIndex = -1; return null; }
            var data = Attachments[attachment]; var sequence = (data as MeshAttachment)?.Sequence;
            var timelineSource = sequence?.TimelineSource;
            if (state.Sequence != timelineSource) { state.Sequence = timelineSource; state.SequenceIndex = -1; }
            var geometry = sequence?.Frame(state.SequenceIndex).Geometry ?? data.Geometry;
            if (state.Geometry != geometry)
            {
                state.Geometry = geometry; state.Deform = Array.Empty<double>();
                int count = geometry.VertexCount * 2;
                if (state.Vertices.Length != count) state.Vertices = new float[count];
            }
            return data;
        }

        internal void BeginConstraints(ReadOnlySpan<double> pose, int offset)
        {
            if (AppliedOrder != Order) Order.CopyTo(AppliedOrder, 0);
            foreach (int slot in constrainedSlots)
            {
                Resolve(slot, (int)pose[offset + slot * 8], true);
                var from = PoseSlots[slot]; var to = Slots[slot];
                to.Attachment = from.Attachment; to.Geometry = from.Geometry; to.Sequence = from.Sequence; to.SequenceIndex = from.SequenceIndex;
                if (to.Deform.Length != from.Deform.Length) to.Deform = new double[from.Deform.Length];
                from.Deform.CopyTo(to.Deform, 0);
                int count = (from.Geometry?.VertexCount ?? 0) * 2;
                if (to.Vertices.Length != count) to.Vertices = new float[count];
            }
        }

        public void Update(ReadOnlySpan<double> pose, int offset, ReadOnlySpan<double> world, ReadOnlySpan<double> tint, double scaleX = 1, double scaleY = 1)
        {
            Draws.Clear(); clipper.Active = false;
            foreach (int index in AppliedOrder)
            {
                var slot = Data.Slots[index]; var state = Slots[index]; int p = offset + index * 8;
                var data = Resolve(index, (int)pose[p]);
                if (data == null) { clipper.Finish(index); continue; }
                var draw = state.Draw;
                state.Geometry!.WriteWorld(world, slot.Bone, state.Deform, state.Vertices);
                draw.Vertices = state.Vertices; draw.VertexCount = state.Geometry.VertexCount;
                if (data is ClippingAttachment clip) clipper.Start(state.Vertices, clip.EndSlot, clip.Convex, clip.Inverse);
                if (!(data is MeshAttachment mesh)) { clipper.Finish(index); continue; }
                mesh.Place(state.Vertices, scaleX, scaleY);
                draw.Attachment = state.Attachment; draw.Texture = mesh.Texture; draw.Blend = slot.Blend;
                draw.Uvs = mesh.Uvs; draw.Indices = mesh.Indices;
                if (mesh.Sequence != null) { var selected = mesh.Sequence.Frame(state.SequenceIndex); draw.Texture = selected.Texture; draw.Uvs = selected.Uvs; }
                draw.IndexCount = mesh.Indices.Length; draw.Clipped = clipper.Active;
                if (clipper.Active)
                {
                    var clipped = state.Clipped ??= new MeshBuffer(); clipper.Clip(state.Vertices, draw.Uvs, mesh.Indices, clipped);
                    draw.Vertices = clipped.Vertices; draw.Uvs = clipped.Uvs; draw.Indices = clipped.Indices;
                    draw.VertexCount = clipped.VertexCount; draw.IndexCount = clipped.IndexCount;
                }
                for (int i = 0; i < 4; i++) draw.Light[i] = (float)(pose[p + 1 + i] * tint[i] * mesh.Color[i]);
                for (int i = 0; i < 3; i++) draw.Dark[i] = (float)pose[p + 5 + i];
                draw.Dark[3] = slot.Dark == null ? 0 : 1;
                if (draw.IndexCount > 0) Draws.Add(draw);
                clipper.Finish(index);
            }
        }

        public Bounds? Bounds(int slot)
        {
            var state = Slots[slot];
            if (state.Attachment < 0 || Attachments[state.Attachment] is MeshAttachment && state.Draw.IndexCount == 0) return null;
            return Cane.Bounds.FromVertices(state.Draw.Vertices.AsSpan(0, state.Draw.VertexCount * 2));
        }
        public bool Clipping(int slot, bool before, ClippingBoundary output)
        {
            int active = -1, end = -1;
            foreach (int index in AppliedOrder)
            {
                int attachment = Slots[index].Attachment;
                if (attachment < 0 && end == index) active = -1;
                if (index == slot && before) break;
                if (active < 0 && attachment >= 0 && Attachments[attachment] is ClippingAttachment clip) { active = index; end = clip.EndSlot; }
                if (index == slot) break;
                if (end == index) active = -1;
            }
            if (active < 0 || !(Attachments[Slots[active].Attachment] is ClippingAttachment data)) return false;
            output.Slot = active; output.Attachment = Slots[active].Attachment;
            output.Prepare(Slots[active].Vertices, data.Convex, data.Inverse); return true;
        }
        public int ClippingPolygonCount()
        {
            var scratch = queryClipper ??= new Clipper(); scratch.Active = false; int count = 0;
            foreach (int slot in AppliedOrder)
            {
                int attachment = Slots[slot].Attachment;
                if (!scratch.Active && attachment >= 0 && Attachments[attachment] is ClippingAttachment clip)
                { scratch.Start(Slots[slot].Vertices, clip.EndSlot, clip.Convex, clip.Inverse); count += scratch.PolygonCount; }
                scratch.Finish(slot);
            }
            return count;
        }
        public bool ContainsPoint(int slot, double x, double y)
        {
            var state = Slots[slot]; if (state.Attachment < 0) return false;
            if (!(Attachments[state.Attachment] is MeshAttachment)) return GeometryQueries.ContainsPoint(state.Vertices, x, y);
            var draw = state.Draw; var v = draw.Vertices;
            for (int i = 0; i < draw.IndexCount; i += 3)
            {
                int a = (int)draw.Indices[i] * 2, b = (int)draw.Indices[i + 1] * 2, c = (int)draw.Indices[i + 2] * 2;
                double ab = GeometryQueries.Cross(v[a], v[a + 1], v[b], v[b + 1], x, y);
                double bc = GeometryQueries.Cross(v[b], v[b + 1], v[c], v[c + 1], x, y);
                double ca = GeometryQueries.Cross(v[c], v[c + 1], v[a], v[a + 1], x, y);
                if ((ab >= 0 && bc >= 0 && ca >= 0 || ab <= 0 && bc <= 0 && ca <= 0) && (ab != 0 || bc != 0 || ca != 0)) return true;
            }
            return false;
        }
        public SegmentHit? IntersectSegment(int slot, double ax, double ay, double bx, double by)
        {
            var state = Slots[slot]; if (state.Attachment < 0) return null;
            if (ContainsPoint(slot, ax, ay)) return new SegmentHit(0, ax, ay, 0, 0);
            bool mesh = Attachments[state.Attachment] is MeshAttachment; var draw = state.Draw;
            var vertices = mesh ? draw.Vertices : state.Vertices; int count = mesh ? draw.IndexCount : vertices.Length / 2;
            double time = double.PositiveInfinity, nx = 0, ny = 0;
            for (int i = 0; i < count; i++)
            {
                int a = (mesh ? (int)draw.Indices[i] : i) * 2, b = (mesh ? (int)draw.Indices[i - i % 3 + (i + 1) % 3] : (i + 1) % count) * 2;
                double? hit = GeometryQueries.SegmentIntersection(ax, ay, bx, by, vertices[a], vertices[a + 1], vertices[b], vertices[b + 1]);
                if (hit.HasValue && hit.Value < time) { time = hit.Value; nx = (double)vertices[b + 1] - vertices[a + 1]; ny = (double)vertices[a] - vertices[b]; }
            }
            if (double.IsPositiveInfinity(time)) return null;
            double length = Affine.Hypot(nx, ny), sign = nx * (bx - ax) + ny * (by - ay) > 0 ? -1 : 1;
            return new SegmentHit(time, ax + (bx - ax) * time, ay + (by - ay) * time, length == 0 ? 0 : nx / length * sign, length == 0 ? 0 : ny / length * sign);
        }
    }
}
