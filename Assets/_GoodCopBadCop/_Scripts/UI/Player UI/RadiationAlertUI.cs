using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerRadiation.OnRadiationChanged"/> and shows a persistent
/// bottom-of-screen alert (via <see cref="UIController.ShowRadiationAlert"/>) whenever the
/// local player's radiation is at or above <see cref="_highRadiationThreshold"/>. The alert
/// keeps resurfacing (same looping style as the "shipment is waiting at the gate" alert) until
/// radiation drops back below the threshold, at which point it is hidden.
/// </summary>
public class RadiationAlertUI : MonoBehaviour
{
    [Tooltip("Normalized radiation (0-1) at or above which the high-radiation alert is shown.")]
    [SerializeField] private float _highRadiationThreshold = 0.5f;

    [SerializeField] private string _alertMessage = "Radiation high. Take pills to reduce.";

    private PlayerRadiation _playerRadiation;
    private bool _alertShown;

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

    private void OnDisable()
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }

        if (_alertShown)
        {
            _alertShown = false;
            UIController.Instance?.HideRadiationAlert();
        }
    }

    private void SubscribeTo(PlayerRadiation playerRadiation)
    {
        _playerRadiation = playerRadiation;
        _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
        OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
    }

    private void OnRadiationChanged(float current, float max)
    {
        if (max <= 0f) return;

        float normalized = current / max;
        bool shouldShow = normalized >= _highRadiationThreshold;

        if (shouldShow == _alertShown) return;

        _alertShown = shouldShow;

        if (shouldShow)
            UIController.Instance?.ShowRadiationAlert(_alertMessage);
        else
            UIController.Instance?.HideRadiationAlert();
    }
}
