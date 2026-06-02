using UnityEngine;

/// <summary>
/// Controls the visibility of the guidebook page-flip tooltips.
/// The controller itself stays always active so its static-event subscriptions
/// are never dropped. Only the <see cref="_tooltipContainer"/> child is toggled.
/// </summary>
public class GuidebookTooltipController : MonoBehaviour
{
    [Tooltip("Root container that holds both tooltip panels — toggled on guidebook open/close.")]
    [SerializeField] private GameObject _tooltipContainer;

    [Tooltip("Tooltip shown when flipping to the next page is available (E / RB).")]
    [SerializeField] private GameObject _nextPageTooltip;

    [Tooltip("Tooltip shown when flipping to the previous page is available (Q / LB).")]
    [SerializeField] private GameObject _prevPageTooltip;

    private GuidebookPageController _pageController;

    private void Awake()
    {
        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(false);
    }

    private void OnEnable()
    {
        GuidebookController.OnGuidebookOpened += HandleGuidebookOpened;
        GuidebookController.OnGuidebookClosed += HandleGuidebookClosed;
    }

    private void OnDisable()
    {
        GuidebookController.OnGuidebookOpened -= HandleGuidebookOpened;
        GuidebookController.OnGuidebookClosed -= HandleGuidebookClosed;
    }

    private void Update()
    {
        if (_tooltipContainer == null || !_tooltipContainer.activeSelf || _pageController == null) return;

        if (_nextPageTooltip != null)
            _nextPageTooltip.SetActive(_pageController.HasNextPage);

        if (_prevPageTooltip != null)
            _prevPageTooltip.SetActive(_pageController.HasPreviousPage);
    }

    private void HandleGuidebookOpened()
    {
        if (_pageController == null)
            _pageController = FindFirstObjectByType<GuidebookPageController>(FindObjectsInactive.Include);

        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(true);
    }

    private void HandleGuidebookClosed()
    {
        if (_tooltipContainer != null)
            _tooltipContainer.SetActive(false);
    }
}
