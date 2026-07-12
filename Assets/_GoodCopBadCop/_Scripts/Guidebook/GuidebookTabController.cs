using System;
using UnityEngine;

/// <summary>
/// Wraps GuidebookPageController with named tab navigation.
/// Q / LB = previous page.  E / RB = next page.
///
/// Input and physical page animation are owned by GuidebookPageController.
/// This controller subscribes to <see cref="GuidebookPageController.OnPageChanged"/>
/// and refreshes the visible content for whichever tab is now active.
///
/// <see cref="ShowTab"/> can be called externally to jump to a specific tab
/// instantly (uses SnapTo — no animation).
///
/// Resets to tab 0 (How to Play) each time the guidebook opens (OnEnable).
/// </summary>
[RequireComponent(typeof(GuidebookPageController))]
public class GuidebookTabController : MonoBehaviour
{
    private const int TasksTabIndex = 1;

    [Tooltip("In-scene content objects, one per tab in tab order. "
           + "[0] How to Play  [1] Tasks")]
    [SerializeField] private GuidebookPageContents[] _pageContents;

    private GuidebookPageController _pageController;

    /// <summary>Index of the currently visible tab.</summary>
    public int ActiveTabIndex { get; private set; } = -1;

    /// <summary>
    /// Fired when the player navigates to the Tasks tab.
    /// GuidebookIcon subscribes to this to clear the notification badge.
    /// </summary>
    public static event Action OnTasksTabViewed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _pageController = GetComponent<GuidebookPageController>();
    }

    private void OnEnable()
    {
        _pageController.OnPageChanged += HandlePageChanged;
        // Snap pages to tab 0 and refresh content without animation on open.
        ActiveTabIndex = -1;
        _pageController.SnapTo(0);
        RefreshTabContent(0);
    }

    private void OnDisable()
    {
        _pageController.OnPageChanged -= HandlePageChanged;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Jumps to <paramref name="tabIndex"/> instantly (no flip animation).
    /// Clamps to the valid range. Safe to call from any system.
    /// </summary>
    public void ShowTab(int tabIndex)
    {
        if (_pageContents == null || _pageContents.Length == 0) return;

        tabIndex = Mathf.Clamp(tabIndex, 0, _pageContents.Length - 1);
        _pageController.SnapTo(tabIndex);
        RefreshTabContent(tabIndex);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GuidebookPageController after each animated flip completes.
    /// Pages are already in position — only refresh the visible tab content.
    /// </summary>
    private void HandlePageChanged(int leftCount)
    {
        int tabIndex = Mathf.Clamp(leftCount, 0, _pageContents != null ? _pageContents.Length - 1 : 0);
        RefreshTabContent(tabIndex);
    }

    /// <summary>Updates ActiveTabIndex and calls Refresh on the newly visible content object.</summary>
    private void RefreshTabContent(int tabIndex)
    {
        if (_pageContents == null || _pageContents.Length == 0) return;
        if (tabIndex == ActiveTabIndex) return;

        ActiveTabIndex = tabIndex;

        if (_pageContents[tabIndex] != null)
            _pageContents[tabIndex].Refresh();

        if (tabIndex == TasksTabIndex)
            OnTasksTabViewed?.Invoke();
    }
}
