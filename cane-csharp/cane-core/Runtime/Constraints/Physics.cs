using System;
using System.Collections.Generic;
using Cane.Animation;
using Cane.Format;

namespace Cane
{
    public readonly struct PhysicsEnvironment
    {
        public readonly float WindX, WindY, GravityX, GravityY;
        public PhysicsEnvironment(float windX, float windY, float gravityX, float gravityY)
        { WindX = windX; WindY = windY; GravityX = gravityX; GravityY = gravityY; }
        public static PhysicsEnvironment Default => new PhysicsEnvironment(1, 0, 0, 1);
    }
}
namespace Cane.Constraints
{
    internal sealed class PhysicsBudget
    {
        internal int Remaining = 1000000;
        internal void Use(string id)
        { if (Remaining == 0) throw Limit(id); Remaining--; }
        internal static RuntimeException Limit(string id) => new RuntimeException(RuntimeErrorCode.ResourceLimit, "apply", "Physics fixed-step budget is exhausted or cannot make progress.");
    }
    internal sealed class PhysicsState
    {
        internal bool Reset = true;
        internal double RestX, RestY, AxisX, AxisY, XOffset, YOffset, XVelocity, YVelocity, AngleOffset, AngleVelocity, LogStretch, StretchVelocity;
        internal PhysicsState Clone() => (PhysicsState)MemberwiseClone();
        internal bool IsFinite => DampedSpring.Finite(RestX) && DampedSpring.Finite(RestY) && DampedSpring.Finite(AxisX) && DampedSpring.Finite(AxisY) && DampedSpring.Finite(XOffset) && DampedSpring.Finite(YOffset) && DampedSpring.Finite(XVelocity) && DampedSpring.Finite(YVelocity) && DampedSpring.Finite(AngleOffset) && DampedSpring.Finite(AngleVelocity) && DampedSpring.Finite(LogStretch) && DampedSpring.Finite(StretchVelocity);
        internal PhysicsState Transport(Affine delta)
        {
            (double X, double Y) Vector(double x, double y) => (delta.A * x + delta.C * y, delta.B * x + delta.D * y);
            PhysicsState next = Clone();
            (next.XOffset, next.YOffset) = Vector(XOffset, YOffset);
            (next.XVelocity, next.YVelocity) = Vector(XVelocity, YVelocity);
            if (!Reset)
            {
                var (ox, oy) = Vector(RestX, RestY); var (ax, ay) = Vector(AxisX, AxisY);
                double si = Math.Sin(AngleOffset), co = Math.Cos(AngleOffset), scale = Math.Exp(LogStretch);
                double dx = scale * (co * AxisX - si * AxisY), dy = scale * (si * AxisX + co * AxisY);
                var (mx, my) = Vector(dx, dy);
                var (vx, vy) = Vector(dx * StretchVelocity - dy * AngleVelocity, dy * StretchVelocity + dx * AngleVelocity);
                double restLength = Math.Sqrt(ax * ax + ay * ay), movedLength = Math.Sqrt(mx * mx + my * my);
                if (restLength > 0 && movedLength > 0)
                {
                    double ux = mx / movedLength, uy = my / movedLength;
                    double principal = Physics.WrapRadians(Math.Atan2(my, mx) - Math.Atan2(ay, ax));
                    double orientation = (double)delta.A * delta.D - (double)delta.B * delta.C < 0 ? -1 : 1;
                    double turns = Math.Round((orientation * AngleOffset - principal) / (2 * Math.PI), MidpointRounding.AwayFromZero);
                    next.AngleOffset = principal + turns * (2 * Math.PI);
                    next.AngleVelocity = (ux * vy - uy * vx) / movedLength;
                    next.LogStretch = Math.Log(movedLength) - Math.Log(restLength);
                    next.StretchVelocity = (ux * vx + uy * vy) / movedLength;
                }
                else next.AngleOffset = next.AngleVelocity = next.LogStretch = next.StretchVelocity = 0;
                next.RestX = ox + delta.Tx; next.RestY = oy + delta.Ty; next.AxisX = ax; next.AxisY = ay;
            }
            if (!next.IsFinite) throw new RuntimeException(RuntimeErrorCode.NonFinite, "setRootTransform", "Physics history transport cannot be represented.");
            return next;
        }
    }

