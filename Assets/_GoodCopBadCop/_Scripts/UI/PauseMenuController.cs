using GoodCopBadCop.UI.SettingsMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using R3;
using VContainer;

public class PauseMenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private ConfirmationDialogController confirmationDialog;
    [SerializeField] private TextButton[] _textButtons;
    [SerializeField] private RectTransform rootRectTransform;
    [SerializeField] private TextMeshProUGUI lobbyCodeText;

    private const string LobbyCodePrefix = "LOBBY CODE: ";
    private const string ReturnToMainMenuTitle = "Return to main menu?";
    private const string ReturnToMainMenuBody = "Current shift progress may be lost.";
    private const string QuitGameTitle = "Quit game?";
    private const string QuitGameBody = "Any unsaved progress will be lost.";
    private const string ConfirmText = "Yes";
    private const string CancelText = "No";

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
        HideTransientPanels();
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
        ShowMainMenu();
    }

    /// <summary>Backward-compatible UnityEvent target for the pause menu Main Menu button.</summary>
    public void ShowMainMenu()
    {
        mainMenu.SetActive(false);
        settingsMenuView.SetVisible(false);
        isSettingsOpen = false;

        confirmationDialog.Show(
            ReturnToMainMenuTitle,
            ReturnToMainMenuBody,
            ConfirmText,
            CancelText,
            ReturnToMainMenu,
            BackToMainMenu);
    }

    public void ShowSettingsMenu()
    {
        mainMenu.SetActive(false);
        confirmationDialog.Hide();
        isSettingsOpen = true;
        settingsMenuView.SetVisible(true);
    }

    public void ShowAreYouSureQuitMenu()
    {
        mainMenu.SetActive(false);
        settingsMenuView.SetVisible(false);
        isSettingsOpen = false;

        confirmationDialog.Show(
            QuitGameTitle,
            QuitGameBody,
            ConfirmText,
            CancelText,
            QuitGame,
            BackToMainMenu);
    }

    public void BackToMainMenu()
    {
        HideTransientPanels();
        mainMenu.SetActive(true);
    }

    private void CloseSettingsMenu()
    {
        if (!isSettingsOpen)
        {
            return;
        }

        BackToMainMenu();
    }

    private void HideTransientPanels()
    {
        isSettingsOpen = false;
        settingsMenuView?.SetVisible(false);
        confirmationDialog?.Hide();
    }

    private void ReturnToMainMenu()
    {
        LobbyManager.Instance?.ExitLobby();
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    private void QuitGame()
    {
        LobbyManager.Instance?.ExitLobby();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
