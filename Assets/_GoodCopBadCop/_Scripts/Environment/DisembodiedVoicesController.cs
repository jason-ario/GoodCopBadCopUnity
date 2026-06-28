using System.Collections;
using UnityEngine;

/// <summary>
/// Scene-level controller that plays disembodied whisper clips at random intervals
/// from the booth window position. Referenced by <see cref="DisembodiedVoicesAnomaly"/>
/// to start and stop playback when the anomaly is active.
/// </summary>
public class DisembodiedVoicesController : MonoBehaviour
{
    [Header("Whisper Clips")]
    [Tooltip("Pool of AudioClips to choose from at random. Assign the Voice_Creature clips here.")]
    [SerializeField] private AudioClip[] whisperClips;

    [Header("Interval")]
    [Tooltip("Minimum seconds to wait between whisper playbacks.")]
    [SerializeField] private float minInterval = 12f;

    [Tooltip("Maximum seconds to wait between whisper playbacks.")]
    [SerializeField] private float maxInterval = 35f;

    [Header("Audio Settings")]
    [Tooltip("Playback volume for each whisper clip.")]
    [SerializeField] [Range(0f, 1f)] private float volume = 0.75f;

    [Tooltip("Pitch range applied randomly to each clip to keep repetition from feeling mechanical.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    [Tooltip("Maximum audible distance in metres for spatial playback.")]
    [SerializeField] private float maxDistance = 8f;

    [Header("Debug")]
    [Tooltip("Press this key in Play Mode to immediately trigger a single whisper.")]
    [SerializeField] private KeyCode testKey = KeyCode.V;

    private Coroutine _whisperLoopCoroutine;

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
            TriggerTestWhisper();
    }

    /// <summary>
    /// Starts the periodic whisper loop. Safe to call even if already running — duplicate calls are ignored.
    /// </summary>
    public void StartWhispering()
    {
        if (_whisperLoopCoroutine != null) return;

        _whisperLoopCoroutine = StartCoroutine(WhisperLoop());
    }

    /// <summary>
    /// Stops the whisper loop. Any clip currently playing through <see cref="SFXController"/>
    /// will finish naturally since it is managed by the emitter prefab.
    /// </summary>
    public void StopWhispering()
    {
        if (_whisperLoopCoroutine == null) return;

        StopCoroutine(_whisperLoopCoroutine);
        _whisperLoopCoroutine = null;
    }

    /// <summary>
    /// Fires a single whisper immediately. Intended for Play Mode testing only.
    /// </summary>
    [ContextMenu("Trigger Test Whisper")]
    public void TriggerTestWhisper()
    {
        PlayWhisper();
    }

    /// <summary>
    /// Waits a random interval then plays one whisper clip. Repeats indefinitely until stopped.
    /// </summary>
    private IEnumerator WhisperLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
            PlayWhisper();
        }
    }

    /// <summary>
    /// Picks a random clip from <see cref="whisperClips"/> and plays it spatially at this
    /// transform's position via <see cref="SFXController"/>.
    /// </summary>
    private void PlayWhisper()
    {
        if (whisperClips == null || whisperClips.Length == 0)
        {
            Debug.LogWarning("[DisembodiedVoicesController] No whisper clips assigned.", this);
            return;
        }

        if (SFXController.Instance == null)
        {
            Debug.LogWarning("[DisembodiedVoicesController] SFXController instance not found.", this);
            return;
        }

        AudioClip clip = whisperClips[Random.Range(0, whisperClips.Length)];
        float pitch = Random.Range(pitchRange.x, pitchRange.y);

        SFXController.Instance.PlayAtPosition(clip, transform.position, volume, pitch, maxDistance);
    }
}
