using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject settingsMenu;
    [SerializeField] private GameObject areYouSureQuitMenu;
    [SerializeField] private GameObject areYouSureMainMenu;
    [SerializeField] private TextButton[] _textButtons;
    [SerializeField] private RectTransform rootRectTransform;
    
    public void ResumeGame()
    {
        UIController.Instance.ClosePauseMenu();
    }

    private void OnEnable()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRectTransform);
        BackToMainMenu();
    }

    private void OnDisable()
    {
        BackToMainMenu();
    }

    public void ShowAreYouSureMainMenu()
    {
        mainMenu.SetActive(true);
        settingsMenu.SetActive(false);
        areYouSureQuitMenu.SetActive(false);
        areYouSureMainMenu.SetActive(false);
    }
    
    public void ShowSettingsMenu()
    {
        mainMenu.SetActive(false);
        settingsMenu.SetActive(true);
    }
    
    public void ShowAreYouSureQuitMenu()
    {
        mainMenu.SetActive(false);
        areYouSureQuitMenu.SetActive(true);
    }
    
    public void BackToMainMenu()
    {
        mainMenu.SetActive(true);
        settingsMenu.SetActive(false);
        areYouSureQuitMenu.SetActive(false);
        areYouSureMainMenu.SetActive(false);
    }
}
