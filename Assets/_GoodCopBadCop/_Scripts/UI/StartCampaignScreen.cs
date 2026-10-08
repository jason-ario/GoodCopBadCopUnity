using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Steamworks;

/// <summary>
/// Pre-game lobby screen shown after networking is established.
/// Displays connected players, their ready state, and lets the host start
/// the game once everyone is ready.
///
/// Supports both solo/campaign and multiplayer sessions.
/// </summary>
public class StartCampaignScreen : MonoBehaviour
{
    [Header("Player List")]
    [SerializeField] private PlayerInfoPanel panelPrefab;
    [SerializeField] private Transform panelsContainer;

    private readonly List<PlayerInfoPanel> _spawnedPanels = new List<PlayerInfoPanel>();

    [Header("Lobby Info")]
    [SerializeField] private TextMeshProUGUI inviteCodeText;
    [SerializeField] private GameObject inviteCodeSection;
    [SerializeField] private Button copyInviteCodeButton;
    [Tooltip("Shows \"New Game - Day 1\" for a fresh slot or \"Continue - Day N\" for a slot " +
             "already in progress. Was previously left as static placeholder text (\"New Game - " +
             "Day 0\") and never updated to reflect the actual chosen save slot.")]
    [SerializeField] private TextMeshProUGUI dayNumberText;

    [Header("Copy Feedback")]
    [Tooltip("Optional pre-built \"Copied to clipboard\" toast. If left empty, one is generated at " +
             "runtime underneath the copy button, reusing the ready checkmark sprite and invite code font.")]
    [SerializeField] private CanvasGroup copiedNotification;
    [SerializeField] private string copiedMessage = "Copied to clipboard";
    [SerializeField] private float copiedFontSize = 18f;
    [SerializeField] private float copiedOffsetBelowButton = 6f;
    [SerializeField] private float copiedFadeInDuration = 0.12f;
    [SerializeField] private float copiedHoldDuration = 1.2f;
    [SerializeField] private float copiedFadeOutDuration = 0.5f;
    [Tooltip("How far (canvas units) the toast drifts upward while fading out.")]
    [SerializeField] private float copiedDriftDistance = 6f;

    private Coroutine _copiedRoutine;
    private Vector2 _copiedBasePosition;
    private bool _copiedBaseCaptured;

