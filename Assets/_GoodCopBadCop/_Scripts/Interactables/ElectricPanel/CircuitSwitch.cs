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

    /// <summary>True when the switch is in the On position.</summary>
    public bool IsOn { get; private set; } = true;

    private void Awake()
    {
        if (_animator == null)
            _animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Snaps the switch to the Off state. Called by
    /// <see cref="ElectricPanelController.OnPowerOff"/> when a power outage begins.
    /// </summary>
    public void SetSwitchOff()
    {
        IsOn = false;
        ApplyAnimator();
    }

    // ─── IClickable ──────────────────────────────────────────────────────────

    /// <summary>Toggles the switch on click from within the diegetic view.</summary>
    public void OnClick()
    {
        IsOn = !IsOn;
        ApplyAnimator();
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
}
