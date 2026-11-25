using System.Collections;
using System.Collections.Generic;
using Netcode.Transports.Facepunch;
using Steamworks.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Join Game screen with two tabs: "Join by Code" (6-character code / LAN) and "Public Lobbies"
/// (browser of lobbies whose host set them to public — see <see cref="LobbyManager.SetLobbyPublic"/>).
/// </summary>
public class JoinLobbyScreen : MonoBehaviour
{
    [Header("Join by Code")]
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private GameObject connectingAnimation;

    [Header("Tabs")]
    [SerializeField] private GameObject codeTabRoot;
    [SerializeField] private GameObject publicTabRoot;
    [SerializeField] private Button codeTabButton;
    [SerializeField] private Button publicTabButton;

    [Header("Public Lobbies")]
    [SerializeField] private Transform lobbyListContent;
    [SerializeField] private LobbyBrowserRow lobbyRowPrefab;
    [SerializeField] private TMP_Text publicStatusLabel;
    [SerializeField] private Button refreshButton;
    [Tooltip("Optional: filters the listed lobbies by host name.")]
    [SerializeField] private TMP_InputField searchInput;

    private const string ErrorEmptyCode     = "NO CODE ENTERED";
    private const string ErrorInvalidCode   = "INVALID JOIN CODE";
    private const string ErrorLobbyNotFound = "LOBBY NOT FOUND";
    private const string ErrorLobbyFull     = "LOBBY FULL";
    private const string ErrorSteamNotReady = "STEAM NOT CONNECTED";
    private const string ErrorJoinFailed    = "UNABLE TO JOIN LOBBY";
    private const string ErrorInvalidIP     = "INVALID IP ADDRESS";
    private const string ErrorHostNoResponse = "HOST DID NOT RESPOND";
    private const string StatusLANReady     = "READY — PRESS JOIN TO CONNECT";
    private const string StatusJoiningInvite = "JOINING FRIEND'S LOBBY...";
    private const string StatusSearching    = "SEARCHING FOR PUBLIC LOBBIES...";
    private const string StatusNoLobbies    = "NO PUBLIC LOBBIES FOUND";
    private const string StatusJoiningLobby = "JOINING LOBBY...";
    private const string StatusSteamOnly    = "PUBLIC LOBBIES REQUIRE STEAM";
    private const string StatusNoMatches    = "NO LOBBIES MATCH YOUR SEARCH";

    private enum Tab { Code, Public }

    // Public Lobbies is the default tab whenever the screen opens.
    private Tab _activeTab = Tab.Public;
    private bool _isJoining;
    private int _refreshRequestId;
    private readonly List<LobbyBrowserRow> _lobbyRows = new List<LobbyBrowserRow>();

    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
        inviteCodeInput.onValueChanged.AddListener(_ => ClearStatus());

