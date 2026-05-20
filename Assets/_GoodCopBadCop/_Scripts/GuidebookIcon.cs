using UnityEngine;

/// <summary>
/// HUD icon for the guidebook. Manages a notification badge (!) that activates
/// when new tasks are assigned and deactivates once the player views the Tasks tab.
/// </summary>
public class GuidebookIcon : MonoBehaviour
{
    [Tooltip("The notification badge GameObject (the ! indicator).")]
    [SerializeField] private GameObject _notificationBadge;

    private void Awake()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(false);
    }

    private void OnEnable()
    {
        BetweenShiftTaskManager.OnTasksAssigned += ShowNotification;
        GuidebookTabController.OnTasksTabViewed += HideNotification;
    }

    private void OnDisable()
    {
        BetweenShiftTaskManager.OnTasksAssigned -= ShowNotification;
        GuidebookTabController.OnTasksTabViewed -= HideNotification;
    }

    /// <summary>Activates the notification badge to signal new tasks are available.</summary>
    private void ShowNotification()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(true);
    }

    /// <summary>Deactivates the notification badge once the player has viewed the Tasks tab.</summary>
    private void HideNotification()
    {
        if (_notificationBadge != null)
            _notificationBadge.SetActive(false);
    }
}