    internal sealed partial class PhysicsStore
    {
        internal readonly Dictionary<string, PhysicsState> States = new Dictionary<string, PhysicsState>(StringComparer.Ordinal);
        private string? animation;
        private float? time, rawTime, rangeStart;
        private float rangeEnd;
        private bool looping;
        internal PhysicsStore Clone()
        { var result = new PhysicsStore { animation = animation, time = time, rawTime = rawTime, rangeStart = rangeStart, rangeEnd = rangeEnd, looping = looping }; foreach (var p in States) result.States.Add(p.Key, p.Value.Clone()); return result; }
        internal bool Reset(string? id = null)
        {
            if (id == null) { bool had = States.Count != 0; States.Clear(); animation = null; time = null; rawTime = null; rangeStart = null; return had; }
            if (!States.ContainsKey(id)) return false; States[id] = new PhysicsState(); return true;
        }
        internal PhysicsState State(string id)
        { if (!States.TryGetValue(id, out PhysicsState? result)) { result = new PhysicsState(); States.Add(id, result); } return result; }
        internal float Advance(AnimationPlayer state, Pose pose, SamplingOptions sampling)
        {
            AnimationPlayback? current = state.Tracks.TryGetValue(0, out Track? track) ? track.Current : null;
            float raw = current?.Time ?? state.DetachedTime, now = sampling.Time(raw); string? clip = current?.Clip?.Id;
            float start = current?.Start ?? 0, end = current?.End ?? 0; bool loop = current?.Loop ?? false;
            bool changed = animation != clip || rangeStart != start || rangeEnd != end || looping != loop || time.HasValue && now < time.Value;
            float? previous = changed ? null : time;
            if (changed) States.Clear(); float delta = Math.Max(0, previous.HasValue ? now - previous.Value : now);
            if (!Numeric.Finite(delta)) throw new RuntimeException(RuntimeErrorCode.NonFinite,"apply","Physics elapsed time overflowed.");
            animation = clip; time = now; rawTime = raw; rangeStart = start; rangeEnd = end; looping = loop;
            if (current?.Clip != null && previous.HasValue && now > previous.Value && (double)current.BlendWeight * current.Progress >= .5)
            {
                foreach (Json timeline in current.Clip.Raw["constraintTimelines"].Items)
                {
                    if (timeline.S("type") != "physics") continue; bool crossed = false;
                    foreach (Json key in timeline["keys"].Items)
                    {
                        float keyTime = key.F("time");
                        if (!key.B("reset") || !Numeric.Finite(keyTime) || keyTime < start || keyTime > end) continue;
                        float offset = keyTime == end ? current.Duration : keyTime - start;
                        crossed = loop && current.Duration > 0 ? SequenceClock.PeriodicResetCrossed(previous.Value,now,offset,current.Duration) : offset > previous.Value && offset <= now;
                        if (crossed) break;
                    }
                    if (!crossed) continue; string id = timeline.S("constraintId");
                    foreach (Json c in pose.Constraints)
                        if (c.S("type") == "physics" && (id == "*" || c.S("id") == id) && pose.IsActive(c.S("id"),"constraintIds") && pose.IsActive(c.S("boneId"),"boneIds")) Reset(c.S("id"));
                }
            }
            return delta;
        }
    }