        if (codeTabButton != null) codeTabButton.onClick.AddListener(ShowCodeTab);
        if (publicTabButton != null) publicTabButton.onClick.AddListener(ShowPublicTab);
        if (refreshButton != null) refreshButton.onClick.AddListener(RefreshPublicLobbies);
        if (searchInput != null) searchInput.onValueChanged.AddListener(_ => ApplySearchFilter());
    }

    private void OnEnable()
    {
        LobbyManager.Instance.OnJoinFailed += OnJoinFailed;
        _isJoining = false;
        inviteCodeInput.interactable = true;
        SetConnectingAnimationVisible(false);
        RefreshUI();
        SetTab(Tab.Public);
    }

    private void OnDisable()
    {
        LobbyManager.Instance.OnJoinFailed -= OnJoinFailed;
        SetConnectingAnimationVisible(false);
        _refreshRequestId++; // drop any in-flight lobby request
        ClearLobbyRows();
    }

    // ---------------------------------------------------------------------------
    // Tabs
    // ---------------------------------------------------------------------------

    /// <summary>UnityEvent-friendly: switch to the join-by-code tab.</summary>
    public void ShowCodeTab() => SetTab(Tab.Code);

    /// <summary>UnityEvent-friendly: switch to the public lobby browser and refresh it.</summary>
    public void ShowPublicTab() => SetTab(Tab.Public);

    private void SetTab(Tab tab)
    {
        bool changed = tab != _activeTab;
        _activeTab = tab;

        if (codeTabRoot != null) codeTabRoot.SetActive(tab == Tab.Code);
        if (publicTabRoot != null) publicTabRoot.SetActive(tab == Tab.Public);

        RefreshTabButtons();

        if (tab == Tab.Public && (changed || _lobbyRows.Count == 0))
        {
            _isJoining = false; // a pending code join never blocks browsing
            RefreshPublicLobbies();
        }
    }

    private void RefreshTabButtons()
    {
        // The Selected animator state is reset when the buttons (re)enable, so apply it again
        // a frame later when the screen has just been activated.
        ApplyTabButtonState();
        if (isActiveAndEnabled) StartCoroutine(ApplyTabButtonStateNextFrame());
    }

    private IEnumerator ApplyTabButtonStateNextFrame()
    {
        yield return null;
        ApplyTabButtonState();
    }

    private void ApplyTabButtonState()
    {
        SetTabButtonActive(codeTabButton, _activeTab == Tab.Code);
        SetTabButtonActive(publicTabButton, _activeTab == Tab.Public);
    }

    private static void SetTabButtonActive(Button button, bool active)
    {
        if (button == null || !button.isActiveAndEnabled) return;
        if (button.TryGetComponent(out TextButton textButton))
            textButton.SetActiveTab(active);
    }

    // ---------------------------------------------------------------------------
    // Cold-launch Steam invite
    // ---------------------------------------------------------------------------

    /// <summary>Cold-launch Steam invite: shows the connecting state with code entry locked.</summary>
    public void ShowInviteJoining()
    {
        SetTab(Tab.Code);
        _refreshRequestId++; // drop the public-lobby search started by the default tab
        _isJoining = true;
        inviteCodeInput.interactable = false;
        SetConnectingAnimationVisible(true);
        SetStatus(StatusJoiningInvite);
    }

    /// <summary>Cold-launch Steam invite failed: shows the reason and unlocks code entry.</summary>
    public void ShowInviteJoinFailed(string reason)
    {
        inviteCodeInput.interactable = true;
        OnJoinFailed(reason);
    }

    // ---------------------------------------------------------------------------
    // Join by Code
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Reads the input field and joins via the active transport:
    /// FacepunchTransport — expects a 6-character join code.
    /// UnityTransport     — connects immediately; no code required.
    /// </summary>
    public void JoinWithCode()
    {
        if (NetworkManager.Singleton == null)
            return;

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        string raw = inviteCodeInput.text.Trim();

        if (transport is FacepunchTransport)
        {
            JoinSteam(raw);
        }
        else
        {
            JoinLAN(raw);
        }
    }

    private void JoinSteam(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            SetStatus(ErrorEmptyCode);
            return;
        }

        string code = raw.ToUpperInvariant();

        if (code.Length != 6)
        {
            SetStatus(ErrorInvalidCode);
            return;
        }

        Debug.Log($"[JoinLobbyScreen] Joining Steam lobby by code: {code}");
        _isJoining = true;
        SetConnectingAnimationVisible(true);
        LobbyManager.Instance.JoinLobbyByCode(code);
    }

    /// <summary>
    /// Connects to a LAN host. When UnityTransport is active the input field is hidden,
    /// so <paramref name="raw"/> will always be empty and the default address is used.
    /// </summary>
    private void JoinLAN(string raw)
    {
        string address = string.IsNullOrEmpty(raw) ? "127.0.0.1" : raw;

        if (!IsValidIPv4(address))
        {
            SetStatus(ErrorInvalidIP);
            return;
        }

        Debug.Log($"[JoinLobbyScreen] Joining LAN host at {address}");
        _isJoining = true;
        SetConnectingAnimationVisible(true);
        LobbyManager.Instance.JoinLobbyLAN(address);
    }

    // ---------------------------------------------------------------------------
    // Public Lobbies
    // ---------------------------------------------------------------------------

    /// <summary>Re-requests the public lobby list from Steam and rebuilds the rows.</summary>
    public async void RefreshPublicLobbies()
    {
        if (_isJoining) return;

        int requestId = ++_refreshRequestId;
        ClearLobbyRows();

        if (!(NetworkManager.Singleton?.NetworkConfig.NetworkTransport is FacepunchTransport))
        {
            SetPublicStatus(StatusSteamOnly);
            return;
        }

        if (!Steamworks.SteamClient.IsValid)
        {
            SetPublicStatus(ErrorSteamNotReady);
            return;
        }

        SetPublicStatus(StatusSearching);
        if (refreshButton != null) refreshButton.interactable = false;

        Lobby[] lobbies;
        try
        {
            lobbies = await LobbyManager.Instance.RequestPublicLobbiesAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[JoinLobbyScreen] Public lobby request failed: {e}");
            lobbies = System.Array.Empty<Lobby>();
        }

        // Screen closed, tab re-refreshed, or a join started while waiting.
        if (requestId != _refreshRequestId || this == null || !isActiveAndEnabled)
            return;

        if (refreshButton != null) refreshButton.interactable = true;

        if (lobbies.Length == 0)
        {
            SetPublicStatus(StatusNoLobbies);
            return;
        }

        ClearPublicStatus();
        foreach (Lobby lobby in lobbies)
            SpawnLobbyRow(lobby);
        ApplySearchFilter();
    }

    private void SpawnLobbyRow(Lobby lobby)
    {
        if (lobbyRowPrefab == null || lobbyListContent == null) return;

        string hostName = lobby.GetData("host");
        if (string.IsNullOrEmpty(hostName)) hostName = "Unknown Host";

        ulong lobbyId = lobby.Id.Value;
        LobbyBrowserRow row = Instantiate(lobbyRowPrefab, lobbyListContent);
        row.name = $"Lobby Row ({lobbyId})";
        row.gameObject.SetActive(true);

        row.Populate(
            hostName,
            LobbyManager.GetLobbyVersion(lobby),
            DevSpectatorConfig.FormatBrowserPlayerCount(lobby),
            LobbyManager.IsLobbyVersionCompatible(lobby),
            () => JoinPublicLobby(lobbyId));

        _lobbyRows.Add(row);
    }

    /// <summary>Shows only rows whose host name contains the search text.</summary>
    private void ApplySearchFilter()
    {
        if (_lobbyRows.Count == 0) return;

        string query = searchInput != null ? searchInput.text.Trim() : string.Empty;
        int visible = 0;
        foreach (LobbyBrowserRow row in _lobbyRows)
        {
            if (row == null) continue;
            bool match = row.Matches(query);
            row.gameObject.SetActive(match);
            if (match) visible++;
        }

        if (_isJoining) return;
        if (visible == 0) SetPublicStatus(StatusNoMatches);
        else ClearPublicStatus();
    }

    private void JoinPublicLobby(ulong lobbyId)
    {
        if (_isJoining) return;

        _isJoining = true;
        _refreshRequestId++;
        SetLobbyRowsInteractable(false);
        if (refreshButton != null) refreshButton.interactable = false;
        SetPublicStatus(StatusJoiningLobby);
        LobbyManager.Instance.JoinPublicLobby(lobbyId);
    }

    private void SetLobbyRowsInteractable(bool interactable)
    {
        foreach (LobbyBrowserRow row in _lobbyRows)
            if (row != null) row.SetInteractable(interactable);
    }

    private void ClearLobbyRows()
    {
        foreach (LobbyBrowserRow row in _lobbyRows)
            if (row != null) Destroy(row.gameObject);
        _lobbyRows.Clear();
    }

    // ---------------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------------

    private void OnJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        _isJoining = false;
        SetConnectingAnimationVisible(false);
        SetLobbyRowsInteractable(true);
        if (refreshButton != null) refreshButton.interactable = true;

        SetStatus(reason switch
        {
            "CODE_NOT_FOUND"  => ErrorLobbyNotFound,
            "STEAM_NOT_READY" => ErrorSteamNotReady,
            "LobbyFull"       => ErrorLobbyFull,
            "Full"            => ErrorLobbyFull,
            "DoesntExist"     => ErrorLobbyNotFound,
            "TIMEOUT"         => ErrorHostNoResponse,
            _                 => ErrorJoinFailed,
        });
    }

    private void SetConnectingAnimationVisible(bool visible)
    {
        if (connectingAnimation != null)
            connectingAnimation.SetActive(visible);
    }

    /// <summary>Writes to the active tab's status label.</summary>
    private void SetStatus(string message)
    {
        if (_activeTab == Tab.Public && publicStatusLabel != null)
            SetPublicStatus(message);
        else
            statusLabel.text = message;
    }

    private void ClearStatus() => statusLabel.text = string.Empty;

    private void SetPublicStatus(string message)
    {
        if (publicStatusLabel == null) return;
        publicStatusLabel.gameObject.SetActive(true);
        publicStatusLabel.text = message;
    }

    private void ClearPublicStatus()
    {
        if (publicStatusLabel == null) return;
        publicStatusLabel.text = string.Empty;
    }

    /// <summary>
    /// Refreshes the UI to match the active transport.
    /// UnityTransport: hides the code input and shows a ready prompt.
    /// FacepunchTransport: shows the code input with appropriate placeholder.
    /// </summary>
    private void RefreshUI()
    {
        if (NetworkManager.Singleton == null)
            return;

        bool isUnityTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport;

        if (isUnityTransport)
        {
            statusLabel.text = StatusLANReady;
        }
        else
        {
            ClearStatus();
            UpdatePlaceholder();
        }
    }

    /// <summary>Updates the input field placeholder text when using FacepunchTransport.</summary>
    private void UpdatePlaceholder()
    {
        var placeholder = inviteCodeInput.placeholder as TMP_Text;
        if (placeholder != null)
            placeholder.text = "Enter join code (e.g. ABC123)";
    }

    private static bool IsValidIPv4(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        string[] parts = address.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int value) || value < 0 || value > 255)
                return false;
        }

        return true;
    }
}
