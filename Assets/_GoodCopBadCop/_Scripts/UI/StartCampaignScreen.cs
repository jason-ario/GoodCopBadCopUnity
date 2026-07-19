using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("Host Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private GameObject waitForHostText;

    [Header("Ready Up")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TextMeshProUGUI readyButtonLabel;

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

        if (copyInviteCodeButton != null)
            copyInviteCodeButton.onClick.RemoveListener(CopyInviteCode);
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
            readyButtonLabel.text = _isReady ? "READY ✓" : "READY UP";
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

    private void RefreshUI()
    {
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
        if (!string.IsNullOrEmpty(code))
            GUIUtility.systemCopyBuffer = code;
    }

    // ---------------------------------------------------------------------------
    // Start / Exit
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Host-only. Starts the game for all connected players.
    /// TransitionToLobby was already called when the lobby was created,
    /// so here we only need to kick off the game start sequence.
    /// </summary>
    public void StartGame()
    {
        // Commit the slot to disk now that the player has confirmed they want to play.
        SaveDataManager.Instance.InitialiseActiveSlot();

        // Run the full transition: fade out UI, play fanfare stinger, spawn players,
        // switch cameras, then fade back in. TryStartGame sets HasGameStarted so
        // late-joining logic works correctly.
        GameManager.Instance.TransitionToLobby();
        GameManager.Instance.TryStartGame();
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
        LobbyManager.Instance.ExitLobby();
        MainMenuController.Instance.BackToHomeScreen();
    }

    // ---------------------------------------------------------------------------
    // Callbacks
    // ---------------------------------------------------------------------------

    private void OnKicked()
    {
        ExitLobby();
    }
}
