using System;
using System.Collections.Generic;
using Cane.Animation;
using Cane.Format;
using Cane.Geometry;

namespace Cane.Constraints
{
    internal sealed partial class PathSampler
    {
        private readonly CubicArc[] curves;
        private readonly double[] lengths;
        private readonly bool closed, constant;
        private readonly double total;
        internal bool Closed => closed;
        internal float Length => FiniteFloat(total);
        private static float FiniteFloat(double value) { float v = (float)value; if (!Numeric.Finite(v)) throw CubicArc.Failure(); return v; }
        internal PathSampler(float[] world, Json path)
        {
            if (world.Length < 12 || world.Length % 6 != 0) throw CubicArc.Failure();
            closed = path.B("closed"); constant = path.B("constantSpeed", true); int knots = world.Length / 6, count = closed ? knots : knots - 1;
            curves = new CubicArc[count]; lengths = new double[count]; var budget = new ArcBudget(); double sum = 0;
            DoublePoint Read(int knot, int point) { int i = knot * 6 + point * 2; return new DoublePoint(world[i], world[i + 1]); }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % knots;
                var curve = new CubicArc(new[] { Read(i, 1), Read(i, 2), Read(next, 0), Read(next, 1) }, budget); curves[i] = curve;
                sum += curve.Length; FiniteFloat(sum); lengths[i] = sum;
            }
            if (!constant)
            {
                bool authored = path["lengths"].ArrayOrEmpty.Count == knots; float previous = 0;
                if (authored) for (int i = 0; i < count; i++) { float value = path["lengths"][i].Float; if (!Numeric.Finite(value) || value < previous) { authored = false; break; } previous = value; }
                if (authored) for (int i = 0; i < count; i++) lengths[i] = path["lengths"][i].Float;
                else
                {
                    double cumulative = 0;
                    for (int i = 0; i < count; i++)
                    {
                        DoublePoint origin = curves[i].Controls[0]; var p = new DoublePoint[4];
                        for (int j = 0; j < 4; j++) p[j] = new DoublePoint(curves[i].Controls[j].X - origin.X, curves[i].Controls[j].Y - origin.Y);
                        DoublePoint before = new DoublePoint(0, 0);
                        for (int step = 1; step <= 24; step++)
                        {
                            double t = step / 24.0, u = 1 - t, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                            var point = new DoublePoint(b * p[1].X + c * p[2].X + d * p[3].X, b * p[1].Y + c * p[2].Y + d * p[3].Y);
                            cumulative += new DoublePoint(point.X - before.X, point.Y - before.Y).Length; before = point;
                        }
                        if (cumulative > float.MaxValue) throw CubicArc.Failure(); lengths[i] = FiniteFloat(cumulative);
                    }
                }
                sum = lengths[lengths.Length - 1];
            }
            total = sum;
        }
        private static Sample Output(DoublePoint point, DoublePoint tangent) => new Sample(new Point(FiniteFloat(point.X), FiniteFloat(point.Y)), FiniteFloat(Math.Atan2(tangent.Y, tangent.X) * (180 / Math.PI)));
        internal Sample? At(float distance)
        {
            double value = FiniteFloat(distance);
            if (closed && total > 0) { value %= total; if (value < 0) value += total; }
            else if (!closed && (value < 0 || value > total))
            {
                CubicArc curve = value < 0 ? curves[0] : curves[curves.Length - 1]; double t = value < 0 ? 0 : 1, extra = value < 0 ? value : value - total;
                DoublePoint point = curve.Point(t), tangent = curve.Tangent(t); double norm = tangent.Length;
                return Output(new DoublePoint(point.X + extra * tangent.X / norm, point.Y + extra * tangent.Y / norm), tangent);
            }
            int index = 0; while (index + 1 < lengths.Length && lengths[index] < value) index++;
            double start = index == 0 ? 0 : lengths[index - 1], span = lengths[index] - start;
            double parameter = span == 0 ? 0 : constant ? curves[index].ParameterAtDistance(value - start) : Math.Max(0, Math.Min(1, (value - start) / span));
            return Output(curves[index].Point(parameter), curves[index].Tangent(parameter));
        }
        internal readonly struct Sample
        {
            internal readonly Point Position;
            internal readonly float Tangent;
            internal Sample(Point position, float tangent) { Position = position; Tangent = tangent; }
        }
    }
    internal static class PathConstraint
    {
        internal static void Solve(Pose pose, Json c, Affine root)
        {
            int slot = pose.Data.SlotIndex[c.S("targetSlotId")], targetBone = pose.Data.SlotData[slot].Bone;
            if (!pose.IsActive(pose.Data.BoneData[targetBone].Id, "boneIds")) return;
            int attachment = pose.Slots[slot].ResolvedAttachment; if (attachment < 0 || pose.Data.AttachmentData[attachment].Kind != "path") return;
            AttachmentData a = pose.Data.AttachmentData[attachment]; var path = new PathSampler(Rendering.WorldVertices(pose, a), a.Raw);
            int count = c["boneIds"].Items.Count; var indices = new int[count]; var lengths = new float[count]; var snapshot = (Affine[])pose.World.Clone();
            bool tangent = c.S("rotateMode") == "tangent", chainScale = c.S("rotateMode") == "chainScale"; int spacesCount = tangent ? count : count + 1;
            float total = 0;
            for (int i = 0; i < count; i++)
            {
                int bone = pose.Data.BoneIndex[c["boneIds"][i].String]; indices[i] = bone; lengths[i] = Math.Max(0, pose.Data.BoneData[bone].Length) * Solvers.Sx(snapshot[bone]);
                if (i < (tangent ? count - 1 : count)) total += lengths[i];
            }
            float spacing = c.F("spacing"), distance = c.F("position") * (c.S("positionMode") == "percent" ? path.Length : 1);
            float rotation = c.F("rotation"), offset = rotation != 0 && snapshot[targetBone].Determinant < 0 ? -rotation : rotation;
            float mr = Numeric.Clamp(c.F("mixRotate", 1), 0, 1), mx = Numeric.Clamp(c.F("mixX", 1), 0, 1), my = Numeric.Clamp(c.F("mixY", 1), 0, 1);
            var desired = new Dictionary<int, Affine>();
            float? translationResidual = null, rotationResidual = null, scaleResidual = null;
            for (int i = 0; i < count; i++)
            {
                int bone = indices[i]; float setupLength = Math.Max(0, pose.Data.BoneData[bone].Length), increment;
                switch (c.S("spacingMode"))
                {
                    case "length": increment = setupLength > 0 ? Math.Max(setupLength + spacing, 0) * lengths[i] / setupLength : spacing; break;
                    case "fixed": increment = setupLength > 0 ? spacing * lengths[i] / setupLength : spacing; break;
                    case "percent": increment = spacing * path.Length; break;
                    default: increment = total > 0 ? (setupLength > 0 ? lengths[i] : spacing) / total * spacing * path.Length : spacing * path.Length / Math.Max(spacesCount, 1); break;
                }
                PathSampler.Sample? sample = path.At(distance), next = path.At(distance + increment); distance += increment;
                if (!sample.HasValue || !next.HasValue) continue;
                Point point = sample.Value.Position, ahead = next.Value.Position; Affine current = snapshot[bone]; float oldAngle = Solvers.Angle(current);
                float chainAngle = Numeric.Atan2(ahead.Y - point.Y, ahead.X - point.X) * Numeric.RadToDeg;
                float angle = oldAngle + Numeric.Wrap((tangent ? sample.Value.Tangent : chainAngle) + offset - oldAngle) * mr;
                float scaleX = Solvers.Sx(current), scaleY = Solvers.Sy(current);
                float dx = ahead.X - point.X, dy = ahead.Y - point.Y, targetLength = (float)Math.Sqrt(dx * dx + dy * dy), boneLength = Math.Abs(pose.Data.BoneData[bone].Length);
                if (chainScale && boneLength != 0) scaleX += (targetLength / boneLength - scaleX) * mr;
                float shearY = Solvers.ShearY(current);
                Affine nextWorld = Affine.FromLocal(new BoneLocal(current.Tx + (point.X - current.Tx) * mx, current.Ty + (point.Y - current.Ty) * my, angle, scaleX, scaleY, 0, shearY));
                if (!nextWorld.IsFinite) throw CubicArc.Failure(); desired[bone] = nextWorld;
                if (mx != 0 || my != 0) Solvers.IncludeMax(ref translationResidual, Numeric.Hypot(mx == 0 ? 0 : nextWorld.Tx - point.X, my == 0 ? 0 : nextWorld.Ty - point.Y));
                if (mr != 0) Solvers.IncludeMax(ref rotationResidual, Math.Abs(Numeric.Wrap(Solvers.Angle(nextWorld) - (tangent ? sample.Value.Tangent : chainAngle) - offset)));
                if (chainScale && mr != 0 && boneLength != 0) Solvers.IncludeMax(ref scaleResidual, Math.Abs(Solvers.Sx(nextWorld) - targetLength / boneLength));

            }
            var affected = new bool[pose.World.Length];
            for (int i = 0; i < affected.Length; i++)
            {
                int parent = pose.Data.BoneData[i].Parent;
                if (desired.TryGetValue(i, out Affine matrix)) { affected[i] = true; Solvers.SetWorld(pose, i, matrix, root); }
                else if (parent >= 0 && affected[parent]) { affected[i] = true; pose.World[i] = Affine.Child(pose.World[parent], pose.Locals[i], pose.Modes[i]); }
            }
            pose.Diagnostics[c.S("id")] = new PathConstraintDiagnostic(desired.Count, c.F("mixRotate", 1), c.F("mixX", 1), c.F("mixY", 1), translationResidual, rotationResidual, scaleResidual);
        }
    }
}
