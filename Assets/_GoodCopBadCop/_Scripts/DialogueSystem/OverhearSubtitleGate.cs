using UnityEngine;

/// <summary>
/// Attached at runtime by <see cref="DialogueManager"/> to an on-screen subtitle that was
/// spawned because the local player was within overhear range of <see cref="_source"/>.
/// Re-evaluates <see cref="OverhearRange"/> every frame and hides the subtitle (CanvasGroup
/// alpha only, so lifetimes/typewriter/auto-destroy timers are untouched) while the local
/// player is out of range, restoring it if they walk back in before it expires.
/// </summary>
[DisallowMultipleComponent]
public class OverhearSubtitleGate : MonoBehaviour
{
    private Transform _source;
    private bool _participantsAlwaysSee;
    private CanvasGroup _canvasGroup;

    /// <summary>
    /// Gates <paramref name="target"/> on range to <paramref name="source"/>. When
    /// <paramref name="participantsAlwaysSee"/> is true, conversation participants keep seeing it
    /// regardless of distance (<see cref="OverhearRange.CanLocalPlayerSee"/>); otherwise it is a
    /// pure range check (<see cref="OverhearRange.IsLocalPlayerWithin(Transform)"/>).
    /// </summary>
    public static void Attach(GameObject target, Transform source, bool participantsAlwaysSee)
    {
        if (target == null || source == null) return;

        if (!target.TryGetComponent(out OverhearSubtitleGate gate))
            gate = target.AddComponent<OverhearSubtitleGate>();

        gate._source = source;
        gate._participantsAlwaysSee = participantsAlwaysSee;
        gate.Refresh();
    }

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Source despawned mid-line: treat like a speaker-less line and keep it visible.
        bool visible = _source == null ||
                       (_participantsAlwaysSee
                           ? OverhearRange.CanLocalPlayerSee(_source)
                           : OverhearRange.IsLocalPlayerWithin(_source));

        _canvasGroup.alpha = visible ? 1f : 0f;
    }
}
