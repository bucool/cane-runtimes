using UnityEngine;

namespace Cane.Unity
{
    // External graphs write inputs first, then this clock evaluates the player once.
    // Ordinary CanePlayer instances do not need this component.
    [AddComponentMenu(""), DefaultExecutionOrder(-50)]
    internal sealed class CaneAnimationClock : MonoBehaviour
    {
        private CanePlayer player;
        private UpdateMode previous;
        private int users;
        internal bool Unscaled;
        internal static CaneAnimationClock Acquire(CanePlayer player)
        {
            var clock = player.GetComponent<CaneAnimationClock>();
            if (clock == null) { clock = player.gameObject.AddComponent<CaneAnimationClock>(); clock.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave; }
            if (clock.users++ == 0)
            {
                clock.player = player; clock.previous = player.UpdateMode;
                clock.Unscaled = player.UpdateMode == UpdateMode.Unscaled; player.UpdateMode = UpdateMode.Manual; clock.enabled = true;
            }
            return clock;
        }
        internal void Release()
        {
            if (--users != 0) return;
            if (player != null) player.UpdateMode = previous;
            enabled = false;
        }
        private void LateUpdate()
        { if (Application.isPlaying && player != null && player.isActiveAndEnabled) player.Advance(Unscaled ? Time.unscaledDeltaTime : Time.deltaTime); }
    }
}
