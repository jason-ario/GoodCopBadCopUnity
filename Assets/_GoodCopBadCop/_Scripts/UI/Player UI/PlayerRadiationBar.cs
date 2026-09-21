using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerRadiation.OnRadiationChanged"/> and drives
/// the inherited <see cref="StatBar"/> visuals with current radiation values.
/// </summary>
public class RadiationBarUI : StatBar
{
    private PlayerRadiation _playerRadiation;
    private PlayerInstance _subscribedInstance;

    private void OnEnable()
    {
        SubscribeTo(PlayerInstance.Instance);
    }

    private void Update()
    {
        // Compare against the current PlayerInstance rather than just checking for a null
        // PlayerRadiation: death/respawn keeps the old (corpse) PlayerInstance alive instead of
        // destroying it (see PlayerInstance.DetachFromPlayerObject), so a cached PlayerRadiation
        // reference never becomes null on its own — it just stops firing events on the corpse,
        // freezing this bar at whatever radiation value it had at death.
        if (PlayerInstance.Instance != _subscribedInstance)
            SubscribeTo(PlayerInstance.Instance);
    }

    protected override void OnDisable()
    {
        SubscribeTo(null);

        base.OnDisable();
    }

    /// <summary>Subscribes to the given PlayerInstance's PlayerRadiation and immediately refreshes the bar. Pass null to unsubscribe.</summary>
    private void SubscribeTo(PlayerInstance playerInstance)
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }

        _subscribedInstance = playerInstance;
        if (playerInstance == null || playerInstance.PlayerRadiation == null) return;

        _playerRadiation = playerInstance.PlayerRadiation;
        _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
        OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnRadiationChanged(float current, float max) => UpdateBar(current, max);
}
