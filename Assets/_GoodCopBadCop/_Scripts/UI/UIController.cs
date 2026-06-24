using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Steamworks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public static UIController Instance;

    /// <summary>Fired when the End of Shift Report becomes visible.</summary>
    public Action OnReportShown { get; set; }

    /// <summary>Fired when the End of Shift Report is hidden.</summary>
    public Action OnReportHidden { get; set; }

    /// <summary>Fired on the local client whenever any player opens the tool shop.</summary>
    public static event Action OnToolShopOpened;

    /// <summary>Fired on the local client when the pause menu is about to open. Use this to close any overlapping UI before the pause menu appears.</summary>
    public static event Action OnPauseMenuOpened;

    [SerializeField] private RawImage cameraImage;
    [SerializeField] private GameObject levelSelectUI;
    [SerializeField] private GameObject playerUI;
    [SerializeField] private GameObject toolShopUI;
    [SerializeField] private GameObject hqOrderScreenUI;
    [SerializeField] private Animator screenFade;
    [SerializeField] private Animator newspaper;
    [SerializeField] private GameObject backButtonUI;
    [SerializeField] private Button backButton;
    [SerializeField] private ScreenDamage _screenDamage;
    [SerializeField] private EndOfShiftReportUI endOfShiftReportUI;
    [SerializeField] private GameObject startShiftScreen;
    [SerializeField] private GameObject guardPurchaseScreen;
    [SerializeField] private GuardPurchaseScreenUI guardPurchaseScreenUI;
    [SerializeField] private GameObject shopItemPurchasePopup;
    [SerializeField] private ShopItemPurchasePopupUI shopItemPurchasePopupUI;
    [SerializeField] private GameObject inviteFriendsPanel;
    [SerializeField] private CashNotificationPopupManager cashNotificationPopupManager;
    [SerializeField] private ShopNotificationManager shopNotificationManager;
    [SerializeField] private BoothWaitingNotification boothWaitingNotification;
    [SerializeField] private DeathScreenUI deathScreenUI;

    /// <summary>The <see cref="ScreenDamage"/> component driving the screen hurt overlay.</summary>
    public ScreenDamage ScreenDamage => _screenDamage;
    public bool IsPaused => pauseMenuOpened;

    [SerializeField] private AudioClip transitionToGameplayStinger;
    [SerializeField] private GameObject pauseMenu;
    private bool pauseMenuOpened = false;

    private Action _onGuardPurchaseConfirmed;
    
    bool showedCursorBeforePaused = false;
    bool couldControlBeforePaused = false;
    bool couldLookBeforePaused = false;
    bool showedReticleBeforePause = false;
    bool playerUIWasActiveBeforePaused = false;

    private void Awake()
    {
        Instance = this;
        backButtonUI.SetActive(false);
    }

    private void Update()
    {
        if (playerUI.activeSelf == false)
        {
            return;
        }
        
        if(PlayerInstance.Instance == null)
        {
            return;
        }

        // While a diegetic view is open, Q is its exit key — suppress the global
        // "Back" shortcut so it doesn't double-fire through the back button as well.
        if (backButtonUI.activeSelf == true && !DiegeticViewController.IsAnyViewActive)
        {
            if(Input.GetButtonDown("Back"))
            {
                backButton.onClick.Invoke();
            }
        }

        if (Input.GetButtonDown("Pause"))
        {
            if (pauseMenuOpened)
            {
                ClosePauseMenu();
            }
            else
            {
                OpenPauseMenu();
            }
        }
        
        if(PlayerInstance.Instance.CanControl == false) return;
    }
    
    public void OpenLevelSelectUI()
    {
        levelSelectUI.SetActive(true);
        playerUI.SetActive(false);
    }
    
    public void CloseLevelSelectUI()
    {
        levelSelectUI.SetActive(false);
        playerUI.SetActive(true);
    }
    
    private ToolsLocker _activeToolsLocker;

    public void OpenToolShop(Transform toolShopLookTarget, ToolsLocker locker)
    {
        _activeToolsLocker = locker;
        PlayerInstance.Instance.SetCanInteract(false);
        ShowCursor();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(toolShopLookTarget);
        OnToolShopOpened?.Invoke();
        StartCoroutine(WaitAndOpenShopUI());
    }

    IEnumerator WaitAndOpenShopUI()
    {
        yield return new WaitForSeconds(.5f);
        playerUI.SetActive(false);
        toolShopUI.SetActive(true);
    }
    
    public void CloseToolShopUI()
    {
        toolShopUI.SetActive(false);
        playerUI.SetActive(true);
        PlayerInstance.Instance.SetCanInteract(true);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);

        if (_activeToolsLocker != null)
        {
            _activeToolsLocker.NotifyPlayerClosedServerRpc();
            _activeToolsLocker = null;
        }
    }

    /// <summary>
    /// Opens the HQ Order Screen. Disables player movement and interaction, shows cursor.
    /// Call this when the player picks up the telephone.
    /// </summary>
    public void OpenHQOrderScreen()
    {
        PlayerInstance.Instance.OpenedUIPanel();
        ShowCursor();
        playerUI.SetActive(false);
        hqOrderScreenUI.SetActive(true);
    }

    /// <summary>
    /// Closes the HQ Order Screen and restores player movement and interaction.
    /// </summary>
    public void CloseHQOrderScreen()
    {
        hqOrderScreenUI.SetActive(false);
        playerUI.SetActive(true);
        HideCursor();
        PlayerInstance.Instance.ClosedUIPanel();
    }

    public void ClosePlayerUI()
    {
        playerUI.SetActive(false);
    }
    
    public void ShowPlayerUI()
    {
        playerUI.SetActive(true);
    }
    
    public void FadeIn()
    { 
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();

        foreach (var canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

        }
        
        screenFade.SetBool("Black", true);
    }
    
    public void FadeOut()
    {
        screenFade.SetBool("Black", false);
        
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();

        foreach (var canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = true;
        }
    }
    

    public void ShowBackButton(UnityAction onClickCallback)
    {
        backButton.onClick.AddListener(onClickCallback);
        backButtonUI.SetActive(true);
    }

    public void HideBackButton()
    {
        backButton.onClick.RemoveAllListeners();
        backButtonUI.SetActive(false);
    }


    public void ShowEndShiftReport(List<EndOfShiftReportUI.ReportRowData> reportRowDatas)
    {
        ShowCursor();
        endOfShiftReportUI.PlayReport(reportRowDatas);
        OnReportShown?.Invoke();
    }

    public void HideEndOfShiftReport()
    {
        endOfShiftReportUI.gameObject.SetActive(false);
        OnReportHidden?.Invoke();
    }

    public void OpenStartShiftScreen()
    {
        UIController.Instance.ShowCursor();
        startShiftScreen.SetActive(true);
        PlayerInstance.Instance.OpenedUIPanel();
    }
    
    public void CloseStartShiftScreen()
    {
        UIController.Instance.HideCursor();
        startShiftScreen.SetActive(false);
        PlayerInstance.Instance.ClosedUIPanel();
    }

    public void EnterFirstShift()
    {
        CloseStartShiftScreen();
        SFXController.Instance.Play(transitionToGameplayStinger);
        ShiftManager.Instance.InitiateIntroCutscene();
    }

    /// <summary>
    /// Opens the Guard Purchase Screen. Disables player movement and interaction until the screen is closed.
    /// </summary>
    /// <param name="price">The coupon price to display.</param>
    /// <param name="onConfirmed">Callback invoked when the player confirms the purchase.</param>
    public void OpenGuardPurchaseScreen(int price, Action onConfirmed)
    {
        _onGuardPurchaseConfirmed = onConfirmed;
        guardPurchaseScreenUI.SetPurchaseMode(price);
        ShowCursor();
        guardPurchaseScreen.SetActive(true);
        PlayerInstance.Instance.OpenedUIPanel();
    }

    /// <summary>Opens the Guard Purchase Screen in hired mode — shows confirmation message and Okay button only.</summary>
    public void OpenGuardPurchaseScreenHired()
    {
        guardPurchaseScreenUI.SetHiredMode();
        ShowCursor();
        guardPurchaseScreen.SetActive(true);
        PlayerInstance.Instance.OpenedUIPanel();
    }

    /// <summary>Closes the Guard Purchase Screen and restores player movement and interaction.</summary>
    public void CloseGuardPurchaseScreen()
    {
        HideCursor();
        guardPurchaseScreen.SetActive(false);
        PlayerInstance.Instance.ClosedUIPanel();
        _onGuardPurchaseConfirmed = null;
    }

    /// <summary>Confirms the guard purchase. Invokes the stored callback then closes the screen.</summary>
    public void ConfirmGuardPurchase()
    {
        _onGuardPurchaseConfirmed?.Invoke();
        CloseGuardPurchaseScreen();
    }

    /// <summary>
    /// Opens the Shop Item Purchase Popup over the diegetic locker view.
    /// Shows "Buy [itemName]", the coupon price, a live 3-D preview, and a Buy button.
    /// </summary>
    /// <param name="item">The shop item to display and purchase.</param>
    /// <param name="onBuy">Callback invoked when the player confirms the purchase.</param>
    /// <param name="onCancel">Callback invoked when the player presses the No button.</param>
    public void OpenShopItemPurchasePopup(ShopItem item, Action onBuy, Action onCancel)
    {
        shopItemPurchasePopupUI.Setup(item, onBuy, onCancel);
        shopItemPurchasePopup.SetActive(true);
    }

    /// <summary>Closes the Shop Item Purchase Popup.</summary>
    public void CloseShopItemPurchasePopup()
    {
        if (shopItemPurchasePopup != null)
            shopItemPurchasePopup.SetActive(false);
    }

    public void OpenInvitePanel()
    {
        PlayerInstance.Instance.OpenedUIPanel();
        LobbyManager.Instance.OpenInviteFriendsPopup();
        inviteFriendsPanel.SetActive(true);
    }

    public void CloseInviteFriendsScreen()
    {
        PlayerInstance.Instance.ClosedUIPanel();
        inviteFriendsPanel.SetActive(false);
    }

    public void ShowCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.Confined;
    }

    public void HideCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public RawImage GetCameraImage()
    {
        return cameraImage;
    }

    public void OpenPauseMenu()
    {
        pauseMenuOpened = true;
        couldControlBeforePaused = PlayerInstance.Instance.CanControl;
        couldLookBeforePaused = PlayerInstance.Instance.GetComponent<PlayerMovementController>().CanLook;
        showedCursorBeforePaused = Cursor.visible;

        // Capture reticle state before we disable control (which will deactivate it).
        // Use couldControlBeforePaused && couldLookBeforePaused as the source of truth,
        // since the reticleActive flag can be stale when the CanControl setter skips
        // SetReticleActive due to CanLook being false.
        showedReticleBeforePause = couldControlBeforePaused && couldLookBeforePaused;

        playerUIWasActiveBeforePaused = playerUI.activeSelf;

        // Give subscribers (e.g. diegetic views with open popups) a chance to clean up
        // their overlapping UI before the pause menu becomes visible.
        OnPauseMenuOpened?.Invoke();

        playerUI.SetActive(false);
        
        ShowCursor();
        
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(false);
        PlayerInstance.Instance.CanControl = false;
        pauseMenu.SetActive(true);
    }
    
    public void ClosePauseMenu()
    {
        // Restore CanLook first so the CanControl setter can properly re-enable the reticle.
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(couldLookBeforePaused);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(couldControlBeforePaused);
        
        if (showedCursorBeforePaused == false)
        {
            HideCursor();
        }
        
        PlayerInstance.Instance.PlayerInteractionController.SetReticleActive(showedReticleBeforePause);

        playerUI.SetActive(playerUIWasActiveBeforePaused);

        pauseMenuOpened = false;
        pauseMenu.SetActive(false);
    }

    public void ShowCashPopUpNotification(int amount, string message)
    {
        cashNotificationPopupManager.SpawnCashNotification(amount, message);
    }

    /// <summary>Displays a transient shop alert notification (purchase confirmed, error, etc.).</summary>
    public void ShowShopNotification(string message)
    {
        shopNotificationManager.ShowNotification(message);
    }

    /// <summary>
    /// Shows the "someone is waiting at the booth" notification in the bottom-centre of the screen.
    /// Call this only on the local client and only when the player is away from the booth.
    /// </summary>
    public void ShowBoothWaitingNotification()
    {
        if (boothWaitingNotification != null)
            boothWaitingNotification.Show();
    }

    /// <summary>Hides the booth waiting notification.</summary>
    public void HideBoothWaitingNotification()
    {
        if (boothWaitingNotification != null)
            boothWaitingNotification.Hide();
    }

    /// <summary>Shows the death screen after the given delay in seconds.</summary>
    public void ShowDeathScreen(float delay)
    {
        if (deathScreenUI == null) return;
        StartCoroutine(ShowDeathScreenDelayed(delay));
    }

    private IEnumerator ShowDeathScreenDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        deathScreenUI.gameObject.SetActive(true);
    }

    /// <summary>Hides the death screen immediately.</summary>
    public void HideDeathScreen()
    {
        if (deathScreenUI == null) return;
        StopCoroutine(nameof(ShowDeathScreenDelayed));
        deathScreenUI.gameObject.SetActive(false);
    }
}