    [Header("Host Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private GameObject waitForHostText;

    [Header("Ready Up")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TextMeshProUGUI readyButtonLabel;
    [Tooltip("Checkmark sprite shown when readied up. Used instead of a text glyph since the " +
        "button font doesn't include a checkmark character.")]
    [SerializeField] private GameObject readyCheckmarkIcon;

    private bool _isMultiplayer;
    private bool _isReady;

    // Per-client ready state tracked on the host only; clients signal via RPC.
    // Key = NGO clientId.
    private readonly Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void OnEnable()
    {
        _isReady = false;
        UpdateReadyButtonLabel();

        LobbyManager.Instance.OnLobbyUpdated += RefreshUI;
        LobbyManager.Instance.OnKicked += OnKicked;

        if (PlayerReadyManager.Instance != null)
            PlayerReadyManager.Instance.OnReadyStatesChanged += OnReadyStatesChanged;

        if (copyInviteCodeButton != null)
            copyInviteCodeButton.onClick.AddListener(CopyInviteCode);

        RefreshUI();
    }

    private void OnDisable()
    {
        ClearPanels();

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyUpdated -= RefreshUI;
            LobbyManager.Instance.OnKicked -= OnKicked;
        }

        if (PlayerReadyManager.Instance != null)
            PlayerReadyManager.Instance.OnReadyStatesChanged -= OnReadyStatesChanged;

        if (copyInviteCodeButton != null)
            copyInviteCodeButton.onClick.RemoveListener(CopyInviteCode);

        HideCopiedNotification();
    }

    // ---------------------------------------------------------------------------
    // Initialisation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Called by <see cref="MainMenuController"/> before activating this screen.
    /// </summary>
    public void Setup(bool isMultiplayer)
    {
        _isMultiplayer = isMultiplayer;
        _readyStates.Clear();
        _isReady = false;

        // Invite code section is only relevant in multiplayer.
        if (inviteCodeSection != null)
            inviteCodeSection.SetActive(isMultiplayer);
    }

    // ---------------------------------------------------------------------------
    // Ready Up
    // ---------------------------------------------------------------------------

    /// <summary>Toggles the local player's ready state and notifies the host.</summary>
    public void OnReadyPressed()
    {
        _isReady = !_isReady;
        UpdateReadyButtonLabel();

        // Signal the host via the PlayerReadyManager NetworkBehaviour.
        if (PlayerReadyManager.Instance != null)
            PlayerReadyManager.Instance.SetReady(_isReady);

        EvaluateStartButton(LobbyManager.Instance.GetMembersSnapshot());
    }

    private void UpdateReadyButtonLabel()
    {
        if (readyButtonLabel != null)
            readyButtonLabel.text = _isReady ? "READY" : "READY UP";

        // Checkmark is a sprite rather than a text glyph since the button font
        // doesn't include a checkmark character.
        if (readyCheckmarkIcon != null)
            readyCheckmarkIcon.SetActive(_isReady);
    }

    /// <summary>Called by <see cref="PlayerReadyManager"/> when any player's ready state changes.</summary>
    public void OnReadyStatesChanged(Dictionary<ulong, bool> states)
    {
        _readyStates.Clear();
        foreach (var kvp in states)
            _readyStates[kvp.Key] = kvp.Value;

        RefreshUI();
    }

    // ---------------------------------------------------------------------------
    // UI Refresh
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Reflects the active save slot's actual state instead of the static placeholder text
    /// left on the prefab. A never-started slot's <see cref="SaveSlot.CurrentDay"/> is 0, so
    /// clamp to 1 the same way <see cref="CampaignManager.StartCampaign"/> does.
    /// </summary>
    private void RefreshDayNumberText()
    {
        if (dayNumberText == null) return;

        SaveSlot slot = SaveDataManager.Instance?.ActiveSlot;
        int displayDay = Mathf.Max(1, slot?.CurrentDay ?? 0);
        bool isNewGame = slot == null || !slot.IsOccupied;
        string duskSuffix = !isNewGame && slot.IsDuskCheckpointFor(slot.CurrentDay) ? " (Dusk)" : string.Empty;

        dayNumberText.text = isNewGame ? $"New Game - Day {displayDay}" : $"Continue - Day {displayDay}{duskSuffix}";
    }

    private void RefreshUI()
    {
        RefreshDayNumberText();

        // Determine who is in the lobby right now.
        var members = LobbyManager.Instance.GetMembersSnapshot();
        bool isHost = LobbyManager.Instance.IsHost;

        // If no lobby members yet, fall back to showing just the local player.
        bool hasLobby = members != null && members.Length > 0;

        // Invite code
        if (inviteCodeText != null)
        {
            ulong lobbyId = LobbyManager.Instance.CurrentLobby.Id;
            inviteCodeText.text = lobbyId != 0 ? LobbyManager.Instance.CurrentJoinCode : string.Empty;
        }

        // Destroy previously spawned panels and rebuild from scratch.
        ClearPanels();

        bool hasSecondPlayer = members != null && members.Length >= 2;

        if (hasLobby)
        {
            ulong hostSteamId = LobbyManager.Instance.CurrentLobby.Owner.Id.Value;
            foreach (var member in members)
            {
                ulong steamId = member.Id.Value;
                // Solo: always show green READY. Multi: use actual ready state.
                bool ready = !hasSecondPlayer || (_readyStates.TryGetValue(steamId, out bool r) && r);
                SpawnPanel(member.Name, ready, isHost: steamId == hostSteamId);
            }
        }
        else
        {
            // Lobby not yet available — show the local player as a placeholder.
            string localName = SteamClient.IsValid ? SteamClient.Name : "Player";
            SpawnPanel(localName, isReady: true, isHost: true);
        }

        EvaluateStartButton(members);

        if (waitForHostText != null)
            waitForHostText.SetActive(!isHost);
    }

    /// <summary>Instantiates one panel and adds it to the tracked list.</summary>
    private void SpawnPanel(string playerName, bool isReady, bool isHost = false)
    {
        if (panelPrefab == null || panelsContainer == null) return;

        PlayerInfoPanel panel = Instantiate(panelPrefab, panelsContainer);
        panel.gameObject.SetActive(true);
        panel.PopulateInfo(playerName, isReady, isHost);
        _spawnedPanels.Add(panel);
    }

    /// <summary>Destroys all previously spawned panels.</summary>
    private void ClearPanels()
    {
        foreach (var panel in _spawnedPanels)
        {
            if (panel != null)
                Destroy(panel.gameObject);
        }
        _spawnedPanels.Clear();
    }

    private void EvaluateStartButton(Friend[] members)
    {
        bool isHost = LobbyManager.Instance.IsHost;
        bool hasSecondPlayer = members != null && members.Length >= 2;

        // Ready-up button: only shown when a second player is in the lobby.
        if (readyButton != null)
            readyButton.gameObject.SetActive(hasSecondPlayer);

        if (startButton == null) return;

        if (!isHost)
        {
            // Clients never see the start button.
            startButton.gameObject.SetActive(false);
            return;
        }

        if (!hasSecondPlayer)
        {
            // Solo / waiting for someone to join — host can start immediately.
            startButton.gameObject.SetActive(true);
            startButton.interactable = true;
            return;
        }

        // Second player present — start button only enabled once everyone is ready.
        bool allReady = AllPlayersReady(members);
        startButton.gameObject.SetActive(allReady);
        startButton.interactable = allReady;
    }

    private bool AllPlayersReady(Friend[] members)
    {
        foreach (var member in members)
        {
            ulong id = member.Id.Value;
            if (!_readyStates.TryGetValue(id, out bool ready) || !ready)
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------------
    // Invite
    // ---------------------------------------------------------------------------

    /// <summary>Opens the Steam invite overlay. Only valid in multiplayer.</summary>
    public void InviteFriend()
    {
        LobbyManager.Instance.OpenInviteFriendsPopup();
    }

    /// <summary>Copies the current join code to the system clipboard.</summary>
    public void CopyInviteCode()
    {
        string code = LobbyManager.Instance.CurrentJoinCode;
        if (string.IsNullOrEmpty(code)) return;

        GUIUtility.systemCopyBuffer = code;
        ShowCopiedNotification();
    }

    // ---------------------------------------------------------------------------
    // Copy Feedback
    // ---------------------------------------------------------------------------

    private void ShowCopiedNotification()
    {
        if (copiedNotification == null)
            copiedNotification = BuildCopiedNotification();
        else if (!_copiedBaseCaptured)
            _copiedBasePosition = ((RectTransform)copiedNotification.transform).anchoredPosition;
        if (copiedNotification == null) return;
        _copiedBaseCaptured = true;

        // Restart cleanly on repeated clicks.
        if (_copiedRoutine != null)
            StopCoroutine(_copiedRoutine);
        _copiedRoutine = StartCoroutine(CopiedNotificationRoutine());
    }

    private void HideCopiedNotification()
    {
        if (_copiedRoutine != null)
        {
            StopCoroutine(_copiedRoutine);
            _copiedRoutine = null;
        }

        if (copiedNotification != null && _copiedBaseCaptured)
        {
            copiedNotification.alpha = 0f;
            ((RectTransform)copiedNotification.transform).anchoredPosition = _copiedBasePosition;
            copiedNotification.gameObject.SetActive(false);
        }
    }

    private IEnumerator CopiedNotificationRoutine()
    {
        var rect = (RectTransform)copiedNotification.transform;
        copiedNotification.gameObject.SetActive(true);
        rect.anchoredPosition = _copiedBasePosition;

        // Unscaled time so the menu feedback works regardless of Time.timeScale.
        for (float t = 0f; t < copiedFadeInDuration; t += Time.unscaledDeltaTime)
        {
            copiedNotification.alpha = t / copiedFadeInDuration;
            yield return null;
        }
        copiedNotification.alpha = 1f;

        yield return new WaitForSecondsRealtime(copiedHoldDuration);

        for (float t = 0f; t < copiedFadeOutDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / copiedFadeOutDuration;
            copiedNotification.alpha = 1f - k;
            rect.anchoredPosition = _copiedBasePosition + Vector2.up * (copiedDriftDistance * k);
            yield return null;
        }

        HideCopiedNotification();
    }

    /// <summary>
    /// The copy button is normally wired via a persistent OnClick (→ CopyInviteCode) rather than
    /// the copyInviteCodeButton field, so find it by its persistent call if the field is empty.
    /// Not assigned back to the field, as OnEnable would then add a second listener.
    /// </summary>
    private Button ResolveCopyButton()
    {
        if (copyInviteCodeButton != null) return copyInviteCodeButton;

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            var onClick = button.onClick;
            for (int i = 0; i < onClick.GetPersistentEventCount(); i++)
            {
                if (onClick.GetPersistentTarget(i) == this &&
                    onClick.GetPersistentMethodName(i) == nameof(CopyInviteCode))
                    return button;
            }
        }

        // Last resort: whatever button was just clicked.
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null ? selected.GetComponent<Button>() : null;
    }

    /// <summary>
    /// Builds a small "[checkmark] Copied to clipboard" row anchored just below the copy button.
    /// The checkmark uses the ready-up checkmark sprite since the UI font has no checkmark glyph.
    /// </summary>
    private CanvasGroup BuildCopiedNotification()
    {
        Button anchorButton = ResolveCopyButton();
        if (anchorButton == null)
        {
            Debug.LogWarning("[StartCampaignScreen] Couldn't find the copy invite code button to anchor " +
                             "the \"Copied\" notification under. Assign copyInviteCodeButton or copiedNotification.", this);
            return null;
        }

        var root = new GameObject("Copied Notification", typeof(RectTransform), typeof(CanvasGroup),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        var rect = (RectTransform)root.transform;
        rect.SetParent(anchorButton.transform, false);

        // Hang below the button's bottom edge, centered.
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        _copiedBasePosition = new Vector2(0f, -copiedOffsetBelowButton);
        rect.anchoredPosition = _copiedBasePosition;

        root.GetComponent<LayoutElement>().ignoreLayout = true;

        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = copiedFontSize * 0.3f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var fitter = root.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Color textColor = inviteCodeText != null ? inviteCodeText.color : Color.white;

        // Checkmark icon (reuses the ready button's sprite if available).
        Image readyIconImage = readyCheckmarkIcon != null ? readyCheckmarkIcon.GetComponent<Image>() : null;
        if (readyIconImage != null && readyIconImage.sprite != null)
        {
            var iconGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(rect, false);
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = readyIconImage.sprite;
            icon.color = readyIconImage.color;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconLayout = iconGo.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = iconLayout.preferredHeight = copiedFontSize;
        }

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(rect, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        if (inviteCodeText != null)
        {
            label.font = inviteCodeText.font;
            label.fontSharedMaterial = inviteCodeText.fontSharedMaterial;
        }
        label.text = copiedMessage;
        label.fontSize = copiedFontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        root.SetActive(false);
        return group;
    }

    // ---------------------------------------------------------------------------
    // Start / Exit
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Starts the game for all connected players. Either the host or a client may initiate this.
    /// Runs the full transition: fades out the menu, spawns every connected player into the
    /// lobby, switches cameras, then starts the campaign — all via ClientRpc broadcasts, so
    /// every currently connected client (including anyone who joined while ready-ing up)
    /// transitions into gameplay at the same time as the host.
    ///
    /// A save already past Day 1 skips the lobby-outside spawn, the Start Shift Gate's Day 1
    /// onboarding (start-shift screen + intro cutscene), and the main menu's intro cinematic
    /// entirely — the player is dropped straight into the bunker as if resuming mid-campaign.
    /// Mirrors <see cref="MainMenuController.ContinueGame"/>; this is the path actually used
    /// when a save is chosen from the campaign slot-selection screen (see
    /// <see cref="CampaignScreenController.OnSlotChosen"/> → <see cref="MainMenuController.StartNewGame"/>
    /// → this pre-game lobby screen), so it needs the same day-aware branch.
    /// </summary>
    public void StartGame()
    {
        if (_isStartingResumedDay) return;
        if (MainMenuController.Instance != null && MainMenuController.Instance.IsGameStartLocked) return;

        // The fade starts now — block all menu input so Escape/Back can't exit the lobby
        // mid-transition (that left players on a black screen with no player spawned).
        MainMenuController.Instance?.LockForGameStart();

        // Commit the slot to disk now that the player has confirmed they want to play.
        SaveDataManager.Instance.InitialiseActiveSlot();

        bool resumingPastDay1 = SaveDataManager.Instance.ShouldResumeDirectlyIntoSavedDay;

        if (resumingPastDay1)
        {
            // Run on UIController (persistent) so hiding this screen can't kill the coroutine.
            _isStartingResumedDay = true;
            UIController.Instance.StartCoroutine(StartResumedDayBehindBlackout());
            return;
        }

        GameManager.Instance.TryStartGame();
        GameManager.Instance.TransitionToLobby();
    }

    private bool _isStartingResumedDay;

    /// <summary>
    /// Resumed (Day 2+) start: fades every screen to black BEFORE players are spawned at the
    /// outside lobby point, so neither the spawn nor ResumeSavedDay's teleport into the bunker
    /// is ever visible. Mirrors <see cref="MainMenuController.ContinueGame"/>'s fade-first order.
    /// ResumeSavedDay owns the single reveal once everyone is in the bunker.
    /// </summary>
    private IEnumerator StartResumedDayBehindBlackout()
    {
        GameManager.Instance.PlayTransitionStinger();
        GameManager.Instance.FadeClientsToBlackForResumedDay();
        yield return UIController.Instance.StartCoroutine(UIController.Instance.FadeInAndWait());

        GameManager.Instance.TryStartGame();

        // Not going through the lobby transition — clear the flag it would otherwise
        // have cleared itself once players were spawned there.
        GameManager.Instance.CancelLobbyTransition();

        // TransitionToLobby (skipped) is normally what spawns every connected client —
        // without it, a client whose connection was deferred while still on this pre-game
        // screen would never get a player object. Spawn them explicitly; ResumeSavedDay
        // repositions everyone from here straight into the bunker.
        GameManager.Instance.SpawnAllPlayersForResumedDay();
        ShiftManager.Instance.ResumeSavedDay();

        _isStartingResumedDay = false;
    }

    /// <summary>
    /// Solo quick-start: creates a lobby and starts immediately.
    /// Kept for backward-compatibility with existing UnityEvent bindings.
    /// </summary>
    public async void StartSolo()
    {
        bool success = await LobbyManager.Instance.CreateLobby();
        if (success)
            GameManager.Instance.TryStartGame();
    }

    /// <summary>
    /// Creates a lobby and waits for a partner to join.
    /// Kept for backward-compatibility with existing UnityEvent bindings.
    /// </summary>
    public async void StartCampaignAsHost()
    {
        await LobbyManager.Instance.CreateLobby();
        RefreshUI();
    }

    public void ExitLobby()
    {
        // Back is ignored once the game start has been committed.
        if (MainMenuController.Instance != null && MainMenuController.Instance.IsGameStartLocked) return;
        LeaveLobby();
    }

    private void LeaveLobby()
    {
        LobbyManager.Instance.ExitLobby();
        MainMenuController.Instance.BackFromPreGameLobbyScreen();
    }

    // ---------------------------------------------------------------------------
    // Callbacks
    // ---------------------------------------------------------------------------

    private void OnKicked()
    {
        LeaveLobby();
    }
}
