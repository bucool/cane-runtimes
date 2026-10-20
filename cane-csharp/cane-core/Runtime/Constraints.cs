using System;
using System.Collections.Generic;

namespace Cane
{
    public enum ConstraintKind { Ik, Transform, Path, Physics, Slider }
    public enum ScaleYMode { Unchanged, PreserveAspect, PreserveArea }
    public enum TransformProperty { Rotation, X, Y, ScaleX, ScaleY, ShearY }
    public enum PathPosition { Fixed, Percent }
    public enum PathSpacing { Length, Fixed, Percent, Proportional }
    public enum PathRotate { Tangent, Chain, ChainScale }
    public enum PhysicsMode { None, Reset, Update, Pose }

    public abstract class Constraint
    {
        public readonly string Name;
        public readonly ConstraintKind Kind;
        public bool SkinRequired;
        internal int ChannelCount => Kind == ConstraintKind.Slider ? 2 : Kind == ConstraintKind.Path ? 5 : Kind == ConstraintKind.Physics ? 7 : 6;
        protected Constraint(string name, ConstraintKind kind) { Name = name; Kind = kind; }
    }
    public sealed class IkConstraint : Constraint
    {
        public readonly int[] Bones;
        public readonly int Target;
        public double Mix = 1, Bend = 1, Softness;
        public bool Compress, Stretch;
        public ScaleYMode ScaleY;
        public IkConstraint(string name, int[] bones, int target) : base(name, ConstraintKind.Ik) { Bones = bones; Target = target; }
    }
    public readonly struct PropertyMapping
    {
        public readonly TransformProperty From, To;
        public readonly double FromOffset, Offset, Scale;
        public readonly double? Limit;
        public PropertyMapping(TransformProperty from, TransformProperty to, double fromOffset = 0, double offset = 0, double scale = 1, double? limit = null)
        { From = from; To = to; FromOffset = fromOffset; Offset = offset; Scale = scale; Limit = limit; }
    }
    public sealed class TransformConstraint : Constraint
    {
        public readonly int Source;
        public readonly int[] Bones;
        public bool LocalSource, LocalTarget, Additive;
        public double[] Offsets = new double[6], Mix = { 1, 1, 1, 1, 1, 1 };
        public PropertyMapping[]? Mappings;
        public TransformConstraint(string name, int source, int[] bones) : base(name, ConstraintKind.Transform)
        { Source = source; Bones = (int[])bones.Clone(); Array.Sort(Bones); }
    }
    public sealed class PathConstraint : Constraint
    {
        public readonly int Slot;
        public readonly int[] Bones;
        public PathPosition PositionMode;
        public PathSpacing SpacingMode;
        public PathRotate RotateMode;
        public double Position, Spacing, OffsetRotation, MixRotate = 1, MixX = 1, MixY = 1;
        public PathConstraint(string name, int slot, int[] bones) : base(name, ConstraintKind.Path) { Slot = slot; Bones = bones; }
    }
    public sealed class PhysicsConstraint : Constraint
    {
        public readonly int Bone;
        public double X, Y, Rotate, ShearX, ScaleX, Step = 1.0 / 60, Limit = 5000;
        public double Inertia = .5, Strength = 100, Damping = .85, MassInverse = 1, Wind, Gravity, Mix = 1;
        public ScaleYMode ScaleY;
        public PhysicsConstraint(string name, int bone) : base(name, ConstraintKind.Physics) { Bone = bone; }
    }
    public sealed class SliderSource
    {
        public readonly int Bone;
        public readonly TransformProperty Property;
        public bool Local;
        public double FromOffset, Offset, Scale = 1;
        public SliderSource(int bone, TransformProperty property) { Bone = bone; Property = property; }
    }
    public sealed class SliderConstraint : Constraint
    {
        public readonly Animation Animation;
        public double Time, Mix = 1;
        public bool Additive, Loop;
        public SliderSource? Source;
        public SliderConstraint(string name, Animation animation) : base(name, ConstraintKind.Slider) { Animation = animation; }
    }

