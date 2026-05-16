using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerRadiation.OnRadiationChanged"/> and drives
/// the inherited <see cref="StatBar"/> visuals with current radiation values.
/// </summary>
public class RadiationBarUI : StatBar
{
    private PlayerRadiation _playerRadiation;

    private void OnEnable()
    {
        if (PlayerInstance.Instance == null) return;

        SubscribeTo(PlayerInstance.Instance.PlayerRadiation);
    }

    private void Update()
    {
        if (_playerRadiation != null) return;
        if (PlayerInstance.Instance == null) return;

        SubscribeTo(PlayerInstance.Instance.PlayerRadiation);
    }

    protected override void OnDisable()
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }

        base.OnDisable();
    }

    /// <summary>Subscribes to the given PlayerRadiation instance and immediately refreshes the bar.</summary>
    private void SubscribeTo(PlayerRadiation playerRadiation)
    {
        _playerRadiation = playerRadiation;
        _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
        OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnRadiationChanged(float current, float max) => UpdateBar(current, max);
}
