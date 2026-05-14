using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;

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

    private void OnDisable()
    {
        if (_playerHealth == null) return;
        _playerHealth.OnHealthChanged -= UpdateBar;
        _playerHealth = null;
    }

    /// <summary>Subscribes to the given PlayerHealth instance and immediately refreshes the bar.</summary>
    private void SubscribeTo(PlayerHealth playerHealth)
    {
        _playerHealth = playerHealth;
        _playerHealth.OnHealthChanged += UpdateBar;
        UpdateBar();
    }

    private void UpdateBar()
    {
        if (_playerHealth == null || fillImage == null) return;
        fillImage.fillAmount = _playerHealth.MaxHealth > 0f
            ? _playerHealth.Health / _playerHealth.MaxHealth
            : 0f;
    }

    /// <summary>Shows the health bar.</summary>
    public void Show() => gameObject.SetActive(true);

    /// <summary>Hides the health bar.</summary>
    public void Hide() => gameObject.SetActive(false);
}
