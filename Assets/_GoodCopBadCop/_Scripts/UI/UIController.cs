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
    [Tooltip("The single bottom-centre notification slot. Booth, shipment, radiation, low-health and one-shot alerts all queue here so they never overlap.")]
    [FormerlySerializedAs("boothWaitingNotification")]
    [SerializeField] private HUDNotificationQueue notificationQueue;
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

    // True while OpenPauseMenu/ClosePauseMenu themselves are writing control/look, so those
    // writes go to the live player instead of being captured into the pause snapshot.
    private bool _applyingPauseState;

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

        // Escape is shared between "Pause" and any currently-shown Back button (the Back
        // button's own KeyBackButtonActivator consumes Escape directly). If a Back button
        // is active and interactable right now, let it own Escape instead of also pausing.
        // Evaluated unconditionally (i.e. even while playerUI/HUD happens to be hidden, e.g.
        // right after a revive) so the player is never stuck unable to pause. Closing the
        // pause menu when it's already open must also stay available regardless of HUD state.
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

        if(PlayerInstance.Instance.CanControl == false) return;
    }
    
    private void LateUpdate()
    {
        KeyBackButtonActivator.ClearEscapeBackButtonPressedThisFrame();
        UpdateBackButtonHudAvoidance();
    }

    // ── Back button / HUD avoidance ──────────────────────────────────────────
    // The Back button and the Geiger counter share the bottom-left corner. While both are
    // visible, lift the Back button so it sits above the Geiger counter instead of overlapping it.

    [Header("Back Button HUD Avoidance")]
    [Tooltip("Vertical gap (canvas units) kept between the top of the Geiger counter and the Back button.")]
    [SerializeField] private float backButtonHudSpacing = 12f;

    private RectTransform _backButtonRect;
    private Vector2 _backButtonDefaultPosition;
    private bool _backButtonLayoutDirty = true;
    private bool _backButtonLiftedForGeiger;
    private Vector2Int _backButtonLayoutScreenSize;
    private readonly Vector3[] _geigerCorners = new Vector3[4];

    private void CacheBackButtonRect()
    {
        if (_backButtonRect != null || backButtonUI == null) return;

        // The Back button prefab is the (only) child of Back UI, which stretches full-screen.
        Transform root = backButtonUI.transform.childCount > 0 ? backButtonUI.transform.GetChild(0) : null;
        _backButtonRect = root as RectTransform;
        if (_backButtonRect != null)
            _backButtonDefaultPosition = _backButtonRect.anchoredPosition;
    }

    private void UpdateBackButtonHudAvoidance()
    {
        if (backButtonUI == null || !backButtonUI.activeSelf) return;

        CacheBackButtonRect();
        if (_backButtonRect == null) return;

        RectTransform geiger = PlayerUI.Instance != null && PlayerUI.Instance.GeigerCounterUI != null
            ? PlayerUI.Instance.GeigerCounterUI.transform as RectTransform
            : null;
        bool geigerVisible = geiger != null && geiger.gameObject.activeInHierarchy;

        // Only re-layout on state changes (not every frame) so the Geiger's high-radiation
        // UIWobble shake doesn't make the Back button jitter.
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (!_backButtonLayoutDirty
            && geigerVisible == _backButtonLiftedForGeiger
            && screenSize == _backButtonLayoutScreenSize)
            return;

        _backButtonLayoutDirty = false;
        _backButtonLiftedForGeiger = geigerVisible;
        _backButtonLayoutScreenSize = screenSize;

        _backButtonRect.anchoredPosition = _backButtonDefaultPosition;
        if (!geigerVisible) return;

        RectTransform parent = _backButtonRect.parent as RectTransform;
        if (parent == null) return;

        // Geiger bounds in the Back button's parent space.
        geiger.GetWorldCorners(_geigerCorners);
        float geigerMinX = float.MaxValue, geigerMaxX = float.MinValue, geigerTop = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = parent.InverseTransformPoint(_geigerCorners[i]);
            geigerMinX = Mathf.Min(geigerMinX, p.x);
            geigerMaxX = Mathf.Max(geigerMaxX, p.x);
            geigerTop = Mathf.Max(geigerTop, p.y);
        }

        // Back button bounds (at its default position) in the same space.
        Rect r = _backButtonRect.rect;
        Vector3 local = _backButtonRect.localPosition;
        Vector3 scale = _backButtonRect.localScale;
        float buttonMinX = local.x + r.xMin * scale.x;
        float buttonMaxX = local.x + r.xMax * scale.x;
        float buttonBottom = local.y + r.yMin * scale.y;

        bool overlapsHorizontally = buttonMaxX > geigerMinX && buttonMinX < geigerMaxX;
        float lift = geigerTop + backButtonHudSpacing - buttonBottom;
        if (overlapsHorizontally && lift > 0f)
            _backButtonRect.anchoredPosition = _backButtonDefaultPosition + new Vector2(0f, lift);
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

    /// <summary>
    /// Hides the HQ Order Screen without touching player control, HUD or cursor. Used when the
    /// handset is force-released from a holder who died/was replaced — the death flow owns
    /// player state then, and <see cref="CloseHQOrderScreen"/> would re-enable a dead player.
    /// </summary>
    public void HideHQOrderScreenOnly()
    {
        if (hqOrderScreenUI != null)
            hqOrderScreenUI.SetActive(false);
    }

    /// <summary>True while the player HUD is shown.</summary>
    public bool IsPlayerUIVisible => playerUI != null && playerUI.activeSelf;

    public void ClosePlayerUI()
    {
        // While paused, the HUD is already hidden by the pause menu; record the request so
        // ClosePauseMenu doesn't bring back a HUD that gameplay hid in the meantime.
        if (pauseMenuOpened)
            playerUIWasActiveBeforePaused = false;

        // Gameplay explicitly hid the HUD during the Day number pop-up — don't bring it back
        // when the pop-up ends.
        if (_dayNumberHudHidden)
            _restoreHudAfterDayNumber = false;

        playerUI.SetActive(false);
    }

    // ── Day number pop-up HUD suppression ────────────────────────────────────

    private bool _dayNumberHudHidden;
    private bool _restoreHudAfterDayNumber;

    /// <summary>True while the Day number pop-up is holding the HUD hidden.</summary>
    public bool IsDayNumberHudHidden => _dayNumberHudHidden;

    /// <summary>
    /// Hides the HUD for the Day number pop-up (<see cref="StartShiftScreen"/>). Any
    /// <see cref="ShowPlayerUI"/> call made while the pop-up plays is deferred until
    /// <see cref="EndDayNumberHudHide"/>, so the HUD reliably stays hidden for the whole reveal
    /// regardless of which day-start path triggered it. Idempotent.
    /// </summary>
    public void BeginDayNumberHudHide()
    {
        if (_dayNumberHudHidden) return;

        _restoreHudAfterDayNumber = pauseMenuOpened ? playerUIWasActiveBeforePaused : playerUI.activeSelf;
        _dayNumberHudHidden = true;

        if (pauseMenuOpened)
            playerUIWasActiveBeforePaused = false;
        playerUI.SetActive(false);
    }

    /// <summary>
    /// Ends the Day number HUD suppression and restores the HUD if it was visible before the
    /// pop-up, or if something requested it during the pop-up. The restore goes through
    /// <see cref="ShowPlayerUI"/> so its dialogue/diegetic/phone/pause guards still apply.
    /// </summary>
    public void EndDayNumberHudHide()
    {
        if (!_dayNumberHudHidden) return;

        _dayNumberHudHidden = false;
        bool restore = _restoreHudAfterDayNumber;
        _restoreHudAfterDayNumber = false;

        if (restore)
            ShowPlayerUI();
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

        // Guard: never reveal the HUD while any diegetic view (bunker door wheel, tool locker,
        // mini fridge, task page, electric panel, quarantine board, etc.) is open. Those views
        // call ClosePlayerUI() themselves on open and ShowPlayerUI() on close, but a completely
        // unrelated delayed coroutine can still call ShowPlayerUI() in between — e.g. the Day
        // Number pop-up (StartShiftScreen.ShowDayNumber) finishing its multi-second reveal after
        // the player has since opened the bunker door wheel — which would otherwise pop the HUD
        // back up over a screen that explicitly hides it. DiegeticViewController.Close() always
        // clears IsAnyViewActive before calling ShowPlayerUI(), so this never blocks the
        // legitimate restore once the view actually closes.
        if (DiegeticViewController.IsAnyViewActive)
            return;

        // Guard: a scripted phone call (e.g. Day 3's HQ power-outage call) keeps the HUD hidden
        // until the handset is put down. The scripted dialogue's own exit path calls
        // ShowPlayerUI() a frame before the auto hang-up, which would otherwise flash the HUD.
        if (IsPhoneCallBackdropVisible)
            return;

        // Guard: the HUD stays hidden for the whole spectate session (dead player or dev
        // spectator). Respawn/revive call SpectateManager.StopSpectating() before ShowPlayerUI().
        if (SpectateManager.Instance != null && SpectateManager.Instance.IsSpectating)
            return;

        // The Day number pop-up keeps the HUD hidden for its whole reveal. Day-start paths
        // (save resume, debug skips, OnDayStart listeners) call ShowPlayerUI() around the
        // fanfare; record the request and apply it when the pop-up ends.
        if (_dayNumberHudHidden)
        {
            _restoreHudAfterDayNumber = true;
            return;
        }

        // While paused, defer the reveal to ClosePauseMenu (e.g. the intro cutscene ending
        // while the settings menu is open) instead of popping the HUD over the pause menu.
        if (pauseMenuOpened)
        {
            playerUIWasActiveBeforePaused = true;
            return;
        }

        playerUI.SetActive(true);
    }

    // ── Scripted phone call backdrop ─────────────────────────────────────────

    [Header("Scripted Phone Call")]
    [Tooltip("Full-screen dim backdrop (same look as the HQ Order Screen BG) shown while the local " +
             "player holds the phone during a scripted call. Has no buttons or dial tone.")]
    [SerializeField] private GameObject phoneCallBackdropUI;

    /// <summary>True while the scripted phone call backdrop is shown.</summary>
    public bool IsPhoneCallBackdropVisible => phoneCallBackdropUI != null && phoneCallBackdropUI.activeSelf;

    /// <summary>
    /// Hides the HUD and shows the dim phone backdrop for a scripted call (HQ dialogue plays
    /// over it). Does not touch movement, cursor, or interaction — the telephone and the
    /// scripted dialogue own those.
    /// </summary>
    public void ShowPhoneCallBackdrop()
    {
        playerUI.SetActive(false);
        if (phoneCallBackdropUI != null)
            phoneCallBackdropUI.SetActive(true);
    }

    /// <summary>Hides the scripted call backdrop. The HUD is restored by the put-down flow.</summary>
    public void HidePhoneCallBackdrop()
    {
        if (phoneCallBackdropUI != null)
            phoneCallBackdropUI.SetActive(false);
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
    ///
    /// Every wait here is hard-capped by <see cref="FadeWaitTimeout"/>. An unbounded wait on a
    /// completion callback is a trap: if the blackout never reports done (callback dropped, its
    /// GameObject deactivated mid-animation), the *caller* hangs. For
    /// <see cref="ShiftManager.InBetweenShiftSequence"/> that means hanging before it tears down
    /// the end-of-shift report, leaving the player staring at a report they cannot dismiss.
    /// Timing out costs at worst a visible pop; hanging costs the player their session.
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
                yield return WaitUntilOrTimeout(() => !_tentacleBlackout.IsPlaying, FadeWaitTimeout,
                    "fade-to-black already in progress");
                yield break;
            }
        }

        bool done = false;
        FadeIn(onComplete: () => done = true);

        if (_tentacleBlackout != null)
            yield return WaitUntilOrTimeout(() => done, FadeWaitTimeout, "fade-to-black completion");
        else
            yield return new WaitForSeconds(2f); // legacy animator fallback
    }

    /// <summary>
    /// Generous upper bound for any fade wait — the configured duration plus slack, never under
    /// three seconds.
    /// </summary>
    private float FadeWaitTimeout => Mathf.Max(_fadeInDuration + 2f, 3f);

    /// <summary>
    /// Waits until <paramref name="predicate"/> is true or <paramref name="timeout"/> seconds have
    /// passed, whichever comes first. Uses unscaled time so a paused game (timeScale 0) still
    /// releases the wait. Logs a warning when it gives up so the underlying stall is traceable.
    /// </summary>
    private static IEnumerator WaitUntilOrTimeout(Func<bool> predicate, float timeout, string description)
    {
        float elapsed = 0f;
        while (!predicate())
        {
            if (elapsed >= timeout)
            {
                Debug.LogWarning(
                    $"[UIController] Timed out after {timeout:0.#}s waiting for {description}. " +
                    "Continuing anyway to avoid stalling the caller.");
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
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
    

    // Callbacks registered through ShowBackButton, so an exclusive owner can set them aside and
    // restore them later (UnityEvent runtime listeners can't be enumerated).
    private readonly List<UnityAction> _backButtonCallbacks = new List<UnityAction>();
    private readonly List<UnityAction> _suspendedBackButtonCallbacks = new List<UnityAction>();
    private bool _backButtonWasVisibleBeforeExclusive;

    public void ShowBackButton(UnityAction onClickCallback)
    {
        backButton.onClick.AddListener(onClickCallback);
        _backButtonCallbacks.Add(onClickCallback);
        backButtonUI.SetActive(true);
        _backButtonLayoutDirty = true;
        UpdateBackButtonHudAvoidance();
    }

    public void HideBackButton()
    {
        backButton.onClick.RemoveAllListeners();
        _backButtonCallbacks.Clear();
        // Whoever owned the button underneath an exclusive owner is done with it too.
        _suspendedBackButtonCallbacks.Clear();
        _backButtonWasVisibleBeforeExclusive = false;
        backButtonUI.SetActive(false);
    }

    /// <summary>
    /// Shows the Back button with <paramref name="onClickCallback"/> as its only action (Escape /
    /// click). Any callbacks already on the button (e.g. an open dialogue's "leave") are set aside
    /// and restored by <see cref="ReleaseExclusiveBackButton"/>.
    /// </summary>
    public void ShowExclusiveBackButton(UnityAction onClickCallback)
    {
        _backButtonWasVisibleBeforeExclusive = backButtonUI.activeSelf;
        foreach (UnityAction callback in _backButtonCallbacks)
        {
            backButton.onClick.RemoveListener(callback);
            _suspendedBackButtonCallbacks.Add(callback);
        }
        _backButtonCallbacks.Clear();
        ShowBackButton(onClickCallback);
    }

    /// <summary>
    /// Undoes <see cref="ShowExclusiveBackButton"/>: removes <paramref name="onClickCallback"/> and
    /// restores the callbacks it set aside. If <see cref="HideBackButton"/> ran in between, the
    /// previous owner is gone and the button simply stays hidden.
    /// </summary>
    public void ReleaseExclusiveBackButton(UnityAction onClickCallback)
    {
        if (!_backButtonCallbacks.Remove(onClickCallback))
        {
            // Another exclusive owner took over on top of this one: drop this callback from the
            // set-aside list so it isn't restored (stale) when that owner releases.
            _suspendedBackButtonCallbacks.Remove(onClickCallback);
            return;
        }
        backButton.onClick.RemoveListener(onClickCallback);

        foreach (UnityAction callback in _suspendedBackButtonCallbacks)
        {
            backButton.onClick.AddListener(callback);
            _backButtonCallbacks.Add(callback);
        }
        _suspendedBackButtonCallbacks.Clear();

        bool visible = _backButtonCallbacks.Count > 0 || _backButtonWasVisibleBeforeExclusive;
        _backButtonWasVisibleBeforeExclusive = false;
        backButtonUI.SetActive(visible);
        _backButtonLayoutDirty = true;
    }


    public void ShowEndShiftReport(ShiftReportData reportData)
    {
        if (PlayerInstance.Instance != null)
            PlayerInstance.Instance.CanControl = false;
        PlayerInstance.Instance?.PlayerInteractionController?.SetCanInteract(false, string.Empty);
        // Mutants ignore and cannot damage the player while the report is up (same as dialogue).
        PlayerInstance.Instance?.SetIsViewingShiftReport(true);
        ShowCursor();
        endOfShiftReportUI.PlayReport(reportData);
        OnReportShown?.Invoke();
    }

    public void HideEndOfShiftReport()
    {
        endOfShiftReportUI.gameObject.SetActive(false);
        PlayerInstance.Instance?.SetIsViewingShiftReport(false);
        HideCursor();
        PlayerInstance.Instance?.PlayerInteractionController?.SetCanInteract(true, string.Empty);
        OnReportHidden?.Invoke();
    }

    /// <summary>True while the end-of-shift report is on screen for the local player.</summary>
    public bool IsEndOfShiftReportVisible =>
        endOfShiftReportUI != null && endOfShiftReportUI.gameObject.activeInHierarchy;

    /// <summary>
    /// Last-resort teardown for the end-of-shift report, used when the normal transition failed to
    /// dismiss it (see the watchdogs in <see cref="EndOfShiftReportUI"/>).
    ///
    /// This differs from <see cref="HideEndOfShiftReport"/> in one important way: it also restores
    /// player control. The normal path deliberately leaves control disabled because it runs while
    /// the screen is black, mid-transition, and
    /// <see cref="ShiftManager.InBetweenShiftSequence"/> re-enables control at the end. When that
    /// coroutine never gets there, nothing restores control — so the failsafe must do it itself,
    /// otherwise dismissing the report just swaps one trap (a stuck screen) for another
    /// (a frozen player).
    /// </summary>
    public void ForceDismissEndOfShiftReport()
    {
        if (endOfShiftReportUI == null)
            return;

        Debug.LogWarning("[UIController] Force-dismissing the end-of-shift report and restoring control (failsafe).");

        HideEndOfShiftReport();

        if (PlayerInstance.Instance != null)
            PlayerInstance.Instance.CanControl = true;
    }

    public void OpenStartShiftScreen()
    {
        UIController.Instance.ShowCursor();
        startShiftScreen.SetActive(true);
        playerUI.SetActive(false);
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

        _applyingPauseState = true;
        try
        {
            PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(false);
            PlayerInstance.Instance.CanControl = false;
        }
        finally
        {
            _applyingPauseState = false;
        }
        pauseMenu.SetActive(true);
    }
    
    public void ClosePauseMenu()
    {
        bool dialogueModeActive = DialogueChoiceSystem.IsInDialogueMode;

        _applyingPauseState = true;
        try
        {
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
        }
        finally
        {
            _applyingPauseState = false;
        }

        // Don't pop the HUD back over a Day number pop-up that is still playing; hand the
        // pending reveal to EndDayNumberHudHide instead.
        if (_dayNumberHudHidden)
        {
            if (playerUIWasActiveBeforePaused)
                _restoreHudAfterDayNumber = true;
        }
        else
        {
            playerUI.SetActive(playerUIWasActiveBeforePaused);
        }

        pauseMenuOpened = false;
        pauseMenu.SetActive(false);
    }

    /// <summary>
    /// Called from <see cref="PlayerMovementController.CanControl"/>'s setter. While the pause menu
    /// is open, a control change from gameplay (e.g. <c>ShiftManager.EnablePlayerControl</c> when
    /// the intro cutscene ends with Settings open) is written into the pause snapshot instead of
    /// the live player. Otherwise <see cref="ClosePauseMenu"/> would restore the stale pre-pause
    /// value and leave the player locked. Mirrors the setter's cursor/reticle side effects.
    /// Returns true when the write was captured, in which case the caller must not apply it.
    /// </summary>
    internal bool TryCaptureControlWhilePaused(PlayerMovementController mover, bool value)
    {
        if (!ShouldCaptureWhilePaused(mover))
            return false;

        couldControlBeforePaused = value;
        if (!value)
        {
            showedCursorBeforePaused = true;
            showedReticleBeforePause = false;
        }
        else if (couldLookBeforePaused)
        {
            showedCursorBeforePaused = false;
            showedReticleBeforePause = true;
        }
        return true;
    }

    /// <summary>
    /// Look counterpart of <see cref="TryCaptureControlWhilePaused"/>, called from
    /// <see cref="PlayerMovementController.SetCanLook"/>. Returns true when the write was captured.
    /// </summary>
    internal bool TryCaptureLookWhilePaused(PlayerMovementController mover, bool value)
    {
        if (!ShouldCaptureWhilePaused(mover))
            return false;

        couldLookBeforePaused = value;
        return true;
    }

    private bool ShouldCaptureWhilePaused(PlayerMovementController mover)
    {
        return pauseMenuOpened
               && !_applyingPauseState
               && mover != null
               && PlayerInstance.Instance != null
               && mover.gameObject == PlayerInstance.Instance.gameObject;
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

    /// <summary>Displays a transient shop notification confirming a successful purchase, with a distinct purchase sound.</summary>
    public void ShowPurchaseNotification(string message)
    {
        shopNotificationManager.ShowPurchaseNotification(message);
    }

    /// <summary>Displays a transient notification for a rejected action (e.g. "Trash is full"), with a negative sound.</summary>
    public void ShowErrorNotification(string message)
    {
        shopNotificationManager.ShowErrorNotification(message);
    }

    private const string BoothWaitingNotificationKey = "BoothWaiting";
    private const string MailDeliveryNotificationKey = "MailDelivery";
    private const string RadiationAlertNotificationKey = "RadiationAlert";
    private const string LowHealthAlertNotificationKey = "LowHealthAlert";
    private const string BoothWaitingMessage = "Someone is waiting at the booth";

    /// <summary>
    /// Queues a message on the shared bottom-centre notification slot (see
    /// <see cref="HUDNotificationQueue"/>). Notifications play one at a time and never overlap.
    /// <paramref name="key"/> identifies the entry so it can be updated or hidden later. When
    /// null, the message itself is the key, so repeating the same message does not stack
    /// duplicates. With <paramref name="loop"/> true, the entry keeps coming back until
    /// <see cref="HideQueuedNotification"/> is called with the same key.
    /// </summary>
    public void ShowQueuedNotification(string message, string key = null, bool loop = false)
    {
        if (notificationQueue != null)
            notificationQueue.Show(key, message, loop);
    }

    /// <summary>Removes a queued notification by key (fades it out if it is on screen).</summary>
    public void HideQueuedNotification(string key)
    {
        if (notificationQueue != null)
            notificationQueue.Hide(key);
    }

    /// <summary>
    /// Queues the "someone is waiting at the booth" notification. Call this only on the local
    /// client and only when the player is away from the booth.
    /// </summary>
    public void ShowBoothWaitingNotification()
    {
        ShowQueuedNotification(BoothWaitingMessage, BoothWaitingNotificationKey);
    }

    /// <summary>Hides the booth waiting notification.</summary>
    public void HideBoothWaitingNotification()
    {
        HideQueuedNotification(BoothWaitingNotificationKey);
    }

    /// <summary>
    /// Queues the mail/shipment notification (e.g. "A shipment is waiting at the gate."). With
    /// <paramref name="loop"/> true it keeps coming back until
    /// <see cref="HideMailDeliveryNotification"/> is called, e.g. once a player opens the gate.
    /// </summary>
    public void ShowMailDeliveryNotification(string message, bool loop = false)
    {
        ShowQueuedNotification(message, MailDeliveryNotificationKey, loop);
    }

    /// <summary>Hides the mail delivery notification.</summary>
    public void HideMailDeliveryNotification()
    {
        HideQueuedNotification(MailDeliveryNotificationKey);
    }

    /// <summary>
    /// Queues the looping "Radiation high" alert. Call this only on the local client. It keeps
    /// coming back until <see cref="HideRadiationAlert"/> is called.
    /// </summary>
    public void ShowRadiationAlert(string message = "Radiation high. Take pills to reduce.")
    {
        ShowQueuedNotification(message, RadiationAlertNotificationKey, loop: true);
    }

    /// <summary>Hides the radiation alert notification.</summary>
    public void HideRadiationAlert()
    {
        HideQueuedNotification(RadiationAlertNotificationKey);
    }

    /// <summary>
    /// Queues the looping low-health alert. Driven by <see cref="LowHealthAlertUI"/>. It keeps
    /// coming back until <see cref="HideLowHealthAlert"/> is called.
    /// </summary>
    public void ShowLowHealthAlert(string message = "Health low. Find a way to heal.")
    {
        ShowQueuedNotification(message, LowHealthAlertNotificationKey, loop: true);
    }

    /// <summary>Hides the low-health alert notification.</summary>
    public void HideLowHealthAlert()
    {
        HideQueuedNotification(LowHealthAlertNotificationKey);
    }

    private Coroutine _showDeathScreenRoutine;

    /// <summary>Shows the death screen after the given delay in seconds.</summary>
    public void ShowDeathScreen(float delay)
    {
        if (deathScreenUI == null) return;
        if (_showDeathScreenRoutine != null) StopCoroutine(_showDeathScreenRoutine);
        _showDeathScreenRoutine = StartCoroutine(ShowDeathScreenDelayed(delay));
    }

    private IEnumerator ShowDeathScreenDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        _showDeathScreenRoutine = null;
        deathScreenUI.gameObject.SetActive(true);
    }

    /// <summary>Hides the death screen immediately and cancels any pending delayed show.</summary>
    public void HideDeathScreen()
    {
        if (deathScreenUI == null) return;
        if (_showDeathScreenRoutine != null)
        {
            StopCoroutine(_showDeathScreenRoutine);
            _showDeathScreenRoutine = null;
        }
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

        ClosePlayerUI();
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

        ClosePlayerUI();
    }

    /// <summary>Closes the End Day confirmation popup and restores the player HUD.</summary>
    public void CloseEndDayPopup()
    {
        if (_endDayPopup != null)
            _endDayPopup.SetActive(false);

        ShowPlayerUI();
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
