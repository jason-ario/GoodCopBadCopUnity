using UnityEngine;

/// <summary>
/// Defines a task delivered to the player via a phone call from HQ.
/// Stored inline on the <see cref="Telephone"/> MonoBehaviour so that
/// <c>_linkedTaskBehaviour</c> can reference other scene objects directly.
/// Add entries to <c>Telephone._availableTasks</c> via the Inspector.
/// </summary>
[System.Serializable]
public class PhoneTaskData
{
    [Header("Task Info")]
    [SerializeField] private string _taskName = "New Task";
    [SerializeField, TextArea(2, 4)] private string _taskDescription = "Complete the task.";
    [SerializeField] private int _couponReward = 5;

    [Header("Voice Line")]
    [Tooltip("Text shown as subtitles when the player answers the call.")]
    [SerializeField, TextArea(2, 6)] private string _voiceLine = "This is HQ. You have a new task.";
    [Tooltip("Audio clips cycled to produce the voice effect. Leave empty for text-only subtitles.")]
    [SerializeField] private AudioClip[] _voiceAudioClips;

    [Header("Linked Task (Optional)")]
    [Tooltip("When assigned, answering this call resets and registers this pre-existing task " +
             "instead of creating a dynamic PhoneCallTask. The MonoBehaviour must implement IBetweenShiftTask. " +
             "Use this for tasks backed by BetweenShiftTaskManager (e.g. TakeOutTrashTask).")]
    [SerializeField] private MonoBehaviour _linkedTaskBehaviour;

    /// <summary>Display name shown in the guidebook task list.</summary>
    public string TaskName => _taskName;

    /// <summary>Short description shown in the guidebook task list.</summary>
    public string TaskDescription => _taskDescription;

    /// <summary>Money awarded upon task completion.</summary>
    public int CouponReward => _couponReward;

    /// <summary>Text displayed as subtitles when the player picks up the phone.</summary>
    public string VoiceLine => _voiceLine;

    /// <summary>Audio clips played to produce the HQ voice. Cycled by DialogueManager.</summary>
    public AudioClip[] VoiceAudioClips => _voiceAudioClips;

    /// <summary>
    /// Optional pre-existing <see cref="IBetweenShiftTask"/> to reset and register when this call is answered.
    /// When set, the phone system registers this task directly in <see cref="TaskRegistry"/>
    /// instead of creating a new <see cref="PhoneCallTask"/>, preserving the existing networked
    /// completion tracking in <see cref="BetweenShiftTaskManager"/>.
    /// Returns null if <c>_linkedTaskBehaviour</c> is unassigned or does not implement <see cref="IBetweenShiftTask"/>.
    /// </summary>
    public IBetweenShiftTask LinkedTask => _linkedTaskBehaviour as IBetweenShiftTask;
}
