using UnityEngine;

namespace GoodCopBadCop.Input
{
    /// <summary>
    /// Tracks whether an in-game text field (e.g. the PC terminal search bar) currently owns the
    /// keyboard. Gameplay/hotkey code should skip keyboard shortcuts while
    /// <see cref="IsCapturingKeyboard"/> is true so typed letters don't also fire actions.
    /// The flag stays true for the whole frame in which focus is released, so the key that
    /// closed the field (Escape / Enter) isn't also handled by another system that same frame,
    /// regardless of Update order.
    /// </summary>
    public static class TextInputFocus
    {
        private static object _owner;
        private static int _releasedFrame = -1;

        public static bool IsCapturingKeyboard => _owner != null || _releasedFrame == Time.frameCount;

        public static bool IsOwnedBy(object owner) => owner != null && ReferenceEquals(_owner, owner);

        public static void Acquire(object owner)
        {
            if (owner != null)
                _owner = owner;
        }

        public static void Release(object owner)
        {
            if (!ReferenceEquals(_owner, owner))
                return;

            _owner = null;
            _releasedFrame = Time.frameCount;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _owner = null;
            _releasedFrame = -1;
        }
    }
}
