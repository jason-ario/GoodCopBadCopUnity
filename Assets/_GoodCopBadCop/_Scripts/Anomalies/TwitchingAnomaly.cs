using System.Collections;
using UnityEngine;

/// <summary>
/// Biological anomaly that repeatedly triggers a random twitch animation on the suspect
/// at random intervals for as long as the anomaly is active.
/// The Animator Controller is expected to return to its idle state via exit time after each twitch.
/// </summary>
public class TwitchingAnomaly : VitalsAnomaly
{
    [SerializeField] private SuspectCharacter suspectCharacter;

    [Tooltip("Name of the Animator integer parameter that selects the twitch animation.")]
    [SerializeField] private string animatorIntParameter = "TwitchIndex";

    [Tooltip("Total number of twitch animations available in the Animator Controller.")]
    [SerializeField] private int animationCount = 4;

    [Header("Timing")]
    [Tooltip("How long the twitch animation plays before the parameter is reset to 0.")]
    [SerializeField] private float twitchDuration = 1f;

    [Tooltip("Minimum seconds to wait between twitch events.")]
    [SerializeField] private float minInterval = 2f;

    [Tooltip("Maximum seconds to wait between twitch events.")]
    [SerializeField] private float maxInterval = 6f;

    private Coroutine _activeCoroutine;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (suspectCharacter == null)
        {
            Debug.LogWarning($"[TwitchingAnomaly] suspectCharacter is not assigned on '{gameObject.name}'.", this);
            return;
        }

        _activeCoroutine = StartCoroutine(TwitchLoop());
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        suspectCharacter.animator.SetInteger(animatorIntParameter, 0);
    }

    [ContextMenu("Activate Anomaly")]
    private void ActivateAnomalyDebug() => ActivateAnomaly();

    /// <summary>
    /// Waits a random interval, plays a random twitch (1 to <see cref="animationCount"/>),
    /// then resets the parameter to 0 after <see cref="twitchDuration"/> seconds. Repeats indefinitely.
    /// </summary>
    private IEnumerator TwitchLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            int index = Random.Range(1, animationCount + 1);
            suspectCharacter.animator.SetInteger(animatorIntParameter, index);

            yield return new WaitForSeconds(twitchDuration);

            suspectCharacter.animator.SetInteger(animatorIntParameter, 0);
        }
    }
}
