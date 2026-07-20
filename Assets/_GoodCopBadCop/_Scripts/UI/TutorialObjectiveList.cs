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
    public static TutorialObjectiveList Instance { get; private set; }

    private static readonly int IsShowingHash = Animator.StringToHash("IsShowing");

    [Header("References")]
    [SerializeField] private GameObject objectiveListRoot;
    [SerializeField] private Animator listAnimator;
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskItemPrefab;

    [Header("Settings")]
    [Tooltip("Seconds to wait after triggering the hide animation before destroying all items.")]
    [SerializeField] private float hideAnimDuration = 0.8f;

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
    /// Hides the objective list with its slide-out animation, then destroys all items.
    /// Safe to call when the list is not showing.
    /// </summary>
    /// <param name="preHideDelay">Seconds to pause before starting the hide animation, so completed tasks remain visible.</param>
    /// <param name="onComplete">Optional callback fired after items are destroyed.</param>
    public void HideAndClear(float preHideDelay = 0f, Action onComplete = null)
    {
        if (!_isShowing) return;

        _isShowing = false;

        if (_clearCoroutine != null) StopCoroutine(_clearCoroutine);
        _clearCoroutine = StartCoroutine(HideAndClearRoutine(preHideDelay, onComplete));
    }

    // ── Private ─────────────────────────────────────────────────────────

    private void Show()
    {
        _isShowing = true;
        objectiveListRoot.SetActive(true);
        listAnimator.SetBool(IsShowingHash, true);
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