    internal static class ConstraintSchedule
    {
        // Nonnegative steps update bones. Complements address constraints, local
        // resolves, then external inputs. Compile only when dependencies change.
        public static int[] Compile(int[] parents, Constraint[] constraints, Appearance? appearance, BoneControl?[]? controls = null)
        {
            var ready = new bool[parents.Length]; var plan = new List<int>();
            var inputAdded = controls == null ? null : new bool[parents.Length];
            var children = new List<int>[parents.Length];
            for (int i = 0; i < children.Length; i++) children[i] = new List<int>();
            for (int i = 0; i < parents.Length; i++) if (parents[i] >= 0) children[parents[i]].Add(i);
            var targets = new int[constraints.Length][];
            for (int i = 0; i < constraints.Length; i++)
            {
                switch (constraints[i])
                {
                    case IkConstraint ik: targets[i] = ik.Bones; break;
                    case TransformConstraint transform: targets[i] = transform.Bones; break;
                    case PathConstraint path: targets[i] = path.Bones; break;
                    case PhysicsConstraint physics: targets[i] = new[] { physics.Bone }; break;
                    case SliderConstraint slider:
                        var bones = new List<int>();
                        foreach (var t in slider.Animation.Timelines) if (t.Channel < parents.Length * 8 && !bones.Contains(t.Channel >> 3)) bones.Add(t.Channel >> 3);
                        targets[i] = bones.ToArray(); break;
                }
            }
            void Ensure(int bone)
            {
                if (ready[bone]) return;
                if (parents[bone] >= 0) Ensure(parents[bone]);
                if (controls != null && controls[bone] != null && !inputAdded![bone])
                { plan.Add(~(constraints.Length + parents.Length + bone)); inputAdded[bone] = true; }
                plan.Add(bone); ready[bone] = true;
            }
            void InvalidateChildren(int bone)
            { foreach (int child in children[bone]) { ready[child] = false; InvalidateChildren(child); } }
            // External inputs precede all constraints, including local-only readers.
            if (controls != null) for (int bone = 0; bone < controls.Length; bone++) if (controls[bone] != null) Ensure(bone);
            for (int i = 0; i < constraints.Length; i++)
            {
                var c = constraints[i]; var affected = targets[i];
                switch (c)
                {
                    case SliderConstraint slider: if (slider.Source != null && !slider.Source.Local) Ensure(slider.Source.Bone); break;
                    case PathConstraint path:
                        Ensure(appearance!.Slots[path.Slot].Bone);
                        foreach (var a in appearance.Attachments) if (a is PathAttachment) foreach (uint bone in a.Geometry.Bones) Ensure((int)bone);
                        break;
                    case IkConstraint ik: Ensure(ik.Target); break;
                    case TransformConstraint transform: Ensure(transform.Source); break;
                }
                if (!(c is SliderConstraint)) foreach (int bone in affected) Ensure(bone);
                plan.Add(~i);
                foreach (int bone in affected) InvalidateChildren(bone);
                foreach (int bone in affected) ready[bone] = !(c is SliderConstraint);
            }
            for (int i = 0; i < parents.Length; i++) Ensure(i);
            var dirty = new bool[parents.Length]; var resolved = new List<int>(plan.Count);
            void Local(int bone) { if (dirty[bone]) { resolved.Add(~(constraints.Length + bone)); dirty[bone] = false; } }
            void BeforeWorld(int bone) { foreach (int child in children[bone]) Local(child); }
            foreach (int step in plan)
            {
                if (step >= 0) { BeforeWorld(step); Local(step); resolved.Add(step); continue; }
                if (~step >= constraints.Length) { resolved.Add(step); continue; }
                var c = constraints[~step]; var affected = targets[~step];
                if (!(c is SliderConstraint)) foreach (int bone in affected) BeforeWorld(bone);
                if (c is IkConstraint || c is SliderConstraint || c is TransformConstraint transform && transform.LocalTarget) foreach (int bone in affected) Local(bone);
                if (c is TransformConstraint source && source.LocalSource) Local(source.Source);
                if (c is SliderConstraint slider && slider.Source != null && slider.Source.Local) Local(slider.Source.Bone);
                resolved.Add(step);
                if (c is PathConstraint || c is PhysicsConstraint || c is TransformConstraint world && !world.LocalTarget) foreach (int bone in affected) dirty[bone] = true;
            }
            return resolved.ToArray();
        }
    }
}
