using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton controller for the active HUD objective rows.
/// Displays the current objectives as an unobstructed list, and removes the list
/// once its final row has been cleared.
///
/// Typical usage per sequence:
/// <code>
///   var item = TutorialObjectiveList.Instance.AddObjective("Pick up Vlad's documents");
///   // ... player completes the task ...
///   TutorialObjectiveList.Instance.CompleteObjective(item);
///   TutorialObjectiveList.Instance.HideAndClear(preHideDelay: 1.5f);
/// </code>
/// </summary>
public class TutorialObjectiveList : MonoBehaviour
{
    private static TutorialObjectiveList _instance;

    /// <summary>
    /// Self-healing singleton accessor. Falls back to a scene search (including inactive
    /// objects) if the cached reference is null or was left pointing at a destroyed object —
    /// e.g. if this panel's hierarchy is torn down and recreated by a menu-to-gameplay
    /// transition (<c>MainMenuController.TransitionToGameplay</c>) after some other script
    /// already cached the stale reference. Without this, silent no-ops on the null-conditional
    /// <c>Instance?.AddObjective(...)</c> calls used throughout the day scripts would leave
    /// tutorial objectives (e.g. Day 1's trash/graffiti tasks) permanently invisible with no
    /// error ever logged.
    /// </summary>
    public static TutorialObjectiveList Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<TutorialObjectiveList>(FindObjectsInactive.Include);
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("References")]
    [SerializeField] private GameObject objectiveListRoot;
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskItemPrefab;

    [Header("Audio")]
    [Tooltip("Played once via SFXController whenever a new objective row is added to the list.")]
    [SerializeField] private AudioClip newTaskSound;
    [Tooltip("Volume multiplier for newTaskSound (before global SFX volume scaling).")]
    [SerializeField] private float newTaskSoundVolume = 1f;

    private bool _isShowing;
    private readonly List<TutorialObjectiveItem> _items = new();
    private Coroutine _clearCoroutine;

    /// <summary>Number of tracked rows not yet marked complete.</summary>
    public int IncompleteCount
    {
        get
        {
            int count = 0;
            foreach (TutorialObjectiveItem item in _items)
            {
                if (item != null && !item.IsComplete)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Items whose <see cref="RemoveObjectiveRoutine"/> is currently mid-flight (marked complete,
    /// waiting out <c>preHideDelay</c> before the row is actually destroyed). Unity silently kills
    /// running coroutines the instant their host GameObject is deactivated — if that happens while
    /// one of these routines is mid-delay (e.g. a scene/UI toggle during a day transition), the
    /// item would otherwise be stranded forever showing its strike-through with no further event
    /// ever removing it. <see cref="OnDisable"/> force-finishes anything still pending here so a
    /// completed task can never linger on screen indefinitely.
    /// </summary>
    private readonly List<TutorialObjectiveItem> _pendingCompletedRemovals = new();

    // ── Unity Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ConfigurePlainList();
    }

    private void OnDisable()
    {
        // Coroutines don't survive their host being disabled — finish any pending
        // completed-row removals immediately rather than leaving them stuck crossed-out.
        if (_pendingCompletedRemovals.Count == 0) return;

        foreach (TutorialObjectiveItem item in _pendingCompletedRemovals)
        {
            if (item == null) continue;
            _items.Remove(item);
            Destroy(item.gameObject);
        }
        _pendingCompletedRemovals.Clear();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Start()
    {
        ClearAllItems();
        objectiveListRoot.SetActive(false);
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new objective row and makes the list visible if needed.
    /// </summary>
    /// <returns>The created <see cref="TutorialObjectiveItem"/> handle, used to mark it complete later.</returns>
    public TutorialObjectiveItem AddObjective(string text)
    {
        Debug.Log($"[TutorialObjectiveList] AddObjective(\"{text}\") called. " +
                  $"instance={(this != null)}, gameObject.activeInHierarchy={gameObject.activeInHierarchy}, " +
                  $"objectiveListRoot={(objectiveListRoot != null ? objectiveListRoot.name : "NULL")}, " +
                  $"objectiveListRoot.activeInHierarchy={(objectiveListRoot != null && objectiveListRoot.activeInHierarchy)}, " +
                  $"taskItemPrefab={(taskItemPrefab != null)}, taskListContainer={(taskListContainer != null)}");

        if (taskItemPrefab == null)
        {
            Debug.LogError("[TutorialObjectiveList] taskItemPrefab is not assigned.", this);
            return null;
        }

        // Cancel any pending hide so a new sequence can reuse the list immediately.
        if (_clearCoroutine != null)
        {
            StopCoroutine(_clearCoroutine);
            _clearCoroutine = null;
            ClearAllItems();
        }

        if (!_isShowing)
            Show();

        var go = Instantiate(taskItemPrefab, taskListContainer);
        var item = go.GetComponent<TutorialObjectiveItem>();

        if (item == null)
        {
            Debug.LogError("[TutorialObjectiveList] taskItemPrefab is missing a TutorialObjectiveItem component.", this);
            Destroy(go);
            return null;
        }

        item.SetText(text);
        _items.Add(item);
        Debug.Log($"[TutorialObjectiveList] Objective row created under '{taskListContainer.name}'. " +
                  $"_isShowing={_isShowing}, objectiveListRoot.activeInHierarchy={objectiveListRoot.activeInHierarchy}, " +
                  $"item.gameObject.activeInHierarchy={item.gameObject.activeInHierarchy}");

        SFXController.Instance?.Play(newTaskSound, newTaskSoundVolume);

        return item;
    }

    /// <summary>
    /// Marks an objective as complete by enabling its strike-through and check mark.
    /// Safe to call with a null reference.
    /// </summary>
    public void CompleteObjective(TutorialObjectiveItem item)
    {
        item?.MarkComplete();
    }

    /// <summary>
    /// Updates the display text of an in-progress objective (e.g. a live "0/3" counter).
    /// Safe to call with a null reference.
    /// </summary>
    public void UpdateObjective(TutorialObjectiveItem item, string text)
    {
        item?.UpdateText(text);
    }

    /// <summary>
    /// Hides the objective list and destroys all items.
    /// Safe to call when the list is not showing.
    ///
    /// WARNING: this unconditionally tears down every row in the shared list, including ones
    /// owned by other, still-in-progress task sequences (multiple sequences can be tracked in
    /// this list at once — e.g. Day 2's "Sort the mail" and "Fix Perimeter Fences" objectives
    /// run concurrently). Only call this when the caller is certain it owns every row currently
    /// showing (e.g. a single self-contained linear sequence, or a hard reset on day change).
    /// When a task may run alongside others, use <see cref="CompleteAndRemoveObjective"/> instead
    /// so finishing early doesn't hide a sibling task's still-active row.
    /// </summary>
    /// <param name="preHideDelay">Seconds to keep completed rows visible before removing the list.</param>
    /// <param name="onComplete">Optional callback fired after items are destroyed.</param>
    public void HideAndClear(float preHideDelay = 0f, Action onComplete = null)
    {
        if (!_isShowing) return;

        _isShowing = false;

        if (_clearCoroutine != null) StopCoroutine(_clearCoroutine);

        // If this object (or a parent) has been deactivated out from under us — e.g. a
        // ClientRpc firing on a client where the tutorial overlay isn't currently active —
        // Unity can't run a coroutine on it. Skip straight to the end state synchronously.
        if (!gameObject.activeInHierarchy)
        {
            objectiveListRoot.SetActive(false);
            ClearAllItems();
            _clearCoroutine = null;
            onComplete?.Invoke();
            return;
        }

        _clearCoroutine = StartCoroutine(HideAndClearRoutine(preHideDelay, onComplete));
    }

    /// <summary>
    /// Marks <paramref name="item"/> complete and removes only that row, leaving every other
    /// concurrently-tracked objective (e.g. a sibling task's still-in-progress row) untouched.
    /// The list is removed once this was the LAST remaining row — safe to use even while
    /// other tasks are still being tracked in the same shared list.
    /// Safe to call with a null <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The objective row to complete and remove.</param>
    /// <param name="preHideDelay">Seconds to pause after marking complete before the row (and, if last, the list) is removed.</param>
    /// <param name="onComplete">Optional callback fired once the row (and list, if applicable) has been removed.</param>
    /// <param name="markComplete">
    /// Whether to apply the strikethrough completion visual before removing. Pass <c>false</c> for
    /// rows that should simply disappear once no longer active (e.g. generic HUD task-list rows),
    /// rather than briefly lingering crossed-out — see <see cref="HUDTaskList"/>.
    /// </param>
    public void CompleteAndRemoveObjective(TutorialObjectiveItem item, float preHideDelay = 0f, Action onComplete = null, bool markComplete = true)
    {
        if (item == null) return;

        if (markComplete)
            item.MarkComplete();

        if (!gameObject.activeInHierarchy)
        {
            _items.Remove(item);
            Destroy(item.gameObject);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RemoveObjectiveRoutine(item, preHideDelay, onComplete));
        _pendingCompletedRemovals.Add(item);
    }

    // ── Private ─────────────────────────────────────────────────────────

    private IEnumerator RemoveObjectiveRoutine(TutorialObjectiveItem item, float preHideDelay, Action onComplete)
    {
        if (preHideDelay > 0f)
            yield return new WaitForSeconds(preHideDelay);

        _pendingCompletedRemovals.Remove(item);
        _items.Remove(item);
        if (item != null)
            Destroy(item.gameObject);

        // Remove the list once every tracked row is gone; sibling tasks keep it visible.
        if (_items.Count == 0 && _isShowing)
        {
            _isShowing = false;

            if (_clearCoroutine != null) StopCoroutine(_clearCoroutine);
            _clearCoroutine = StartCoroutine(HideAndClearRoutine(0f, onComplete));
        }
        else
        {
            onComplete?.Invoke();
        }
    }

    private void ConfigurePlainList()
    {
        if (objectiveListRoot == null) return;

        if (objectiveListRoot.TryGetComponent(out UnityEngine.UI.Image background))
            background.enabled = false;

        Transform header = objectiveListRoot.transform.Find("Screens/Header");
        if (header != null)
            header.gameObject.SetActive(false);

        // NOTE: taskListContainer (Tasks Panel) is a fixed-width row container sized
        // and anchored directly in the scene to fit the sidebar (see Task Sidebar/Tasks Panel).
        // Do NOT force it to a full-stretch, zero-sizeDelta rect here — that was leftover from
        // when this pointed at a different, full-bleed tutorial-overlay panel. Overwriting its
        // anchors/size on every Awake collapses its width to 0 at runtime (Tasks Panel's own
        // VerticalLayoutGroup doesn't control width, so it just reads whatever width this
        // container currently reports), which made every task row wrap to one character per
        // line the instant the list first appeared each session.
    }

    private void Show()
    {
        _isShowing = true;
        objectiveListRoot.SetActive(true);
        Debug.Log($"[TutorialObjectiveList] Show() — objectiveListRoot.activeSelf={objectiveListRoot.activeSelf}, " +
                  $"activeInHierarchy={objectiveListRoot.activeInHierarchy}");
    }

    private IEnumerator HideAndClearRoutine(float preHideDelay, Action onComplete)
    {
        // Let the player see completed tasks before hiding.
        if (preHideDelay > 0f)
            yield return new WaitForSeconds(preHideDelay);

        objectiveListRoot.SetActive(false);
        ClearAllItems();
        _clearCoroutine = null;

        onComplete?.Invoke();
    }

    private void ClearAllItems()
    {
        foreach (TutorialObjectiveItem item in _items)
        {
            if (item != null)
                Destroy(item.gameObject);
        }
        _items.Clear();
        _pendingCompletedRemovals.Clear();

        // Also destroy any children not tracked in _items (e.g. design-time placeholders).
        if (taskListContainer == null) return;
        for (int i = taskListContainer.childCount - 1; i >= 0; i--)
            Destroy(taskListContainer.GetChild(i).gameObject);
    }
}
