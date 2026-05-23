using UnityEngine;

/// <summary>
/// HUD icon for the guidebook. Manages a notification badge (!) that activates
/// when new tasks are assigned — either via BetweenShiftTaskManager (night phase)
/// or directly via GuidebookTaskRegistry (any system, any time) — and deactivates
/// once the player views the Tasks tab.
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
        GuidebookTaskRegistry.OnTasksAdded += ShowNotification;
        GuidebookTabController.OnTasksTabViewed += HideNotification;
        GuidebookController.OnGuidebookOpened += HideNotification;
    }

    private void OnDisable()
    {
        GuidebookTaskRegistry.OnTasksAdded -= ShowNotification;
        GuidebookTabController.OnTasksTabViewed -= HideNotification;
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

    /// <summary>Deactivates the notification badge once the player has viewed the Tasks tab.</summary>
    private void HideNotification()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(false);
    }
}
