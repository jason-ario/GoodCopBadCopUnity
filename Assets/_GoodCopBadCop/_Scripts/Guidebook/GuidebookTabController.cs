using System;
using UnityEngine;

/// <summary>
/// Wraps GuidebookPageController with named tab navigation.
/// Q / LB = previous tab.  E / RB = next tab.
/// Resets to tab 0 (How to Play) each time the guidebook opens (OnEnable).
/// </summary>
[RequireComponent(typeof(GuidebookPageController))]
public class GuidebookTabController : MonoBehaviour
{
    private static readonly KeyCode NextKey = KeyCode.E;
    private static readonly KeyCode PrevKey = KeyCode.Q;
    private static readonly KeyCode NextPad = KeyCode.JoystickButton5; // RB
    private static readonly KeyCode PrevPad = KeyCode.JoystickButton4; // LB

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

    private void Awake()
    {
        _pageController = GetComponent<GuidebookPageController>();
    }

    private void OnEnable()
    {
        ShowTab(0);
    }

    private void Update()
    {
        if (Input.GetKeyDown(NextKey) || Input.GetKeyDown(NextPad))
            ShowTab(ActiveTabIndex + 1);
        else if (Input.GetKeyDown(PrevKey) || Input.GetKeyDown(PrevPad))
            ShowTab(ActiveTabIndex - 1);
    }

    /// <summary>
    /// Navigates to the tab at tabIndex, clamped to the valid range.
    /// Drives the page-flip animator and calls Refresh on the new page's content object.
    /// Fires OnTasksTabViewed when the Tasks tab becomes active.
    /// </summary>
    public void ShowTab(int tabIndex)
    {
        if (_pageContents == null || _pageContents.Length == 0) return;

        tabIndex = Mathf.Clamp(tabIndex, 0, _pageContents.Length - 1);
        if (tabIndex == ActiveTabIndex) return;

        // Drive the physical page-flip animator to the correct spread.
        _pageController.ResetPages();
        for (int i = 0; i < tabIndex; i++)
            _pageController.TurnNext();

        ActiveTabIndex = tabIndex;

        if (_pageContents[tabIndex] != null)
            _pageContents[tabIndex].Refresh();

        if (tabIndex == TasksTabIndex)
            OnTasksTabViewed?.Invoke();
    }
}
