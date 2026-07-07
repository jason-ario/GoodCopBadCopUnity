using GoodCopBadCop.UI.SettingsMenu;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using R3;
using VContainer;

public class PauseMenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject areYouSureQuitMenu;
    [SerializeField] private GameObject areYouSureMainMenu;
    [SerializeField] private TextButton[] _textButtons;
    [SerializeField] private RectTransform rootRectTransform;
    [SerializeField] private TextMeshProUGUI lobbyCodeText;

    private const string LobbyCodePrefix = "LOBBY CODE: ";

    private ISettingsMenuView settingsMenuView;
    private DisposableBag settingsDisposables;
    private bool isSettingsOpen;

    [Inject]
    public void Construct(ISettingsMenuView settingsMenuView)
    {
        this.settingsMenuView = settingsMenuView;
        settingsMenuView.BackRequested.Subscribe(_ => CloseSettingsMenu()).AddTo(ref settingsDisposables);
    }

    public void ResumeGame()
    {
        UIController.Instance.ClosePauseMenu();
    }

    private void OnDestroy()
    {
        settingsDisposables.Dispose();
    }

    private void OnEnable()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRectTransform);
        BackToMainMenu();
        RefreshLobbyCode();
    }

    private void OnDisable()
    {
        BackToMainMenu();
    }

    /// <summary>Updates the lobby code label. Shows the encoded code when in an active lobby, hides it otherwise.</summary>
    private void RefreshLobbyCode()
    {
        if (lobbyCodeText == null) return;

        bool hasLobby = LobbyManager.Instance != null && LobbyManager.Instance.CurrentLobby.Id != 0;
        lobbyCodeText.gameObject.SetActive(hasLobby);

        if (hasLobby)
        {
            string joinCode = LobbyManager.Instance.CurrentJoinCode;
            lobbyCodeText.text = LobbyCodePrefix + joinCode;
        }
    }

    public void ShowAreYouSureMainMenu()
    {
        mainMenu.SetActive(true);
        areYouSureQuitMenu.SetActive(false);
        areYouSureMainMenu.SetActive(false);
    }
    
    public void ShowSettingsMenu()
    {
        mainMenu.SetActive(false);
        areYouSureQuitMenu.SetActive(false);
        areYouSureMainMenu.SetActive(false);
        isSettingsOpen = true;
        settingsMenuView?.SetVisible(true);
    }
    
    public void ShowAreYouSureQuitMenu()
    {
        mainMenu.SetActive(false);
        areYouSureQuitMenu.SetActive(true);
    }
    
    public void BackToMainMenu()
    {
        isSettingsOpen = false;
        settingsMenuView?.SetVisible(false);
        mainMenu.SetActive(true);
        areYouSureQuitMenu.SetActive(false);
        areYouSureMainMenu.SetActive(false);
    }

    private void CloseSettingsMenu()
    {
        if (!isSettingsOpen)
        {
            return;
        }

        BackToMainMenu();
    }
}
