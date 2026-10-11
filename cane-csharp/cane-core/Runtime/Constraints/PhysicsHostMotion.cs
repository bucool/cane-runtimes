using System;
using System.Collections.Generic;

namespace Cane
{
    public enum PhysicsHostMotionMode { Move, Teleport, PreserveInertia, ClearInertia }
}

namespace Cane.Constraints
{
    internal sealed partial class PhysicsStore
    {
        // Called only on a detached transaction candidate. Clock identity and time are retained.
        internal void ApplyHostMotion(Affine previous, Affine next, PhysicsHostMotionMode mode, string? constraintId)
        {
            if (mode == PhysicsHostMotionMode.Move) return;
            if (mode == PhysicsHostMotionMode.Teleport)
            {
                if (constraintId == null) { foreach (string id in new List<string>(States.Keys)) States[id] = new PhysicsState(); }
                else if (States.ContainsKey(constraintId)) States[constraintId] = new PhysicsState();
                return;
            }
            float determinant = previous.Determinant;
            if (determinant == 0 || !Numeric.Finite(determinant))
                throw new RuntimeException(RuntimeErrorCode.InvalidState, "setRootTransform", "Preserving Physics history requires an invertible previous root.", "rootTransform");
            float a = previous.D / determinant, b = -previous.B / determinant, c = -previous.C / determinant, d = previous.A / determinant;
            Affine inverse = new Affine(a, b, c, d, -a * previous.Tx - c * previous.Ty, -b * previous.Tx - d * previous.Ty);
            Affine delta = next * inverse;
            if (!delta.IsFinite) throw new RuntimeException(RuntimeErrorCode.NonFinite, "setRootTransform", "Root motion delta overflowed.", "rootTransform");
            var staged = new Dictionary<string, PhysicsState>();
            foreach (KeyValuePair<string, PhysicsState> entry in States)
            {
                if (constraintId != null && entry.Key != constraintId) continue;
                PhysicsState s = entry.Value.Transport(delta);
                if (mode == PhysicsHostMotionMode.ClearInertia) s.XVelocity = s.YVelocity = s.AngleVelocity = s.StretchVelocity = 0;
                staged.Add(entry.Key, s);
            }
            foreach (var entry in staged) States[entry.Key] = entry.Value;
        }
    }
}
