using UnityEngine;

/// <summary>
/// HUD icon for the guidebook. Manages a notification badge (!) that bobs up and down while:
/// <list type="bullet">
///   <item>new tasks were assigned (via <see cref="TaskRegistry"/>) since the guidebook was last opened, or</item>
///   <item>any unlocked guidebook entry is still unseen (<see cref="GuidebookSeenState"/>). This part
///   stays on until the player opens the section holding the new entries — the matching section
///   tab inside the book shows the same "!".</item>
/// </list>
/// </summary>
public class GuidebookIcon : MonoBehaviour
{
    private const float RefreshInterval = 1f;

    [Tooltip("The notification badge GameObject (the ! indicator).")]
    [SerializeField] private GameObject _notificationBadge;

    [Tooltip("Sound played when a new task is added to the guidebook.")]
    [SerializeField] private AudioClip _taskAddedSound;

    [Tooltip("Guidebook content, used to check for unlocked entries the player hasn't seen yet.")]
    [SerializeField] private GuidebookDatabase _database;

    [Header("Bob")]
    [Tooltip("Vertical bob distance of the badge, in canvas units.")]
    [SerializeField] private float _bobAmplitude = 4f;
    [Tooltip("Bobs per second.")]
    [SerializeField] private float _bobFrequency = 1.6f;

    private bool _hasPendingTasks;
    private float _nextRefreshTime;
    private RectTransform _badgeRect;
    private Vector2 _badgeBasePosition;

    private void Awake()
    {
        if (_notificationBadge != null)
        {
            _badgeRect = _notificationBadge.transform as RectTransform;
            if (_badgeRect != null) _badgeBasePosition = _badgeRect.anchoredPosition;
            _notificationBadge.SetActive(false);
        }
    }

    private void OnEnable()
    {
        TaskRegistry.OnTasksAdded += HandleTasksAdded;
        AnomalyUnlockManager.OnAnomalyUnlocked += HandleAnomalyUnlocked;
        GuidebookController.OnGuidebookOpened += HandleGuidebookOpened;
        GuidebookSeenState.OnChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        TaskRegistry.OnTasksAdded -= HandleTasksAdded;
        AnomalyUnlockManager.OnAnomalyUnlocked -= HandleAnomalyUnlocked;
        GuidebookController.OnGuidebookOpened -= HandleGuidebookOpened;
        GuidebookSeenState.OnChanged -= Refresh;
    }

    private void Update()
    {
        // Catches unlocks restored from a save slot, which don't raise OnAnomalyUnlocked.
        if (Time.unscaledTime >= _nextRefreshTime) Refresh();

        if (_badgeRect != null && _notificationBadge.activeSelf)
        {
            float bob = Mathf.Sin(Time.unscaledTime * _bobFrequency * Mathf.PI * 2f) * _bobAmplitude;
            _badgeRect.anchoredPosition = _badgeBasePosition + new Vector2(0f, bob);
        }
    }

    private void HandleTasksAdded()
    {
        _hasPendingTasks = true;
        Refresh();

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
        Refresh();
    }

    /// <summary>Opening the book acknowledges new tasks; unseen entries stay flagged until their section is opened.</summary>
    private void HandleGuidebookOpened()
    {
        _hasPendingTasks = false;
        Refresh();
    }

    private void Refresh()
    {
        if (this == null) return;
        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        if (_notificationBadge == null) return;

        bool show = _hasPendingTasks || GuidebookSeenState.HasNewEntries(_database);
        if (_notificationBadge.activeSelf == show) return;

        _notificationBadge.SetActive(show);
        if (!show && _badgeRect != null) _badgeRect.anchoredPosition = _badgeBasePosition;
    }
}
