using System.Collections;
using UnityEngine;

/// <summary>
/// Plays random megaphone commentary clips after stamp events and kill events.
/// After any stamp, plays a random "stamp comment" clip (e.g. "Are you sure about that?").
/// After a kill, plays a random "kill laugh" clip.
/// Each trigger has a <see cref="triggerChance"/> probability of playing (default 20%).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MegaphoneCommentary : MonoBehaviour
{
    [Range(0f, 1f)]
    [SerializeField] private float triggerChance = 0.2f;

    [Header("Stamp Comment Clips")]
    [Tooltip("Played at random after any stamp. E.g. 'Are you sure about that?', 'Interesting choice'.")]
    [SerializeField] private AudioClip[] stampCommentClips;

    [Header("Kill Laugh Clips")]
    [Tooltip("Played at random after a suspect is killed.")]
    [SerializeField] private AudioClip[] killLaughClips;

    [Header("Timing")]
    [Tooltip("Delay in seconds before playing the stamp comment, so it doesn't overlap the stamp SFX.")]
    [SerializeField] private float stampCommentDelay = 1.2f;

    [Tooltip("Delay in seconds before playing the kill laugh.")]
    [SerializeField] private float killLaughDelay = 2.5f;

    private AudioSource _audioSource;
    private Coroutine _pendingClip;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        FolderController.OnAnyFolderStamped += OnStamped;
        ShiftManager.OnSuspectKilled += OnKilled;
    }

    private void OnDisable()
    {
        FolderController.OnAnyFolderStamped -= OnStamped;
        ShiftManager.OnSuspectKilled -= OnKilled;
    }

    private void OnStamped()
    {
        if (stampCommentClips == null || stampCommentClips.Length == 0) return;
        if (Random.value > triggerChance) return;

        AudioClip clip = stampCommentClips[Random.Range(0, stampCommentClips.Length)];
        ScheduleClip(clip, stampCommentDelay);
    }

    private void OnKilled()
    {
        if (killLaughClips == null || killLaughClips.Length == 0) return;
        if (Random.value > triggerChance) return;

        AudioClip clip = killLaughClips[Random.Range(0, killLaughClips.Length)];
        ScheduleClip(clip, killLaughDelay);
    }

    private void ScheduleClip(AudioClip clip, float delay)
    {
        if (_pendingClip != null)
            StopCoroutine(_pendingClip);

        _pendingClip = StartCoroutine(PlayAfterDelay(clip, delay));
    }

    private IEnumerator PlayAfterDelay(AudioClip clip, float delay)
    {
        yield return new WaitForSeconds(delay);
        _audioSource.PlayOneShot(clip);
        _pendingClip = null;
    }
}
