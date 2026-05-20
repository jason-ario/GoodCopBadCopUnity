using UnityEngine;

/// <summary>
/// Manages page-turning on the Guidebook prefab.
/// Attach to the root Guidebook GameObject alongside its Animator.
///
/// Next page  : sets the current page's "IsLeft" to true  (flips it left, reveals next page).
/// Prev page  : sets the previous page's "IsLeft" to false (flips it back right).
///
/// Keyboard/Mouse : E = next, Q = previous.
/// Controller     : RB (JoystickButton5) = next, LB (JoystickButton4) = previous.
/// </summary>
public class GuidebookPageController : MonoBehaviour
{
    private static readonly string IsLeftParam   = "IsLeft";
    private static readonly KeyCode NextKey      = KeyCode.E;
    private static readonly KeyCode PrevKey      = KeyCode.Q;
    private static readonly KeyCode NextPad      = KeyCode.JoystickButton5; // RB
    private static readonly KeyCode PrevPad      = KeyCode.JoystickButton4; // LB

    [Tooltip("Page child Animators in reading order. Each entry is one physical page in the prefab.")]
    [SerializeField] private Animator[] _pages;

    /// <summary>Index of the current (rightmost visible) page spread. Starts at 0.</summary>
    private int _currentPageIndex;

    private void OnEnable()
    {
        ResetPages();
    }

    private void Update()
    {
        if (Input.GetKeyDown(NextKey) || Input.GetKeyDown(NextPad))
            TurnNext();
        else if (Input.GetKeyDown(PrevKey) || Input.GetKeyDown(PrevPad))
            TurnPrevious();
    }

    /// <summary>
    /// Flips the current page to the left, advancing to the next spread.
    /// </summary>
    public void TurnNext()
    {
        if (_pages == null || _currentPageIndex >= _pages.Length) return;

        _pages[_currentPageIndex].SetBool(IsLeftParam, true);
        _currentPageIndex++;
    }

    /// <summary>
    /// Flips the previous page back to the right, returning to the prior spread.
    /// </summary>
    public void TurnPrevious()
    {
        if (_pages == null || _currentPageIndex <= 0) return;

        _currentPageIndex--;
        _pages[_currentPageIndex].SetBool(IsLeftParam, false);
    }

    /// <summary>
    /// Resets all pages to their default right position and returns to the first spread.
    /// Called automatically when the guidebook is opened (OnEnable).
    /// </summary>
    public void ResetPages()
    {
        _currentPageIndex = 0;

        if (_pages == null) return;

        foreach (Animator page in _pages)
        {
            if (page != null)
                page.SetBool(IsLeftParam, false);
        }
    }
}
