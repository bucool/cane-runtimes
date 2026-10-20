using System;

namespace Cane
{
    /// <summary>One instance owns mutable pose, constraints and final geometry.</summary>
    public sealed class Skeleton
    {
        public readonly SkeletonData Data;
        public readonly double[] Pose, World;
        internal double[] Working;
        internal readonly double[] IkPose, SourceTransform = new double[6];
        private readonly byte[] dirtyLocal, previousConstraints;
        private readonly double[] localBases;
        private readonly SliderState?[] sliders;
        private readonly PathState?[] paths;
        public readonly PhysicsState?[] Physics;
        public readonly double[] Wind = { 1, 0 }, Gravity = { 0, -1 }, Tint = { 1, 1, 1, 1 };
        public readonly AttachmentFrame? Frame;
        public readonly byte[] ActiveBones, ActiveConstraints;
        private Skin[] hostSkins = Array.Empty<Skin>();
        private readonly Skin[] animationSkins = new Skin[1];
        private int skinOverride = -2;
        private int[] schedule;
        private int scheduleCount;
        private BoneControl?[]? controls;
        private double[]? controlledPose;
        private int controlCount;
        public double X, Y, ScaleX = 1, ScaleY = 1;

        public Skeleton(SkeletonData data)
        {
            Data = data; Pose = (double[])data.Setup.Clone(); World = new double[data.Parents.Length * 6];
            dirtyLocal = new byte[data.Parents.Length]; localBases = new double[data.Parents.Length * 9]; Array.Fill(localBases, double.NaN);
            int longest = 0;
            foreach (var c in data.Constraints) if (c is IkConstraint ik && ik.Bones.Length > 2) longest = Math.Max(longest, ik.Bones.Length * 3);
            IkPose = new double[longest]; Working = data.Constraints.Length == 0 ? Pose : new double[Pose.Length];
            int count = data.Constraints.Length; sliders = new SliderState?[count]; paths = new PathState?[count]; Physics = new PhysicsState?[count];
            for (int i = 0; i < count; i++)
            {
                if (data.Constraints[i] is SliderConstraint slider) sliders[i] = new SliderState(slider.Animation);
                else if (data.Constraints[i] is PathConstraint path) paths[i] = new PathState(path.Bones.Length);
                else if (data.Constraints[i] is PhysicsConstraint) Physics[i] = new PhysicsState();
            }
            Frame = data.Appearance == null ? null : new AttachmentFrame(data.Appearance, data.SliderSlots, data.SliderOrder);
            ActiveBones = new byte[data.Parents.Length]; ActiveConstraints = new byte[count]; previousConstraints = new byte[count];
            schedule = new int[data.Schedule.Length]; SetSkins(default); UpdateWorld(0, PhysicsMode.Reset); UpdateGeometry();
        }
        public void SetupPose() { Data.Setup.CopyTo(Pose, 0); Frame?.SetupPose(); }
        public BoneControl? GetBoneControl(int bone) => controls?[bone];
        /// <summary>Replace a bone's optional pre-constraint input. Null releases it.
        /// Controls are evaluated once per frame against the current parent basis.</summary>
        public void SetBoneControl(int bone, BoneControl? control)
        {
            if (controls == null)
            {
                if (control == null) return;
                controls = new BoneControl[Data.Parents.Length];
            }
            bool changed = (controls[bone] == null) != (control == null);
            if (controls[bone] == null && control != null) controlCount++;
            else if (controls[bone] != null && control == null) controlCount--;
            controls[bone] = control;
            if (changed) RebuildSchedule();
        }
        public void SetSkins(ReadOnlySpan<Skin> skins) { hostSkins = skins.ToArray(); skinOverride = -2; ApplySkins(skins); }
        public int AnimationSkin => skinOverride;
        internal void SetAnimationSkin(int skin)
        {
            if (skin == skinOverride) return;
            skinOverride = skin;
            if (skin == -2) ApplySkins(hostSkins);
            else if (skin == -1) ApplySkins(default);
            else { animationSkins[0] = Data.Appearance!.Skins[skin]; ApplySkins(animationSkins); }
        }
        private void ApplySkins(ReadOnlySpan<Skin> skins)
        {
            ActiveConstraints.CopyTo(previousConstraints, 0); Frame?.SetSkins(skins);
            for (int i = 0; i < ActiveBones.Length; i++) ActiveBones[i] = (byte)(1 - Data.SkinRequired[i]);
            for (int i = 0; i < ActiveConstraints.Length; i++) ActiveConstraints[i] = (byte)(Data.Constraints[i].SkinRequired ? 0 : 1);
            foreach (var skin in skins)
            {
                foreach (int bone in skin.Bones) for (int b = bone; b >= 0; b = Data.Parents[b]) ActiveBones[b] = 1;
                foreach (int c in skin.Constraints) ActiveConstraints[c] = 1;
            }
            for (int i = 0; i < ActiveConstraints.Length; i++)
            {
                int source = Data.Constraints[i] switch {
                    IkConstraint c => c.Target, PathConstraint c => Data.Appearance!.Slots[c.Slot].Bone,
                    PhysicsConstraint c => c.Bone, TransformConstraint c => c.Source, SliderConstraint c => c.Source?.Bone ?? -1, _ => -1 };
                if (source >= 0 && ActiveBones[source] == 0) ActiveConstraints[i] = 0;
                if (ActiveConstraints[i] != 0 && previousConstraints[i] == 0) Physics[i]?.Reset();
            }
            if (Frame != null) for (int i = 0; i < Frame.Active.Length; i++) Frame.Active[i] = ActiveBones[Frame.Data.Slots[i].Bone];
            RebuildSchedule();
        }
        public int AddAttachment(Attachment attachment)
        {
            var frame = Frame!; int index = frame.Attachments.Length;
            var attachments = new Attachment[index + 1]; frame.Attachments.CopyTo(attachments, 0); attachments[index] = attachment; frame.Attachments = attachments;
            if (attachment is PathAttachment) RebuildSchedule();
            return index;
        }
        /// <summary>Replaces an instance attachment without changing shared data or skin bindings.</summary>
        public void ReplaceAttachment(int index, Attachment attachment)
        {
            var frame = Frame!; var previous = frame.Attachments[index];
            if (frame.Attachments == frame.Data.Attachments) frame.Attachments = (Attachment[])frame.Attachments.Clone();
            frame.Attachments[index] = attachment;
            if (previous is PathAttachment || attachment is PathAttachment) RebuildSchedule();
        }
        private void RebuildSchedule()
        {
            var plan = Data.Schedule;
            bool attachments = Frame != null && Frame.Attachments != Frame.Data.Attachments;
            if (attachments || controlCount != 0)
                plan = ConstraintSchedule.Compile(Data.Parents, Data.Constraints,
                    attachments ? new Appearance(Frame!.Data.Slots, Frame.Data.Keys, Frame.Attachments) : Data.Appearance,
                    controlCount == 0 ? null : controls);
            if (schedule.Length < plan.Length) schedule = new int[plan.Length];
            scheduleCount = 0;
            foreach (int step in plan)
            {
                bool active = step >= 0 ? ActiveBones[step] != 0 : ~step < Data.Constraints.Length ? ActiveConstraints[~step] != 0 : ActiveBones[(~step - Data.Constraints.Length) % ActiveBones.Length] != 0;
                if (active) schedule[scheduleCount++] = step;
            }
        }
        public void UpdateWorld(double delta = 0, PhysicsMode? mode = null)
        {
            if (Data.Constraints.Length == 0)
                Working = controlCount == 0 ? Pose : controlledPose ?? (controlledPose = new double[Pose.Length]);
            if (Working != Pose) Pose.CopyTo(Working, 0);
            Array.Clear(dirtyLocal, 0, dirtyLocal.Length);
            if (Data.SliderSlots.Length != 0 || Data.SliderOrder) Frame?.BeginConstraints(Pose, Data.SlotOffset);
            for (int i = 0; i < scheduleCount; i++)
            {
                int step = schedule[i];
                if (step >= 0) { UpdateBone(step); continue; }
                int c = ~step;
                if (c >= Data.Constraints.Length)
                {
                    int bone = c - Data.Constraints.Length;
                    if (bone < Data.Parents.Length) EnsureLocal(bone);
                    else controls![bone - Data.Parents.Length]!.Apply(this, bone - Data.Parents.Length);
                    continue;
                }
                int offset = Data.ConstraintOffsets[c];
                switch (Data.Constraints[c])
                {
                    case IkConstraint ik: IkSolver.Apply(this, ik, offset); break;
                    case TransformConstraint transform: TransformSolver.Apply(this, transform, offset); break;
                    case SliderConstraint slider: sliders[c]!.Apply(this, slider, offset); break;
                    case PathConstraint path: paths[c]!.Apply(this, path, offset); break;
                    case PhysicsConstraint physics: Physics[c]!.Apply(this, physics, offset, delta, mode ?? (delta == 0 ? PhysicsMode.Pose : PhysicsMode.Update)); break;
                }
            }
        }
        internal void UpdateBone(int bone) => Affine.UpdateBone(bone, Data.Parents[bone], Working, World, localBases, ScaleX, ScaleY, X, Y);
        public ReadOnlySpan<double> Applied { get { for (int i = 0; i < dirtyLocal.Length; i++) EnsureLocal(i); return Working; } }
        internal void InvalidateLocal(int bone) => dirtyLocal[bone] = 1;
        internal void EnsureLocal(int bone) { if (dirtyLocal[bone] != 0) SyncBone(bone); }
        internal void SyncBone(int bone)
        { dirtyLocal[bone] = 0; Affine.SyncBone(bone, Data.Parents[bone], Working, World, ScaleX, ScaleY, X, Y); }
        public void UpdateGeometry() => Frame?.Update(Working, Data.SlotOffset, World, Tint, ScaleX, ScaleY);
        /// <summary>Opacity inherited by an external object at this slot, before its own material.</summary>
        public double SlotOpacity(int slot) => Working[Data.SlotOffset + slot * 8 + 4] * Tint[3];
        public (double X, double Y) LocalToWorld(int bone, double x, double y) => Affine.LocalToWorld(World, bone, x, y);
        public bool WorldToLocal(int bone, double x, double y, out double localX, out double localY) => Affine.WorldToLocal(World, bone, x, y, out localX, out localY);
        public double[]? MatchedTransformOffsets(int constraint) => Data.Constraints[constraint] is TransformConstraint c ? TransformSolver.MatchedOffsets(this, c) : null;
        public (double? Source, double Time)? SliderSample(int constraint)
        { var state = sliders[constraint]; return ActiveConstraints[constraint] != 0 && state != null && state.Sampled ? (state.SourceValue, state.Time) : ((double?, double)?)null; }
        public ReadOnlySpan<double> PathPoints(int constraint)
        { var path = paths[constraint]; return ActiveConstraints[constraint] != 0 && path != null && path.PointCount > 0 ? path.Points() : ReadOnlySpan<double>.Empty; }
        public bool PathSampleAt(int constraint, double position, Span<double> output)
        {
            var path = paths[constraint];
            if (ActiveConstraints[constraint] == 0 || path == null || path.PointCount == 0 || !(Data.Constraints[constraint] is PathConstraint c)) return false;
            path.SamplePoint(position * (c.PositionMode == PathPosition.Percent ? path.Total : 1), output); return true;
        }
        public (double Length, bool Closed)? PathExtent(int constraint)
        { var path = paths[constraint]; return ActiveConstraints[constraint] != 0 && path != null && path.PointCount > 0 ? (path.Total, path.Closed) : ((double, bool)?)null; }
        public void AttachmentVertices(int slot, int attachment, ReadOnlySpan<double> deform, Span<float> output)
        {
            var frame = Frame!; var data = frame.Attachments[attachment];
            data.Geometry.WriteWorld(World, frame.Data.Slots[slot].Bone, deform, output);
            if (data is MeshAttachment mesh) mesh.Place(output, ScaleX, ScaleY);
        }
        public (double X, double Y, double Rotation)? AttachmentPoint(int slot, int attachment)
        {
            if (Frame == null || !(Frame.Attachments[attachment] is PointAttachment point)) return null;
            int bone = Frame.Data.Slots[slot].Bone; Span<double> position = stackalloc double[2]; point.Geometry.WriteWorld(World, bone, default, position);
            return (position[0], position[1], PointRotation(bone, point.Rotation));
        }
        private double PointRotation(int bone, double rotation)
        {
            int w = bone * 6; double r = rotation * Affine.Degrees, c = Math.Cos(r), s = Math.Sin(r);
            return Math.Atan2(World[w + 1] * c + World[w + 3] * s, World[w] * c + World[w + 2] * s) * Affine.Radians;
        }
        public (double X, double Y, double Rotation)? Point(int slot)
        {
            if (Frame == null) return null;
            var state = Frame.Slots[slot];
            if (state.Attachment < 0 || !(Frame.Attachments[state.Attachment] is PointAttachment point)) return null;
            return (state.Vertices[0], state.Vertices[1], PointRotation(Frame.Data.Slots[slot].Bone, point.Rotation));
        }
    }
}
