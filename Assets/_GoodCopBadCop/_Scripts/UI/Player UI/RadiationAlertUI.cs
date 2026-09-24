using UnityEngine;

/// <summary>
/// Subscribes to <see cref="PlayerRadiation.OnRadiationChanged"/> and shows a persistent
/// bottom-of-screen alert (via <see cref="UIController.ShowRadiationAlert"/>) whenever the
/// local player's radiation is at or above <see cref="_highRadiationThreshold"/>. The alert
/// keeps resurfacing (same looping style as the "shipment is waiting at the gate" alert) until
/// radiation drops back below the threshold, at which point it is hidden.
///
/// The alert is always suppressed while the local player is dead, while the pause menu is
/// open, or while the main menu is showing — see <see cref="RefreshVisibility"/>.
/// </summary>
public class RadiationAlertUI : MonoBehaviour
{
    [Tooltip("Normalized radiation (0-1) at or above which the high-radiation alert is shown.")]
    [SerializeField] private float _highRadiationThreshold = 0.5f;

    [SerializeField] private string _alertMessage = "Radiation high. Take pills to reduce.";

    private PlayerRadiation _playerRadiation;
    private PlayerHealth _playerHealth;

    /// <summary>Whether radiation is currently high enough to warrant the alert, ignoring menus/death.</summary>
    private bool _radiationIsHigh;

    /// <summary>Whether the alert is currently being displayed via the UIController.</summary>
    private bool _alertShown;

    private void OnEnable()
    {
        UIController.OnPauseMenuOpened += HandlePauseMenuOpened;

        SubscribeTo(PlayerInstance.Instance);
    }

    private void Update()
    {
        // Re-check every frame, not just when the cached refs are null: death/respawn keeps
        // the old (corpse) PlayerInstance alive rather than destroying it (see
        // PlayerInstance.DetachFromPlayerObject), so our cached PlayerRadiation/PlayerHealth
        // never actually become null on their own — they just silently stop firing events on
        // the corpse. Comparing against the current PlayerInstance.Instance catches that swap.
        if (PlayerInstance.Instance != _subscribedInstance)
            SubscribeTo(PlayerInstance.Instance);

        // Pause menu / main menu visibility can change without firing a dedicated event this
        // script listens to (e.g. closing the pause menu), so re-evaluate every frame.
        RefreshVisibility();
    }

    private void OnDisable()
    {
        UIController.OnPauseMenuOpened -= HandlePauseMenuOpened;

        SubscribeTo(null);

        ForceHide();
    }

    private PlayerInstance _subscribedInstance;

    private void SubscribeTo(PlayerInstance playerInstance)
    {
        if (_playerRadiation != null)
        {
            _playerRadiation.OnRadiationChanged.RemoveListener(OnRadiationChanged);
            _playerRadiation = null;
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnDeath -= HandlePlayerDeath;
            _playerHealth = null;
        }

        // Drop the flag from the previous instance. The corpse's radiation keeps accruing on the
        // server after death, so without this a stale "high" value would survive the swap to a
        // null instance (revive handoff / despawn), where there's no PlayerHealth left to report
        // IsDead — and the alert would pop up while the player is still dead.
        _radiationIsHigh = false;

        _subscribedInstance = playerInstance;
        if (playerInstance == null)
        {
            RefreshVisibility();
            return;
        }

        if (playerInstance.PlayerRadiation != null)
        {
            _playerRadiation = playerInstance.PlayerRadiation;
            _playerRadiation.OnRadiationChanged.AddListener(OnRadiationChanged);
            OnRadiationChanged(_playerRadiation.CurrentRadiation, _playerRadiation.MaxRadiation);
        }

        if (playerInstance.PlayerHealth != null)
        {
            _playerHealth = playerInstance.PlayerHealth;
            _playerHealth.OnDeath += HandlePlayerDeath;
        }
    }

    private void OnRadiationChanged(float current, float max)
    {
        _radiationIsHigh = max > 0f && (current / max) >= _highRadiationThreshold;
        RefreshVisibility();
    }

    /// <summary>Immediately drops the "high radiation" flag on death so the alert can't resurface until it's earned again.</summary>
    private void HandlePlayerDeath()
    {
        _radiationIsHigh = false;
        RefreshVisibility();
    }

    private void HandlePauseMenuOpened()
    {
        RefreshVisibility();
    }

    /// <summary>
    /// Recomputes whether the alert should currently be visible and syncs it with the
    /// UIController, suppressing it whenever the local player is dead or a menu (pause or main
    /// menu) is covering the screen.
    /// </summary>
    private void RefreshVisibility()
    {
        bool isDead = _playerHealth != null && _playerHealth.IsDead;
        bool isPaused = UIController.Instance != null && UIController.Instance.IsPaused;
        bool isMainMenuActive = MainMenuController.Instance != null &&
                                 MainMenuController.Instance.mainMenu != null &&
                                 MainMenuController.Instance.mainMenu.activeSelf;

        bool shouldShow = _radiationIsHigh && !isDead && !isPaused && !isMainMenuActive;

        if (shouldShow == _alertShown) return;

        _alertShown = shouldShow;

        if (shouldShow)
            UIController.Instance?.ShowRadiationAlert(_alertMessage);
        else
            UIController.Instance?.HideRadiationAlert();
    }

    private void ForceHide()
    {
        if (!_alertShown) return;

        _alertShown = false;
        UIController.Instance?.HideRadiationAlert();
    }
}
