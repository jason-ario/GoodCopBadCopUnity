using UnityEngine;

/// <summary>
/// Placed on a Guidebook Tab GameObject that marks the first page of a section.
/// Clicking navigates the guidebook to that section's first page via
/// <see cref="GuidebookPageController.SnapToPage"/>.
///
/// <see cref="_sectionPage"/> auto-resolves to the direct parent Transform when left null,
/// so no manual Inspector wiring is needed as long as the tab is a direct child of its page.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GuidebookSectionTab : MonoBehaviour, IClickable
{
    [Tooltip("The Transform of the first page in this section. " +
             "Leave null to auto-resolve to the direct parent Transform at Awake.")]
    [SerializeField] private Transform _sectionPage;

    private GuidebookPageController _controller;

    private void Awake()
    {
        _controller = GetComponentInParent<GuidebookPageController>();

        if (_sectionPage == null)
            _sectionPage = transform.parent;
    }

    public void OnClick()
    {
        if (_controller == null || _sectionPage == null) return;
        _controller.SnapToPage(_sectionPage);
    }
}
