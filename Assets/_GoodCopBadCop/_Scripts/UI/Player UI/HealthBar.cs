using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerHealth.OnHealthChanged"/> and drives
/// the inherited <see cref="StatBar"/> visuals with current health values.
/// </summary>
public class HealthBar : StatBar
{
    private PlayerHealth _playerHealth;

    private void OnEnable()
    {
        if (PlayerInstance.Instance?.PlayerHealth == null) return;

        SubscribeTo(PlayerInstance.Instance.PlayerHealth);
    }

    private void Update()
    {
        if (_playerHealth != null) return;
        if (PlayerInstance.Instance?.PlayerHealth == null) return;

        SubscribeTo(PlayerInstance.Instance.PlayerHealth);
    }

    protected override void OnDisable()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= OnHealthChanged;
            _playerHealth = null;
        }

        base.OnDisable();
    }

    /// <summary>Subscribes to the given PlayerHealth instance and immediately refreshes the bar.</summary>
    private void SubscribeTo(PlayerHealth playerHealth)
    {
        _playerHealth = playerHealth;
        _playerHealth.OnHealthChanged += OnHealthChanged;
        OnHealthChanged();
    }

    private void OnHealthChanged()
    {
        if (_playerHealth == null) return;

        UpdateBar(_playerHealth.Health, _playerHealth.MaxHealth);
    }
}
