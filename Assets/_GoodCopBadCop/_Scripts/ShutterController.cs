using DG.Tweening;
using UnityEngine;

public class ShutterController : MonoBehaviour
{
    public static ShutterController Instance { get; private set; }

    [SerializeField] Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;

    [Header("Hit Feedback")]
    [Tooltip("Sound played on all clients each time the mutant hits the shutter.")]
    [SerializeField] private AudioClip hitClip;
    [Tooltip("The shutter mesh Transform to shake. Assign a child visual so it doesn't fight the Animator root.")]
    [SerializeField] private Transform shutterVisual;
    [Tooltip("Duration of the shake in seconds.")]
    [SerializeField] private float shakeDuration = 0.25f;
    [Tooltip("Maximum positional offset during the shake.")]
    [SerializeField] private float shakeStrength = 0.04f;
    [Tooltip("Number of oscillations during the shake.")]
    [SerializeField] private int shakeVibrato = 12;

    private Tween _shakeTween;

    /// <summary>True while the booth window is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// When true, <see cref="CloseShutter"/> is a no-op.
    /// Set by scripted sequences (e.g. Day_01 Vlad arrival) to prevent
    /// the shutter from being closed while a scripted visitor is at the window.
    /// Cleared automatically by <see cref="ResetShutter"/>.
    /// </summary>
    public bool ShutterLockedOpen { get; set; }

    private void Awake()
    {
        Instance = this;
    }

    public void OpenShutter()
    {
        IsOpen = true;
        animator.SetBool("Open", true);
        audioSource.PlayOneShot(openClip);
    }

    /// <summary>
    /// Closes the shutter. Has no effect when <see cref="ShutterLockedOpen"/> is true.
    /// </summary>
    public void CloseShutter()
    {
        if (ShutterLockedOpen) return;

        IsOpen = false;
        animator.SetBool("Open", false);
        audioSource.PlayOneShot(closeClip);
    }

    public void ResetShutter()
    {
        ShutterLockedOpen = false;
        IsOpen = false;
        animator.SetBool("Open", false);
        animator.SetTrigger("Reset");
    }

    /// <summary>
    /// Plays the hit sound and shakes the shutter visual.
    /// Called on all clients via <see cref="MutantSuspectBehaviour"/> ClientRpc each time the mutant strikes.
    /// </summary>
    public void OnHitByMutant()
    {
        if (hitClip != null)
            audioSource.PlayOneShot(hitClip);

        if (shutterVisual != null)
        {
            // Kill any in-progress shake before starting a new one so rapid hits don't stack.
            _shakeTween?.Kill(complete: true);
            _shakeTween = shutterVisual.DOShakePosition(shakeDuration, shakeStrength, shakeVibrato);
        }
    }
}
