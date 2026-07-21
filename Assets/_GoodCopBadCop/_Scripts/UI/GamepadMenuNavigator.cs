using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Selects the first interactable button in <see cref="_buttons"/> whenever the menu panel
/// becomes active. From that point, the InputSystemUIInputModule drives D-pad / left-stick
/// navigation, Submit (A), and Cancel (B) automatically through Unity's EventSystem.
///
/// Attach to any menu root that contains a list of buttons to navigate via controller.
/// Assign the ordered list of Buttons in the Inspector to define navigation order.
/// </summary>
public class GamepadMenuNavigator : MonoBehaviour
{
    [Tooltip("Ordered list of buttons to navigate. Only active, interactable entries are selectable.")]
    [SerializeField] private List<Button> _buttons;

    private void OnEnable()
    {
        // Defer by one frame so the panel's layout and interactable states are settled
        // before we attempt to set the selection.
        StartCoroutine(SelectFirstNextFrame());
    }

    private void OnDisable()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private System.Collections.IEnumerator SelectFirstNextFrame()
    {
        yield return null;
        SelectFirst();
    }

    /// <summary>Sets the EventSystem selection to the first active, interactable button.</summary>
    public void SelectFirst()
    {
        if (_buttons == null || EventSystem.current == null) return;

        foreach (Button btn in _buttons)
        {
            if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
            {
                EventSystem.current.SetSelectedGameObject(btn.gameObject);
                return;
            }
        }
    }

    /// <summary>
    /// Refreshes the selection after the button list changes at runtime
    /// (e.g. the Continue button becomes visible after a save file is found).
    /// </summary>
    public void RefreshSelection()
    {
        SelectFirst();
    }
}
