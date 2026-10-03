using UnityEngine;

/// <summary>
/// HUD icon for the guidebook. Manages a notification badge (!) that activates when new tasks
/// are assigned (via <see cref="TaskRegistry"/>) or when new anomaly pages are unlocked in the
/// guidebook (via <see cref="AnomalyUnlockManager.OnAnomalyUnlocked"/>), and deactivates once
/// the player opens the guidebook.
/// </summary>
public class GuidebookIcon : MonoBehaviour
{
    [Tooltip("The notification badge GameObject (the ! indicator).")]
    [SerializeField] private GameObject _notificationBadge;

    [Tooltip("Sound played when a new task is added to the guidebook.")]
    [SerializeField] private AudioClip _taskAddedSound;

    private void Awake()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(false);
    }

    private void OnEnable()
    {
        TaskRegistry.OnTasksAdded += ShowNotification;
        AnomalyUnlockManager.OnAnomalyUnlocked += HandleAnomalyUnlocked;
        GuidebookController.OnGuidebookOpened += HideNotification;
    }

    private void OnDisable()
    {
        TaskRegistry.OnTasksAdded -= ShowNotification;
        AnomalyUnlockManager.OnAnomalyUnlocked -= HandleAnomalyUnlocked;
        GuidebookController.OnGuidebookOpened -= HideNotification;
    }

    /// <summary>Activates the notification badge to signal new tasks are available.</summary>
    private void ShowNotification()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(true);

        if (_taskAddedSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_taskAddedSound);
    }

    /// <summary>
    /// New guidebook pages are available. Silent — a whole category unlocks at once, so a sound
    /// per anomaly would stack.
    /// </summary>
    private void HandleAnomalyUnlocked(string _)
    {
        if (this == null) return; // static event can outlive a scene reload
        if (_notificationBadge != null)
            _notificationBadge.SetActive(true);
    }

    /// <summary>Deactivates the notification badge once the player opens the guidebook.</summary>
    private void HideNotification()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(false);
    }
}
