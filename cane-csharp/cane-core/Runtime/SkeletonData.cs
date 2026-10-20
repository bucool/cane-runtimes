using System;
using System.Collections.Generic;

namespace Cane
{
    public sealed class Bone
    {
        public readonly string Name;
        public readonly int Parent;
        public double Length, X, Y, Rotation, ScaleX = 1, ScaleY = 1, ShearX, ShearY;
        public Inherit Inherit;
        public bool SkinRequired;
        public Bone(string name, int parent = -1) { Name = name; Parent = parent; }
    }

    /// <summary>Compiled, shared data. Names resolve once; every frame uses numeric indices.</summary>
    public sealed class SkeletonData
    {
        public readonly int[] Parents;
        public readonly double[] Lengths, Setup;
        public readonly byte[] SkinRequired;
        public readonly Constraint[] Constraints;
        public readonly Appearance? Appearance;
        public readonly double ReferenceScale;
        public readonly int SlotOffset;
        public IReadOnlyDictionary<string, int> BoneNames => boneNames;
        public IReadOnlyDictionary<string, Animation> Animations => animations;
        private readonly Dictionary<string, int> boneNames = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, Animation> animations = new Dictionary<string, Animation>(StringComparer.Ordinal);
        internal readonly int[] Schedule, ConstraintOffsets, SliderSlots;
        internal readonly bool SliderOrder;

        public SkeletonData(Bone[] bones, Animation[]? animations = null, Constraint[]? constraints = null, Appearance? appearance = null, double referenceScale = 100)
        {
            Constraints = constraints ?? Array.Empty<Constraint>(); Appearance = appearance; ReferenceScale = referenceScale;
            Parents = new int[bones.Length]; Lengths = new double[bones.Length]; SkinRequired = new byte[bones.Length];
            int channels = bones.Length * 8; ConstraintOffsets = new int[Constraints.Length];
            for (int i = 0; i < Constraints.Length; i++) { ConstraintOffsets[i] = channels; channels += Constraints[i].ChannelCount; }
            SlotOffset = channels;
            var sliderSlots = new HashSet<int>();
            foreach (var c in Constraints) if (c is SliderConstraint slider)
            {
                foreach (var t in slider.Animation.Timelines) if (t.Kind == ChannelKind.Attachment) sliderSlots.Add((t.Channel - channels) / 8);
                foreach (var t in slider.Animation.Deforms) sliderSlots.Add(t.Slot);
                foreach (var t in slider.Animation.Sequences) sliderSlots.Add(t.Slot);
                SliderOrder |= slider.Animation.DrawOrders.Length != 0;
            }
            SliderSlots = new int[sliderSlots.Count]; sliderSlots.CopyTo(SliderSlots);
            Setup = new double[channels + (appearance?.Slots.Length ?? 0) * 8];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i]; int p = i * 8;
                Parents[i] = b.Parent; Lengths[i] = b.Length; SkinRequired[i] = (byte)(b.SkinRequired ? 1 : 0); boneNames[b.Name] = i;
                Setup[p] = b.X; Setup[p + 1] = b.Y; Setup[p + 2] = b.Rotation; Setup[p + 3] = b.ScaleX;
                Setup[p + 4] = b.ScaleY; Setup[p + 5] = b.ShearX; Setup[p + 6] = b.ShearY; Setup[p + 7] = (int)b.Inherit;
            }
            foreach (var animation in animations ?? Array.Empty<Animation>()) this.animations[animation.Name] = animation;
            for (int i = 0; i < Constraints.Length; i++)
            {
                int p = ConstraintOffsets[i];
                switch (Constraints[i])
                {
                    case IkConstraint c:
                        Setup[p] = c.Mix; Setup[p + 1] = c.Bend; Setup[p + 2] = c.Softness;
                        Setup[p + 3] = c.Compress ? 1 : 0; Setup[p + 4] = c.Stretch ? 1 : 0; Setup[p + 5] = (int)c.ScaleY; break;
                    case TransformConstraint c: c.Mix.CopyTo(Setup, p); break;
                    case SliderConstraint c: Setup[p] = c.Time; Setup[p + 1] = c.Mix; break;
                    case PathConstraint c:
                        Setup[p] = c.Position; Setup[p + 1] = c.Spacing; Setup[p + 2] = c.MixRotate; Setup[p + 3] = c.MixX; Setup[p + 4] = c.MixY; break;
                    case PhysicsConstraint c:
                        Setup[p] = c.Inertia; Setup[p + 1] = c.Strength; Setup[p + 2] = c.Damping;
                        Setup[p + 3] = c.MassInverse; Setup[p + 4] = c.Wind; Setup[p + 5] = c.Gravity; Setup[p + 6] = c.Mix; break;
                }
            }
            Schedule = ConstraintSchedule.Compile(Parents, Constraints, appearance);
            if (appearance != null) for (int i = 0; i < appearance.Slots.Length; i++)
            {
                var slot = appearance.Slots[i]; int p = channels + i * 8; Setup[p] = slot.Attachment;
                for (int j = 0; j < 4; j++) Setup[p + 1 + j] = slot.Color[j];
                if (slot.Dark != null) for (int j = 0; j < 3; j++) Setup[p + 5 + j] = slot.Dark[j];
            }
        }
    }
}
