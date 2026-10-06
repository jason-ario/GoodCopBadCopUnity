using System.Collections;
using UnityEngine;

/// <summary>
/// Single local gate for 2D task-feedback cues, so one finished piece of work never produces two
/// overlapping success sounds.
///
/// Three sources route through here:
///   - <see cref="TutorialObjectiveItem.UpdateText"/>'s progress ding (a row counter ticking, e.g.
///     "Packages sorted 3/30") — via <see cref="TryPlay"/>.
///   - <see cref="TutorialObjectiveItem.MarkComplete"/>'s completion chime — via
///     <see cref="PlayCompletion"/>. Always plays; it is the authoritative "task done" signal.
///   - Per-item cleanup cues (a trash bag deposited, a blood splatter or graffiti scrubbed, a fence
///     segment repaired) broadcast by each cleanup task's ClientRpc — via <see cref="PlayCleanupItemCue"/>.
///     These exist because cleanup rows are either hand-scripted with the silent
///     <see cref="TutorialObjectiveItem.SetText"/> (Day 1) or not shown at all (optional days), so the
///     row ding never fires for them.
///
/// Rules: any cue suppresses another <see cref="TryPlay"/> cue within <see cref="DedupeWindow"/>. An
/// item cue for the item that FINISHES its task waits <see cref="CompletionSettleDelay"/> and is
/// dropped if a completion chime (e.g. Day 1's tutorial row) played meanwhile — so the last item gets
/// exactly one sound: the completion chime when a row announces it, the item cue otherwise.
/// </summary>
public static class TaskSuccessCue
{
    /// <summary>Seconds within which a second cue is considered a duplicate of the first.</summary>
    public const float DedupeWindow = 0.3f;

    /// <summary>
    /// Seconds a task-completing item cue waits for a possible completion chime. The completion
    /// RPC/NetworkVariable arrives in the same network batch as the item cue, so this is generous.
    /// </summary>
    public const float CompletionSettleDelay = 0.25f;

    private static float _lastCueTime = float.NegativeInfinity;
    private static float _completionSuppressedUntil = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _lastCueTime = float.NegativeInfinity;
        _completionSuppressedUntil = float.NegativeInfinity;
    }

    private static bool PlayedWithin(float window) => Time.unscaledTime - _lastCueTime < window;

    /// <summary>
    /// True while objective completions are state-sync artifacts rather than player achievements
    /// (see <see cref="SuppressCompletionCuesFor"/>).
    /// </summary>
    public static bool IsCompletionSuppressed => Time.unscaledTime < _completionSuppressedUntil;

    /// <summary>
    /// Silences <see cref="PlayCompletion"/> for the next <paramref name="seconds"/> (unscaled),
    /// replacing any previous window. Used by <see cref="ShiftManager"/> while a saved day is being
    /// resumed: rows are added and removed by several systems (timecard reset/restore, HUDTaskList
    /// registry sync, task restores) before the player has control, and none of those removals is
    /// a real completion. Time-bounded so an interrupted resume can never mute the chime for good.
    /// </summary>
    public static void SuppressCompletionCuesFor(float seconds) =>
        _completionSuppressedUntil = Time.unscaledTime + Mathf.Max(0f, seconds);

    /// <summary>Plays <paramref name="clip"/> unless another task cue played within <see cref="DedupeWindow"/>.</summary>
    public static bool TryPlay(AudioClip clip, float volume)
    {
        if (clip == null || IsCompletionSuppressed || PlayedWithin(DedupeWindow)) return false;

        _lastCueTime = Time.unscaledTime;
        SFXController.Instance?.Play(clip, volume);
        return true;
    }

    /// <summary>Always plays the completion chime and records it so a trailing item cue yields to it.</summary>
    public static void PlayCompletion(AudioClip clip, float volume)
    {
        if (clip == null || IsCompletionSuppressed) return;

        _lastCueTime = Time.unscaledTime;
        SFXController.Instance?.Play(clip, volume);
    }

    /// <summary>
    /// Plays a per-item cleanup success cue on this client. Call from a ClientRpc so each finished
    /// item produces exactly one cue per client.
    /// </summary>
    /// <param name="host">Active behaviour used to run the settle delay (the task itself).</param>
    /// <param name="completesTask">True when this item finished the whole task; see class remarks.</param>
    public static void PlayCleanupItemCue(MonoBehaviour host, AudioClip clip, float volume, bool completesTask)
    {
        if (clip == null || IsCompletionSuppressed) return;

        if (!completesTask || host == null || !host.isActiveAndEnabled)
        {
            TryPlay(clip, volume);
            return;
        }

        host.StartCoroutine(PlayAfterCompletionSettles(clip, volume));
    }

    private static IEnumerator PlayAfterCompletionSettles(AudioClip clip, float volume)
    {
        yield return new WaitForSecondsRealtime(CompletionSettleDelay);

        // A completion chime (or any other cue) around the moment this item finished the task
        // already announced it — don't stack a second sound on top.
        if (IsCompletionSuppressed || PlayedWithin(CompletionSettleDelay + DedupeWindow)) yield break;

        _lastCueTime = Time.unscaledTime;
        SFXController.Instance?.Play(clip, volume);
    }
}
