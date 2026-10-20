using System;
using UnityEngine;

namespace Cane.Unity
{
    public enum UpdateMode { Update, FixedUpdate, Unscaled, Manual, LateUpdate }

    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(-20)]
    public sealed class CanePlayer : MonoBehaviour
    {
        public CaneAsset Asset;
        public string InitialAnimation = "";
        public bool Loop = true;
        public float TimeScale = 1, UnitsPerPixel = .01f;
        public UpdateMode UpdateMode;
        public Vector2 PhysicsTranslation = Vector2.one;
        public float PhysicsRotation = 1;
        public Transform PhysicsRelativeTo;
        public Skeleton Skeleton { get; private set; }
        public AnimationState State { get; private set; }
        public CaneResources Resources { get; private set; }
        public event Action<CanePlayer> Rebuilt, BeforeWorld, AfterWorld, AfterGeometry;
        internal CaneRootMotion MotionDriver;
        public event Action<RuntimeEvent> AnimationEvent;
        private CaneAsset retained;
        private int assetRevision;
        private Vector3 previousPosition;
        private float previousAngle;
        private readonly System.Collections.Generic.List<RuntimeEvent> pending = new System.Collections.Generic.List<RuntimeEvent>();
        private bool dispatching;
        private bool hasPhysics;

        private void OnEnable() { if (Skeleton == null && Asset != null) Initialize(); ResetHostMotion(); }
        public void Initialize(CaneResources resources = null)
        {
            if (resources == null && Asset == null)
            {
                retained?.Release(Resources); retained = null; Resources = null; Skeleton = null; State = null;
                Rebuilt?.Invoke(this); return;
            }
            var source = resources ?? Asset.Acquire(); var skeleton = new Skeleton(source.Asset.Data); var state = new AnimationState();
            retained?.Release(Resources); retained = resources == null ? Asset : null;
            assetRevision = retained == null ? 0 : retained.Revision;
            Resources = source; Skeleton = skeleton; State = state;
            hasPhysics = Array.Exists(skeleton.Physics, physics => physics != null);
            if (InitialAnimation.Length > 0) Play(InitialAnimation, Loop);
            // Core already evaluated setup; only animation or host callbacks need another evaluation.
            bool evaluate = InitialAnimation.Length > 0 || Rebuilt != null || BeforeWorld != null || AfterWorld != null || AfterGeometry != null;
            ResetHostMotion(); Rebuilt?.Invoke(this);
            if (evaluate) Advance(0);
        }
        public TrackEntry Play(string animation, bool loop = true, double mix = 0, int track = 0)
        {
            int index = Array.IndexOf(Resources.Asset.AnimationIds, animation);
            if (index < 0) index = Array.FindIndex(Resources.Asset.Animations, a => a.Name == animation);
            if (index < 0) throw new ArgumentException("Unknown animation: " + animation, nameof(animation));
            return State.Set(track, Resources.Asset.Animations[index], loop, mix);
        }
        public int FindSlot(string name)
        {
            if (Skeleton?.Frame == null) return -1;
            int slot = Array.IndexOf(Resources.Asset.SlotIds, name);
            return slot >= 0 ? slot : Array.FindIndex(Skeleton.Frame.Data.Slots, value => value.Name == name);
        }
        private void Update() { if (Application.isPlaying && (UpdateMode == UpdateMode.Update || UpdateMode == UpdateMode.Unscaled)) Advance(UpdateMode == UpdateMode.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime); }
        private void FixedUpdate() { if (Application.isPlaying && UpdateMode == UpdateMode.FixedUpdate) Advance(Time.fixedDeltaTime); }
        private void LateUpdate() { if (Application.isPlaying && UpdateMode == UpdateMode.LateUpdate) Advance(Time.deltaTime); }
        public void Advance(double delta)
        {
            if (retained != null && (retained != Asset || assetRevision != retained.Revision)) Initialize();
            if (Skeleton == null) return;
            delta *= TimeScale; State.Update(delta); State.Apply(Skeleton);
            MotionDriver?.Restore(this); BeforeWorld?.Invoke(this); ApplyHostMotion(); Skeleton.UpdateWorld(delta);
            MotionDriver?.Move(this); AfterWorld?.Invoke(this);
            Skeleton.UpdateGeometry(); AfterGeometry?.Invoke(this);
            // Dispatch a stable queue so a listener may change tracks or seek safely.
            if (dispatching || State.Events.Count == 0) return;
            pending.AddRange(State.Events); State.Events.Clear(); dispatching = true;
            try { for (int i = 0; i < pending.Count; i++) AnimationEvent?.Invoke(pending[i]); }
            finally { pending.Clear(); dispatching = false; }
        }
        private Vector3 HostPosition => PhysicsRelativeTo == null ? transform.position : PhysicsRelativeTo.InverseTransformPoint(transform.position);
        private float HostAngle => transform.eulerAngles.z - (PhysicsRelativeTo == null ? 0 : PhysicsRelativeTo.eulerAngles.z);
        public void ResetHostMotion() { if (hasPhysics) { previousPosition = HostPosition; previousAngle = HostAngle; } }
        private void ApplyHostMotion()
        {
            if (!hasPhysics) return;
            var position = HostPosition; float angle = HostAngle;
            var movement = position - previousPosition;
            if (PhysicsRelativeTo != null) movement = PhysicsRelativeTo.TransformVector(movement);
            movement = transform.InverseTransformVector(movement);
            double rotation = (double)Mathf.DeltaAngle(previousAngle, angle) * PhysicsRotation;
            foreach (var physics in Skeleton.Physics) if (physics != null)
            {
                if (UnitsPerPixel != 0) physics.Translate((double)movement.x * PhysicsTranslation.x / UnitsPerPixel, (double)movement.y * PhysicsTranslation.y / UnitsPerPixel);
                if (rotation != 0) physics.Rotate(Skeleton.X, Skeleton.Y, rotation);
            }
            previousPosition = position; previousAngle = angle;
        }
        private void OnDestroy() { retained?.Release(Resources); retained = null; }
    }
}
