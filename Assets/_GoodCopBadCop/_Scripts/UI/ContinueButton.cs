using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the interactable state of the Continue button based on whether a save file exists.
/// Attach to the Continue button GameObject on the home screen.
/// </summary>
[RequireComponent(typeof(Button))]
public class ContinueButton : MonoBehaviour
{
    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void Start()
    {
        Refresh();
    }

    /// <summary>Updates interactable state to reflect current save data.</summary>
    public void Refresh()
    {
        bool hasSave = MainMenuController.Instance != null && MainMenuController.Instance.HasSaveFile;
        _button.interactable = hasSave;
    }
}
