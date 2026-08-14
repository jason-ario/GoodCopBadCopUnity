using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Steamworks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
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
    [SerializeField] private BoothWaitingNotification mailDeliveryNotification;
    [SerializeField] private BoothWaitingNotification radiationAlertNotification;
    [SerializeField] private DeathScreenUI deathScreenUI;
    [SerializeField] private GameObject _endDayPopup;
    [SerializeField] private EndDayPopupUI _endDayPopupUI;
    [SerializeField] private GameObject _thanksForPlayingPanel;
    [SerializeField] private ThanksForPlayingUI _thanksForPlayingUI;

    /// <summary>The <see cref="ScreenDamage"/> component driving the screen hurt overlay.</summary>
    public ScreenDamage ScreenDamage => _screenDamage;
    public bool IsPaused => pauseMenuOpened;

    [SerializeField] private AudioClip transitionToGameplayStinger;
    [SerializeField] private GameObject pauseMenu;
    private bool pauseMenuOpened = false;

    [Header("Transition Effect")]
    [Tooltip("Tentacle blackout controller for the menu → gameplay transition. " +
             "Falls back to the screenFade Animator when null.")]
    [SerializeField] private TentacleBlackoutController _tentacleBlackout;

    [Tooltip("Duration in seconds for the fade-to-black animation. " +
             "Should complete before the 2-second wait used by transition coroutines.")]
    [SerializeField] private float _fadeInDuration = 3.0f;

    [Tooltip("Duration in seconds for the reveal-from-black animation.")]
    [SerializeField] private float _fadeOutDuration = 0.8f;

    /// <summary>How long the fade-to-black animation takes in seconds.</summary>
    public float FadeInDuration => _fadeInDuration;

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
        if (PlayerInstance.Instance == null)
        {
            return;
        }

        // Back button input (Escape key / gamepad East) is handled directly by the
        // KeyBackButtonActivator / GamepadBackButtonActivator components on the back
        // button itself, so no manual polling is needed here.

        if (playerUI.activeSelf == false)
        {
            return;
        }

        // Failsafe: the invite panel is normally dismissed by the Steam overlay's
        // OnGameOverlayActivated callback, but that callback never fires if the
        // overlay isn't available (Editor Play Mode, overlay disabled, etc.). Let
        // the player back out manually so they're never stuck on a dark screen.
        if (inviteFriendsPanel.activeSelf)
        {
            bool cancelInput = Input.GetButtonDown("Cancel")
                               || Input.GetKeyDown(KeyCode.Escape)
                               || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
            if (cancelInput)
            {
                CloseInviteFriendsScreen();
            }
        }

        // Escape is shared between "Pause" and any currently-shown Back button (the Back
        // button's own KeyBackButtonActivator consumes Escape directly). If a Back button
        // is active and interactable right now, let it own Escape instead of also pausing.
        bool escapePausePressed = Input.GetButtonDown("Pause")
                                   && !KeyBackButtonActivator.AnyEscapeBackButtonInteractable;
        if (KeyBackButtonActivator.EscapeBackButtonPressedThisFrame)
            escapePausePressed = false;


        bool pauseInput = escapePausePressed
                          || (Gamepad.current?.startButton.wasPressedThisFrame ?? false);
        if (pauseInput)
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
    
    private void LateUpdate()
    {
        KeyBackButtonActivator.ClearEscapeBackButtonPressedThisFrame();
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
        // Guard: never reveal the HUD while a scripted cutscene or dialogue session is still
        // active. Mirrors the guard in PlayerMovementController.CanControl — delayed coroutines
        // (interaction close, panel dismissal, etc.) can call ShowPlayerUI() after scripted mode
        // has already locked the player, which would otherwise pop the HUD back up mid-cutscene
        // (e.g. during the Alexei cutscene) well before movement is actually restored.
        // The scripted/dialogue exit paths always clear these flags before calling ShowPlayerUI(),
        // so this never blocks the legitimate restore.
        if (ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode)
            return;

        playerUI.SetActive(true);
    }
    
    public void FadeIn(Action onComplete = null)
    {
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();
        foreach (CanvasGroup canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (_tentacleBlackout != null)
            _tentacleBlackout.FadeToBlack(_fadeInDuration, onComplete);
        else
        {
            screenFade.SetBool("Black", true);
            // Animator-based fade has no callback — caller should use WaitForSeconds.
        }
    }

    /// <summary>
    /// Starts the fade-to-black and yields until the screen is fully dark.
    /// Safe to call even if a fade is already in progress or complete:
    /// — already black (progress ≥ 0.99): returns immediately.
    /// — already animating: waits for the in-progress animation to finish without restarting it.
    /// — idle at 0: starts the animation and waits for it.
    /// </summary>
    public IEnumerator FadeInAndWait()
    {
        if (_tentacleBlackout != null)
        {
            // Already fully dark — nothing to do
            if (_tentacleBlackout.CurrentProgress >= 0.99f)
                yield break;

            // A fade-to-black started by an earlier call is still running — wait for it
            // instead of restarting it from the beginning (which would cause a visible flash)
            if (_tentacleBlackout.IsPlaying)
            {
                yield return new WaitUntil(() => !_tentacleBlackout.IsPlaying);
                yield break;
            }
        }

        bool done = false;
        FadeIn(onComplete: () => done = true);

        if (_tentacleBlackout != null)
            yield return new WaitUntil(() => done);
        else
            yield return new WaitForSeconds(2f); // legacy animator fallback
    }

    public void FadeOut()
    {
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();
        foreach (CanvasGroup canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (_tentacleBlackout != null)
            _tentacleBlackout.FadeFromBlack(_fadeOutDuration);
        else
            screenFade.SetBool("Black", false);
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


    public void ShowEndShiftReport(
        List<EndOfShiftReportUI.ReportRowData> reportRowDatas,
        int residentsFullyMutatedOvernight = 0,
        int civiliansKilledOvernight = 0,
        int currentPopulation = 0)
    {
        if (PlayerInstance.Instance != null)
            PlayerInstance.Instance.CanControl = false;
        PlayerInstance.Instance?.PlayerInteractionController?.SetCanInteract(false, string.Empty);
        ShowCursor();
        endOfShiftReportUI.PlayReport(
            reportRowDatas, residentsFullyMutatedOvernight, civiliansKilledOvernight, currentPopulation);
        OnReportShown?.Invoke();
    }

    public void HideEndOfShiftReport()
    {
        endOfShiftReportUI.gameObject.SetActive(false);
        HideCursor();
        PlayerInstance.Instance?.PlayerInteractionController?.SetCanInteract(true, string.Empty);
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
    /// Shows "Buy [itemName]" (or <paramref name="titleOverride"/> when provided), the coupon price, and a Buy button.
    /// </summary>
    /// <param name="item">The shop item to display and purchase.</param>
    /// <param name="onBuy">Callback invoked when the player confirms the purchase.</param>
    /// <param name="onCancel">Callback invoked when the player presses the No button.</param>
    /// <param name="titleOverride">
    /// When non-null and non-empty, replaces the default "Buy {item.Name}" title.
    /// </param>
    public void OpenShopItemPurchasePopup(ShopItem item, Action onBuy, Action onCancel, string titleOverride = null)
    {
        shopItemPurchasePopupUI.Setup(item, onBuy, onCancel, titleOverride);
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
        LobbyManager.Instance?.CancelInviteOverlayTracking();
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
        bool dialogueModeActive = DialogueChoiceSystem.IsInDialogueMode;

        if (dialogueModeActive)
        {
            PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(false);
            PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
            ShowCursor();
            PlayerInstance.Instance.PlayerInteractionController.SetReticleActive(false);
        }
        else
        {
            // Restore CanLook first so the CanControl setter can properly re-enable the reticle.
            PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(couldLookBeforePaused);
            PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(couldControlBeforePaused);

            if (showedCursorBeforePaused == false)
            {
                HideCursor();
            }

            PlayerInstance.Instance.PlayerInteractionController.SetReticleActive(showedReticleBeforePause);
        }

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

    /// <summary>
    /// Shows the same bottom-centre reveal-and-fade notification style as the booth waiting
    /// alert, but with a caller-supplied message. Used by tasks (e.g. Sort Mail) that need an
    /// unobtrusive popup that can appear at any time, independent of the booth-waiting alert.
    /// If <paramref name="loop"/> is true, the notification keeps fading out and back in
    /// (rather than disappearing for good) until <see cref="HideMailDeliveryNotification"/> is
    /// called — e.g. for the "shipment is waiting at the gate" alert, which should keep
    /// resurfacing until a player actually opens the gate.
    /// </summary>
    public void ShowMailDeliveryNotification(string message, bool loop = false)
    {
        if (mailDeliveryNotification != null)
            mailDeliveryNotification.Show(message, loop);
    }

    /// <summary>Hides the mail delivery notification.</summary>
    public void HideMailDeliveryNotification()
    {
        if (mailDeliveryNotification != null)
            mailDeliveryNotification.Hide();
    }

    /// <summary>
    /// Shows the bottom-centre "Radiation high. Take pills to reduce." alert, using the same
    /// looping reveal-and-fade style as the "shipment is waiting at the gate" notification.
    /// Call this only on the local client once radiation crosses the high threshold; it keeps
    /// resurfacing until <see cref="HideRadiationAlert"/> is called (i.e. once radiation drops
    /// back below the threshold).
    /// </summary>
    public void ShowRadiationAlert(string message = "Radiation high. Take pills to reduce.")
    {
        if (radiationAlertNotification != null)
            radiationAlertNotification.Show(message, loop: true);
    }

    /// <summary>Hides the radiation alert notification.</summary>
    public void HideRadiationAlert()
    {
        if (radiationAlertNotification != null)
            radiationAlertNotification.Hide();
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

    /// <summary>
    /// Opens the End Day confirmation popup in the ready state.
    /// Shows "End the day?" with a Yes and No button.
    /// </summary>
    /// <param name="onConfirm">Callback invoked when the player confirms ending the day.</param>
    /// <param name="onCancel">Callback invoked when the player presses the No button.</param>
    public void OpenEndDayPopup(Action onConfirm, Action onCancel)
    {
        if (_endDayPopupUI != null)
            _endDayPopupUI.Setup(onConfirm, onCancel);

        if (_endDayPopup != null)
            _endDayPopup.SetActive(true);
    }

    /// <summary>
    /// Opens the End Day popup in the blocked state.
    /// Shows "Can't sleep yet" with no action buttons — the Back UI is the only exit.
    /// </summary>
    /// <param name="onCancel">Callback invoked when the player dismisses the popup.</param>
    public void OpenEndDayBlockedPopup(Action onCancel)
    {
        if (_endDayPopupUI != null)
            _endDayPopupUI.SetupBlocked(onCancel);

        if (_endDayPopup != null)
            _endDayPopup.SetActive(true);
    }

    /// <summary>Closes the End Day confirmation popup.</summary>
    public void CloseEndDayPopup()
    {
        if (_endDayPopup != null)
            _endDayPopup.SetActive(false);
    }

    // ─── Thanks For Playing ───────────────────────────────────────────────────

    /// <summary>
    /// Locks the local player's movement/interaction/look, freezes their animations, and marks
    /// them invincible so no stray hit/damage animation can play. Idempotent — safe to call
    /// repeatedly.
    /// </summary>
    private void LockPlayerForEndOfDemo()
    {
        if (PlayerInstance.Instance == null)
            return;

        PlayerInstance.Instance.CanControl = false;
        PlayerInstance.Instance.PlayerInteractionController?.SetCanInteract(false, string.Empty);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>()?.SetCanLook(false);
        PlayerInstance.Instance.GetComponent<PlayerAnimationController>()?.SetAnimatorsEnabled(false);

        PlayerHealth playerHealth = PlayerInstance.Instance.GetComponent<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.IsInvincible = true;

        PlayerRadiation playerRadiation = PlayerInstance.Instance.GetComponent<PlayerRadiation>();
        if (playerRadiation != null)
            playerRadiation.IsInvincible = true;
    }

    /// <summary>
    /// Waits <paramref name="delaySeconds"/> — with the player still fully in control — before
    /// locking them and revealing the Thanks For Playing panel. Used when a mutant breach ends
    /// the demo, so the moment lands after a beat instead of popping up (and freezing the
    /// player) the instant the last mutant is resolved.
    /// </summary>
    public void ShowThanksForPlayingScreenAfterDelay(float delaySeconds = 5f)
    {
        StartCoroutine(ShowThanksForPlayingScreenAfterDelayRoutine(delaySeconds));
    }

    private IEnumerator ShowThanksForPlayingScreenAfterDelayRoutine(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        ShowThanksForPlayingScreen();
    }

    /// <summary>
    /// Shows the "Thanks for Playing the Demo" end screen.
    /// Locks player movement, interaction, look, and hurt (invincible), shows the cursor, and
    /// swaps the audio over to main menu music with the ambience faded out.
    /// Called by <see cref="ShiftManager"/> after the final day's shift sequence completes, and
    /// (after a delay) by <see cref="MutantBreachManager"/> when a finale breach ends the demo.
    /// </summary>
    public void ShowThanksForPlayingScreen()
    {
        LockPlayerForEndOfDemo();

        ShowCursor();
        playerUI.SetActive(false);

        if (_thanksForPlayingPanel != null)
            _thanksForPlayingPanel.SetActive(true);

        if (MainMenuController.Instance != null)
            MainMenuController.Instance.PlayMainMenuMusic();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.FadeOutAmbientAudio();
            AudioManager.Instance.SetRainAmbience(false);
        }
    }

    /// <summary>Hides the thanks-for-playing screen.</summary>
    public void HideThanksForPlayingScreen()
    {
        if (_thanksForPlayingPanel != null)
            _thanksForPlayingPanel.SetActive(false);
    }
}
