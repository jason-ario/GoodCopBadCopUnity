using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton controller for the tutorial objective list panel.
/// Slides in from the right when the first objective of a sequence is added,
/// shows strikethroughs as objectives are completed, then hides and destroys
/// all items when the sequence ends via <see cref="HideAndClear"/>.
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

    private static readonly int IsShowingHash = Animator.StringToHash("IsShowing");

    [Header("References")]
    [SerializeField] private GameObject objectiveListRoot;
    [SerializeField] private Animator listAnimator;
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskItemPrefab;

    [Header("Settings")]
    [Tooltip("Seconds to wait after triggering the hide animation before destroying all items.")]
    [SerializeField] private float hideAnimDuration = 0.8f;

    [Header("Audio")]
    [Tooltip("Played once via SFXController whenever a new objective row is added to the list.")]
    [SerializeField] private AudioClip newTaskSound;
    [Tooltip("Volume multiplier for newTaskSound (before global SFX volume scaling).")]
    [SerializeField] private float newTaskSoundVolume = 1f;

    private bool _isShowing;
    private readonly List<TutorialObjectiveItem> _items = new();
    private Coroutine _clearCoroutine;

    // ── Unity Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Start()
    {
        // Clear design-time placeholder items then deactivate the card root.
        // The root stays inactive until the first objective of a sequence is added.
        ClearAllItems();
        objectiveListRoot.SetActive(false);
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new objective row to the list. Automatically slides the panel
    /// in if it is not already visible.
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
    /// Hides the objective list with its slide-out animation, then destroys all items.
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
    /// <param name="preHideDelay">Seconds to pause before starting the hide animation, so completed tasks remain visible.</param>
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
    /// The panel only plays its slide-out-and-hide animation once this was the LAST remaining
    /// row — safe to use even while other tasks are still being tracked in the same shared list.
    /// Safe to call with a null <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The objective row to complete and remove.</param>
    /// <param name="preHideDelay">Seconds to pause after marking complete before the row (and, if last, the whole panel) actually hides.</param>
    /// <param name="onComplete">Optional callback fired once the row (and panel, if applicable) has been removed.</param>
    public void CompleteAndRemoveObjective(TutorialObjectiveItem item, float preHideDelay = 0f, Action onComplete = null)
    {
        if (item == null) return;

        item.MarkComplete();

        if (!gameObject.activeInHierarchy)
        {
            _items.Remove(item);
            Destroy(item.gameObject);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RemoveObjectiveRoutine(item, preHideDelay, onComplete));
    }

    // ── Private ─────────────────────────────────────────────────────────

    private IEnumerator RemoveObjectiveRoutine(TutorialObjectiveItem item, float preHideDelay, Action onComplete)
    {
        if (preHideDelay > 0f)
            yield return new WaitForSeconds(preHideDelay);

        _items.Remove(item);
        if (item != null)
            Destroy(item.gameObject);

        // Only slide the panel out and deactivate it once every tracked row is gone —
        // other concurrently-tracked tasks may still have rows showing.
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

    private void Show()
    {
        _isShowing = true;
        objectiveListRoot.SetActive(true);
        listAnimator.SetBool(IsShowingHash, true);
        Debug.Log($"[TutorialObjectiveList] Show() — objectiveListRoot.activeSelf={objectiveListRoot.activeSelf}, " +
                  $"activeInHierarchy={objectiveListRoot.activeInHierarchy}, listAnimator.enabled={listAnimator.enabled}, " +
                  $"listAnimator.gameObject.activeInHierarchy={listAnimator.gameObject.activeInHierarchy}, " +
                  $"runtimeAnimatorController={(listAnimator.runtimeAnimatorController != null ? listAnimator.runtimeAnimatorController.name : "NULL")}");
    }

    private IEnumerator HideAndClearRoutine(float preHideDelay, Action onComplete)
    {
        // Let the player see completed tasks before hiding.
        if (preHideDelay > 0f)
            yield return new WaitForSeconds(preHideDelay);

        listAnimator.SetBool(IsShowingHash, false);

        // Wait for the slide-out animation to finish, then deactivate and clear.
        yield return new WaitForSeconds(hideAnimDuration);

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

        // Also destroy any children not tracked in _items (e.g. design-time placeholders).
        if (taskListContainer == null) return;
        for (int i = taskListContainer.childCount - 1; i >= 0; i--)
            Destroy(taskListContainer.GetChild(i).gameObject);
    }
}
