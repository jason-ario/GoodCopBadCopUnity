using GoodCopBadCop.UI.SettingsMenu;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private GameObject menuBackground;
    [SerializeField] private GameObject menuHeader;

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

    private void SetPauseMenuShellVisible(bool visible)
    {
        if (menuBackground != null) menuBackground.SetActive(visible);
        if (menuHeader != null) menuHeader.SetActive(visible);
    }

    public void ShowAreYouSureMainMenu()
    {
        ShowMainMenu();
    }

    /// <summary>Backward-compatible UnityEvent target for the pause menu Main Menu button.</summary>
    public void ShowMainMenu()
    {
        mainMenu.SetActive(false);
        SetPauseMenuShellVisible(false);
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
        SetPauseMenuShellVisible(false);
        confirmationDialog.Hide();
        isSettingsOpen = true;
        settingsMenuView.SetVisible(true);
    }

    public void ShowAreYouSureQuitMenu()
    {
        mainMenu.SetActive(false);
        SetPauseMenuShellVisible(false);
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
        SetPauseMenuShellVisible(true);
        _returnedToMainListFrame = Time.frameCount;
    }

    // Frame the pause button list was (re)shown. The B press that closed Settings or a dialog in
    // that frame must not also resume the game.
    private int _returnedToMainListFrame = -1;

    /// <summary>Gamepad B on the pause button list resumes, mirroring Start. Settings and the
    /// confirmation dialog handle B themselves while they're open.</summary>
    private void Update()
    {
        if (!(Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)) return;
        if (Time.frameCount == _returnedToMainListFrame) return;
        if (isSettingsOpen || mainMenu == null || !mainMenu.activeInHierarchy) return;
        if (ConfirmationDialogController.IsAnyOpen || ConfirmationDialogController.CancelHandledThisFrame) return;
        if (UIController.Instance == null || !UIController.Instance.IsPaused) return;

        ResumeGame();
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

    private async void ReturnToMainMenu()
    {
        // Progress is only persisted at the start of each day. Drop anything done since then so
        // the menu (and a later Continue) reflects the day-start checkpoint on disk.
        SaveDataManager.Instance?.StopPlaytimeTracking();
        SaveDataManager.Instance?.RevertToLastCheckpoint();

        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.ExitLobbyAsync();

        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    private async void QuitGame()
    {
        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.ExitLobbyAsync();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
