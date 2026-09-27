using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerHealth.OnHealthChanged"/> and drives
/// the inherited <see cref="StatBar"/> visuals with current health values.
/// Follows <see cref="SpectateManager.HudSubject"/>, so it shows the watched teammate's
/// health while spectating and rebinds whenever the subject changes (respawn, target switch).
/// </summary>
public class HealthBar : StatBar
{
    private PlayerHealth _playerHealth;

    private void OnEnable()
    {
        Rebind();
    }

    private void Update()
    {
        if (CurrentSubjectHealth() != _playerHealth)
            Rebind();
    }

    protected override void OnDisable()
    {
        Unsubscribe();
        base.OnDisable();
    }

    private static PlayerHealth CurrentSubjectHealth()
    {
        PlayerInstance subject = SpectateManager.HudSubject;
        return subject != null ? subject.PlayerHealth : null;
    }

    private void Rebind()
    {
        Unsubscribe();

        PlayerHealth health = CurrentSubjectHealth();
        if (health == null) return;

        _playerHealth = health;
        _playerHealth.OnHealthChanged += OnHealthChanged;
        OnHealthChanged();
    }

    private void Unsubscribe()
    {
        if (_playerHealth != null)
            _playerHealth.OnHealthChanged -= OnHealthChanged;
        _playerHealth = null;
    }

    private void OnHealthChanged()
    {
        if (_playerHealth == null) return;

        UpdateBar(_playerHealth.Health, _playerHealth.MaxHealth);
    }
}
