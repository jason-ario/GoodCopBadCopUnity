using UnityEngine;

/// <summary>
/// A single circuit breaker switch on the electrical panel.
/// Toggled on click while the player is inside the diegetic view.
/// Call <see cref="SetSwitchOff"/> to reset the switch during a power outage.
/// </summary>
public class CircuitSwitch : MonoBehaviour, IClickable
{
    private static readonly int SwitchOnHash   = Animator.StringToHash("SwitchOn");
    private const string SwitchOnStateName  = "Switch On";
    private const string SwitchOffStateName = "Switch Off";

    [SerializeField] private Animator _animator;

    [Header("Audio")]
    [Tooltip("AudioSource used to play the switch flip sound. Falls back to a component on this GameObject if left empty.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Sound played whenever the switch is flipped (either direction).")]
    [SerializeField] private AudioClip _switchSound;

    /// <summary>True when the switch is in the On position.</summary>
    public bool IsOn { get; private set; } = true;

    private void Awake()
    {
        if (_animator == null)
            _animator = GetComponent<Animator>();
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    /// <summary>
    /// Snaps the switch to the Off state. Called by
    /// <see cref="ElectricPanelController.OnPowerOff"/> when a power outage begins, or by
    /// <see cref="ElectricPanelDiegeticController"/> when the puzzle resets because not all
    /// switches were On when the knob reached its On position.
    /// </summary>
    public void SetSwitchOff()
    {
        bool wasOn = IsOn;
        IsOn = false;
        ApplyAnimator();
        if (wasOn) PlaySwitchSound();
    }

    /// <summary>
    /// Snaps the switch to the On state without playing a sound. Called by
    /// <see cref="ElectricPanelController.OnPowerOn"/> to keep the switch visuals in sync
    /// whenever the power is already/becomes on (e.g. late-joining clients, puzzle solved).
    /// </summary>
    public void SetSwitchOn()
    {
        IsOn = true;
        ApplyAnimator();
    }

    // ─── IClickable ──────────────────────────────────────────────────────────

    /// <summary>Toggles the switch on click from within the diegetic view.</summary>
    public void OnClick()
    {
        IsOn = !IsOn;
        ApplyAnimator();
        PlaySwitchSound();
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void ApplyAnimator()
    {
        if (_animator == null) return;
        // Keep the bool in sync for any external inspector reads.
        _animator.SetBool(SwitchOnHash, IsOn);
        // CrossFade directly to the target state so the transition fires even
        // when the animator is mid-blend or in a looping clip (Switch On loops).
        _animator.CrossFade(IsOn ? SwitchOnStateName : SwitchOffStateName, 0.25f, 0, 0f);
    }

    private void PlaySwitchSound()
    {
        if (_audioSource == null || _switchSound == null) return;
        _audioSource.PlayOneShot(_switchSound);
    }
}