    internal static class Physics
    {
        internal static double WrapRadians(double angle)
        { double r = (angle + Math.PI) % (2 * Math.PI); if (r < 0) r += 2 * Math.PI; return r - Math.PI; }
        internal static void Solve(Pose pose, Json c, Affine root, PhysicsStore store, float delta, PhysicsEnvironment env, float rootScaleX, float rootScaleY, PhysicsBudget budget)
        {
            string id = c.S("id"), boneId = c.S("boneId"); if (!pose.IsActive(boneId, "boneIds")) return;
            float mix = c.F("mix", 1), fps = c.F("fps", 60), reference = pose.Data.ReferenceScale;
            if (mix == 0 || fps == 0 || !Numeric.Finite(reference) || reference <= 0) return;
            int bone = pose.Data.BoneIndex[boneId];
            PhysicsState before = store.State(id), s = before.Clone(); Affine current = pose.World[bone];
            delta = Numeric.Finite(delta) ? Math.Max(delta, 0) : 0;
            double length = Math.Max(pose.Data.BoneData[bone].Length, 0), axisX = length * current.A, axisY = length * current.B;
            float x = c.F("x"), y = c.F("y"), rotate = c.F("rotate"), shear = c.F("shearX"), scaleX = c.F("scaleX");
            bool xe = x > 0, ye = y > 0, re = rotate > 0 || shear > 0, se = scaleX > 0;
            double countValue = !s.Reset && (xe || ye || re || se) && delta > 0 ? Math.Ceiling((float)((double)delta * fps)) : 0;
            if (!DampedSpring.Finite(countValue) || countValue > budget.Remaining) throw PhysicsBudget.Limit(id);
            int count = (int)countValue; float? requiredLimit = 0; bool saturated = false;
            double limit = Math.Max(c.F("limit", 5000), 0);
            void Observe(double motion, double maximum, float scale)
            {
                saturated |= Math.Abs(motion) > maximum;
                float rounded = Math.Abs((float)motion); if (rounded == 0 || !requiredLimit.HasValue) return;
                float denominator = delta * Math.Abs(scale), candidate = rounded / denominator;
                requiredLimit = denominator == 0 || !Numeric.Finite(candidate) ? (float?)null : Math.Max(requiredLimit.Value, candidate);
            }
            if (!s.Reset)
            {
                double inertia = Numeric.Clamp(c.F("inertia", 1), 0, 1);
                double motionX = (current.Tx - s.RestX) * inertia, motionY = (current.Ty - s.RestY) * inertia;
                double limitX = limit * delta * Math.Abs(rootScaleX), limitY = limit * delta * Math.Abs(rootScaleY);
                if (xe || re || se) Observe(motionX, limitX, rootScaleX);
                if (ye || re || se) Observe(motionY, limitY, rootScaleY);
                double pivotX = Math.Max(-limitX, Math.Min(limitX, motionX)), pivotY = Math.Max(-limitY, Math.Min(limitY, motionY));
                if (xe) s.XOffset -= pivotX; if (ye) s.YOffset -= pivotY;
                double axisLength = Math.Sqrt(axisX * axisX + axisY * axisY), previousLength = Math.Sqrt(s.AxisX * s.AxisX + s.AxisY * s.AxisY);
                if (axisLength > 0 && previousLength > 0)
                {
                    double ux = axisX / axisLength, uy = axisY / axisLength;
                    if (re) s.AngleOffset -= inertia * WrapRadians(Math.Atan2(axisY, axisX) - Math.Atan2(s.AxisY, s.AxisX)) + (ux * pivotY - uy * pivotX) / axisLength;
                    if (se) s.LogStretch -= inertia * (Math.Log(axisLength) - Math.Log(previousLength)) + (ux * pivotX + uy * pivotY) / axisLength;
                }
                double mass = c.F("mass", 1); if (!DampedSpring.Finite(mass) || mass <= 0) throw Failure(id);
                double stiffness = Math.Max(c.F("strength", 100), 0) / mass, drag = 60 * (1 - (double)Numeric.Clamp(c.F("damping", 1), 0, 1));
                double forceX = (double)reference * ((double)c.F("wind") * env.WindX + (double)c.F("gravity") * env.GravityX) * rootScaleX;
                double forceY = -(double)reference * ((double)c.F("wind") * env.WindY + (double)c.F("gravity") * env.GravityY) * rootScaleY;
                double ax = forceX / mass, ay = forceY / mass;
                double angular = (axisX * forceY - axisY * forceX) / ((double)reference * reference * mass);
                double axial = (axisX * forceX + axisY * forceY) / ((double)reference * reference * mass);
                double interval = count > 0 ? (double)delta / count : 0;
                for (int step = 0; step < count; step++)
                {
                    if (xe) (s.XOffset, s.XVelocity) = DampedSpring.Advance(s.XOffset, s.XVelocity, interval, ax, stiffness, drag);
                    if (ye) (s.YOffset, s.YVelocity) = DampedSpring.Advance(s.YOffset, s.YVelocity, interval, ay, stiffness, drag);
                    if (re) (s.AngleOffset, s.AngleVelocity) = DampedSpring.Advance(s.AngleOffset, s.AngleVelocity, interval, angular, stiffness, drag);
                    if (se) (s.LogStretch, s.StretchVelocity) = DampedSpring.Advance(s.LogStretch, s.StretchVelocity, interval, axial, stiffness, drag);
                }
            }
            s.Reset = false; s.RestX = current.Tx; s.RestY = current.Ty; s.AxisX = axisX; s.AxisY = axisY;
            if (!xe) s.XOffset = s.XVelocity = 0; if (!ye) s.YOffset = s.YVelocity = 0;
            if (!re) s.AngleOffset = s.AngleVelocity = 0; if (!se) s.LogStretch = s.StretchVelocity = 0;
            Affine m = new Affine(current.A, current.B, current.C, current.D,
                (float)(current.Tx + s.XOffset * mix * x), (float)(current.Ty + s.YOffset * mix * y));
            if (rotate > 0) m = RotateBasis(m, (float)(s.AngleOffset * mix * rotate), false);
            if (shear > 0) m = RotateBasis(m, (float)(s.AngleOffset * mix * shear), true);
            if (se)
            {
                double log = s.LogStretch * mix * scaleX, factor = Math.Exp(log);
                double yFactor = c.S("scaleYMode") == "uniform" ? factor : c.S("scaleYMode") == "volume" ? Math.Exp(-log) : 1;
                m = new Affine((float)(m.A * factor), (float)(m.B * factor), (float)(m.C * yFactor), (float)(m.D * yFactor), m.Tx, m.Ty);
            }
            if (!s.IsFinite || !m.IsFinite) throw Failure(id);
            var diagnostic = new PhysicsConstraintDiagnostic(count, s, (float)limit, requiredLimit, saturated);
            Solvers.WriteWorld(pose, bone, m, root);
            store.States[id] = s; budget.Remaining -= count; pose.Diagnostics[id] = diagnostic;
        }
        private static RuntimeException Failure(string id) => new RuntimeException(RuntimeErrorCode.NonFinite, "apply", "Physics state or geometry cannot be represented.", entityId: id);
        private static Affine RotateBasis(Affine m, float angle, bool firstOnly)
        {
            float co = Numeric.Cos(angle), si = Numeric.Sin(angle);
            return new Affine(co * m.A - si * m.B, si * m.A + co * m.B,
                firstOnly ? m.C : co * m.C - si * m.D, firstOnly ? m.D : si * m.C + co * m.D, m.Tx, m.Ty);
        }
    }
}
