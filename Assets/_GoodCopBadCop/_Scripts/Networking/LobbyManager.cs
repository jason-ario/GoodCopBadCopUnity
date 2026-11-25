using System;
using System.Linq;
using System.Threading.Tasks;
using GoodCopBadCop.Networking;
using Netcode.Transports.Facepunch;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode.Transports.UTP;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    /// <summary>
    /// True while the local player is voluntarily leaving the session (e.g. returning to the
    /// main menu, quitting). Connection-loss handling should ignore NetworkManager disconnect
    /// callbacks while this is true, since the disconnect was requested, not accidental.
    /// </summary>
    public static bool IsIntentionalDisconnect { get; private set; }

    public Lobby CurrentLobby { get; private set; }

    public event Action OnLobbyUpdated;
    public event Action OnKicked;
    /// <summary>Fires with a reason code (one of the JoinFail* constants or a Steam RoomEnter name) when
    /// a join fails. Turn it into player-facing text with <see cref="DescribeJoinFailure"/>.</summary>
    public event Action<string> OnJoinFailed;

    // Join failure reasons passed through OnJoinFailed (besides Steam's RoomEnter names and NGO's
    // connection-approval reasons such as "LobbyFull").
    public const string JoinFailCodeNotFound = "CODE_NOT_FOUND";
    public const string JoinFailSteamNotReady = "STEAM_NOT_READY";
    public const string JoinFailSteamOffline = "STEAM_OFFLINE";
    public const string JoinFailVersionMismatch = "VERSION_MISMATCH";
    public const string JoinFailAlreadyInLobby = "ALREADY_IN_LOBBY";
    public const string JoinFailInProgress = "JOIN_IN_PROGRESS";
    public const string JoinFailLobbyFull = "LobbyFull";
    public const string JoinFailAbandoned = "LOBBY_ABANDONED";
    public const string JoinFailHostUnreachable = "HOST_UNREACHABLE";
    public const string JoinFailTimeout = "TIMEOUT";
    public const string JoinFailDisconnected = "DISCONNECTED";
    public const string JoinFailError = "Error";

    // English source text; render-time localization translates it on labels, Loc.T in toasts.
    private static readonly string MsgLobbyNotFound = Loc.N("LOBBY NOT FOUND");
    private static readonly string MsgLobbyFull = Loc.N("LOBBY FULL");
    private static readonly string MsgSteamNotReady = Loc.N("STEAM NOT CONNECTED");
    private static readonly string MsgJoinFailed = Loc.N("UNABLE TO JOIN LOBBY");
    private static readonly string MsgHostNoResponse = Loc.N("HOST DID NOT RESPOND");
    private static readonly string MsgDisconnected = Loc.N("LOST CONNECTION TO HOST");
    private static readonly string MsgVersionMismatch = Loc.N("HOST IS ON A DIFFERENT GAME VERSION");
    private static readonly string MsgAlreadyInLobby = Loc.N("YOU'RE ALREADY IN THIS LOBBY");
    private static readonly string MsgJoinInProgress = Loc.N("ALREADY JOINING A LOBBY");
    private static readonly string MsgNotAllowed = Loc.N("LOBBY IS NOT ACCEPTING PLAYERS");
    private static readonly string MsgBlocked = Loc.N("YOU CAN'T JOIN THIS LOBBY");
    private static readonly string MsgLimitedAccount = Loc.N("LIMITED STEAM ACCOUNTS CAN'T JOIN LOBBIES");
    private static readonly string MsgRateLimited = Loc.N("TOO MANY ATTEMPTS, TRY AGAIN SHORTLY");

    /// <summary>English source text for a join failure reason (translated at display time).</summary>
    public static string DescribeJoinFailure(string reason)
    {
        switch (reason)
        {
            case JoinFailCodeNotFound:
            case JoinFailAbandoned:
            case "DoesntExist":
                return MsgLobbyNotFound;
            case JoinFailSteamNotReady:
            case JoinFailSteamOffline:
                return MsgSteamNotReady;
            case JoinFailLobbyFull:
            case "Full":
                return MsgLobbyFull;
            case JoinFailVersionMismatch:
                return MsgVersionMismatch;
            case JoinFailAlreadyInLobby:
                return MsgAlreadyInLobby;
            case JoinFailInProgress:
                return MsgJoinInProgress;
            case JoinFailHostUnreachable:
            case JoinFailTimeout:
                return MsgHostNoResponse;
            case JoinFailDisconnected:
                return MsgDisconnected;
            case "NotAllowed":
                return MsgNotAllowed;
            case "Banned":
            case "CommunityBan":
            case "MemberBlockedYou":
            case "YouBlockedMember":
                return MsgBlocked;
            case "Limited":
                return MsgLimitedAccount;
            case "RatelimitExceeded":
                return MsgRateLimited;
            default:
                return MsgJoinFailed;
        }
    }

    private FacepunchTransport facepunchTransport;
    private bool inviteOverlayWasOpenedByUs;
    // True once Steam confirmed the overlay we requested actually appeared.
    private bool inviteOverlayActivated;
    // Bumped on every open/cancel so a stale watchdog can't close a newer invite request.
    private int inviteRequestId;
    // Steam lobby we entered and immediately left (full, abandoned, wrong version, cancelled); its
    // late OnLobbyEntered callback must not make it the current lobby again.
    private ulong _rejectedFullLobbyId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        _launchArgsConsumed = false;
        _launchInviteLobbyId = null;
        _launchInviteResolved = false;
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Launched from a Steam invite: skip the studio splash so its logo sequence can't finish
        // after the join and drop the player back on the home screen.
        if (IsLaunchInviteJoinPending)
            MainMenuSplashScreen.Suppress();

        InitializeSteam();
    }

    /// <summary>
    /// Initializes Steam using the App ID configured on the scene's <see cref="FacepunchTransport"/>.
    /// That inspector field is the ONLY source of the App ID — there is no hardcoded fallback.
    /// </summary>
    private static void InitializeSteam()
    {
        var transport = FindFirstObjectByType<FacepunchTransport>(FindObjectsInactive.Include);
        if (transport == null)
        {
            Debug.LogError("[LobbyManager] No FacepunchTransport found in the scene — cannot determine Steam App ID. Steam not initialized.");
            return;
        }

        uint appId = transport.SteamAppId;
        if (appId == 0)
        {
            Debug.LogError("[LobbyManager] FacepunchTransport.steamAppId is 0 — set the Steam App ID on the transport. Steam not initialized.");
            return;
        }

        // FacepunchTransport.Awake may already have initialized Steam. Calling Init a second
        // time throws "already initialized", which was being misreported as "is Steam running?".
        if (SteamClient.IsValid)
        {
            if (SteamClient.AppId.Value != appId)
                Debug.LogWarning($"[LobbyManager] Steam already initialized with AppId={SteamClient.AppId.Value}, expected {appId}. " +
                                 "Players on different App IDs cannot find each other's lobbies by join code.");
            else
                Debug.Log($"[LobbyManager] Steam already initialized. Name={SteamClient.Name} AppId={SteamClient.AppId.Value}");
            return;
        }

        try
        {
            // asyncCallbacks: false because we call RunCallbacks() manually in Update.
            SteamClient.Init(appId, asyncCallbacks: false);

            if (!SteamClient.IsValid)
                Debug.LogError("[LobbyManager] SteamClient.Init succeeded but IsValid is false.");
            else
                Debug.Log($"[LobbyManager] Steam initialized. Name={SteamClient.Name} AppId={SteamClient.AppId.Value}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyManager] SteamClient.Init failed for AppId={appId}. Make sure Steam is running, logged in, " +
                           $"and this account has access to that App ID. {e.Message}");
        }
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("LobbyManager: NetworkManager.Singleton is null.");
            return;
        }

        facepunchTransport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();

        NetworkTransport networkTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (networkTransport is FacepunchTransport)
        {
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDataChanged += OnLobbyMemberDataChanged;
            SteamMatchmaking.OnLobbyDataChanged += OnLobbyDataChanged;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamFriends.OnGameOverlayActivated += OnOverlayToggled;
        }

        // Connection approval is enabled for every build (it is part of NGO's config hash) so the
        // host can identify hidden developer spectators and enforce the visible-player cap.
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStopped += OnServerStopped;

        if (networkTransport is FacepunchTransport)
            _ = TryJoinLobbyFromLaunchArgs();
    }

    private const string ConnectLobbyLaunchArg = "+connect_lobby";
    private const float LaunchInviteJoinTimeoutSeconds = 20f;
    private static bool _launchArgsConsumed;
    private static ulong? _launchInviteLobbyId;
    private static bool _launchInviteResolved;

    /// <summary>Lobby ID from "+connect_lobby" on the command line, or 0. Parsed once.</summary>
    private static ulong LaunchInviteLobbyId =>
        _launchInviteLobbyId ??= ParseConnectLobbyArg(Environment.GetCommandLineArgs());

    /// <summary>
    /// True from startup until a cold-launch Steam invite join either reaches the lobby/game or
    /// fails. While true, <see cref="MainMenuController"/> keeps the home screen hidden.
    /// </summary>
    public static bool IsLaunchInviteJoinPending => !_launchInviteResolved && LaunchInviteLobbyId != 0;

    private const string LaunchInviteTimeoutReason = "TIMEOUT";
    private const string LaunchInviteDisconnectedReason = "DISCONNECTED";

    /// <summary>
    /// Ends a pending launch-invite join as failed: leaves any half-joined Steam lobby / NGO
    /// session (flagged intentional so ConnectionLossHandler doesn't reload the scene) and shows
    /// the reason on the join screen.
    /// </summary>
    private async Task FailLaunchInvite(string reason)
    {
        if (_launchInviteResolved)
            return;
        _launchInviteResolved = true;

        Debug.LogWarning($"[LobbyManager] Launch invite join failed: {reason}");

        if (CurrentLobby.Id != 0 || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
            await ExitLobbyAsync();

        if (MainMenuController.Instance != null)
            MainMenuController.Instance.ShowInviteJoinFailed(reason);
    }

    /// <summary>Player backed out of the join screen while a launch-invite join was connecting.</summary>
    public async void CancelLaunchInviteJoin()
    {
        if (_launchInviteResolved)
            return;
        _launchInviteResolved = true;

        Debug.Log("[LobbyManager] Launch invite join cancelled by player.");
        await ExitLobbyAsync();
    }

    /// <summary>
    /// Cold-launch invite handling. When a player accepts a Steam lobby invite while the game is
    /// closed, Steam launches the game with "+connect_lobby &lt;lobbyId&gt;" on the command line and
    /// does NOT raise <see cref="SteamFriends.OnGameLobbyJoinRequested"/> (that callback only fires
    /// while the game is already running). Parse the argument once at startup and join.
    /// </summary>
    private async Task TryJoinLobbyFromLaunchArgs()
    {
        if (_launchArgsConsumed)
            return;
        _launchArgsConsumed = true;

        ulong lobbyId = LaunchInviteLobbyId;
        if (lobbyId == 0)
            return;

        Debug.Log($"[LobbyManager] Launched from Steam invite — {ConnectLobbyLaunchArg} {lobbyId}");

        // Wait for Steam and the main menu UI to be ready so the host's join RPCs land on an
        // initialized client (same state as accepting an invite while already in the menu).
        const float ReadyTimeoutSeconds = 10f;
        float waited = 0f;
        while ((!SteamClient.IsValid || UIController.Instance == null) && waited < ReadyTimeoutSeconds)
        {
            await Task.Delay(100);
            waited += 0.1f;
            if (this == null || _launchInviteResolved)
                return;
        }

        if (!SteamClient.IsValid)
        {
            Debug.LogError("[LobbyManager] Steam not initialized — cannot join lobby from launch invite.");
            await FailLaunchInvite("STEAM_NOT_READY");
            return;
        }

        // Capture the Steam-side failure reason (DoesntExist, Full, LobbyFull, ...) if the join fails.
        string joinFailReason = null;
        void CaptureJoinFail(string r)
        {
            // A rejected second join attempt doesn't mean this invite join failed.
            if (r != JoinFailInProgress)
                joinFailReason ??= r;
        }
        OnJoinFailed += CaptureJoinFail;
        try
        {
            await JoinLobbyFromInvite(lobbyId);
        }
        finally
        {
            OnJoinFailed -= CaptureJoinFail;
        }

        if (_launchInviteResolved)
            return;

        if (joinFailReason != null)
        {
            await FailLaunchInvite(joinFailReason);
            return;
        }

        // The host routes us (OnClientConnected → ready-up screen or gameplay). Wait for that;
        // fail if the connection drops (e.g. approval rejected) or the host never responds.
        waited = 0f;
        while (waited < LaunchInviteJoinTimeoutSeconds)
        {
            if (this == null || _launchInviteResolved)
                return;

            MainMenuController menu = MainMenuController.Instance;
            if (menu != null && menu.IsInLobbyOrGame)
            {
                _launchInviteResolved = true;
                Debug.Log("[LobbyManager] Launch invite join succeeded.");
                return;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient)
            {
                string reason = nm != null && !string.IsNullOrEmpty(nm.DisconnectReason)
                    ? nm.DisconnectReason
                    : LaunchInviteDisconnectedReason;
                await FailLaunchInvite(reason);
                return;
            }

            if (CurrentLobby.Id.Value != lobbyId)
            {
                await FailLaunchInvite(LaunchInviteDisconnectedReason);
                return;
            }

            await Task.Delay(100);
            waited += 0.1f;
        }

        await FailLaunchInvite(LaunchInviteTimeoutReason);
    }

    private static ulong ParseConnectLobbyArg(string[] args)
    {
        if (args == null)
            return 0;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], ConnectLobbyLaunchArg, StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(args[i + 1], out ulong id))
                return id;
        }

        return 0;
    }

    private void Update()
    {
        SteamClient.RunCallbacks();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        ShutdownNetworkSessionImmediate();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
            if (NetworkManager.Singleton.ConnectionApprovalCallback == ApproveConnection)
                NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }

        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyMemberDataChanged -= OnLobbyMemberDataChanged;
        SteamMatchmaking.OnLobbyDataChanged -= OnLobbyDataChanged;
        SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        SteamFriends.OnGameOverlayActivated -= OnOverlayToggled;

        if (SteamClient.IsValid)
            SteamClient.Shutdown();

        Instance = null;
    }

    private void OnDisable()
    {
        if (!Application.isPlaying || Instance != this)
            return;

        ShutdownNetworkSessionImmediate();
    }

    private void OnOverlayToggled(bool isActive)
    {
        Debug.Log($"Steam overlay active: {isActive}");

        // Only react if we previously opened the invite overlay ourselves.
        if (!inviteOverlayWasOpenedByUs)
            return;

        if (isActive)
        {
            inviteOverlayActivated = true;
            return;
        }

        inviteOverlayWasOpenedByUs = false;
        inviteOverlayActivated = false;
        CloseInviteFriendsPopUp();
    }

    /// <summary>Visible player cap. The Steam lobby itself gets extra hidden developer slot(s).</summary>
    private const int MaxLobbyMembers = DevSpectatorConfig.MaxPlayers;
    private const int SteamLobbyCapacity = DevSpectatorConfig.MaxPlayers + DevSpectatorConfig.ReservedDevSlots;

    /// <summary>True while the local machine is joining/connected as a hidden developer spectator.</summary>
    public bool IsDevSpectating => DevSpectatorRegistry.IsLocalSpectating;

    private const string LobbyDataKeyJoinCode = "join_code";
    private const int JoinCodeLength = 6;
    private const string JoinCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private string _localJoinCode;

    // Public/private is lobby metadata, NOT Steam's lobby type: the Steam lobby must stay
    // searchable (SetPublic) because join-by-code finds lobbies through the lobby list. The
    // public lobby browser only lists lobbies whose "visibility" is "public".
    private const string LobbyDataKeyVisibility = "visibility";
    private const string VisibilityPublic = "public";
    private const string VisibilityPrivate = "private";
    private const int PublicLobbyMaxResults = 100;

    // Build version the host is running; the browser greys out lobbies from other builds.
    private const string LobbyDataKeyVersion = "version";

    /// <summary>Build version a lobby's host is running (empty for lobbies created before it was published).</summary>
    public static string GetLobbyVersion(Lobby lobby) => lobby.GetData(LobbyDataKeyVersion);

    /// <summary>True when a browsed lobby is from the same build as the local game (or predates version tagging).</summary>
    public static bool IsLobbyVersionCompatible(Lobby lobby)
    {
        string version = GetLobbyVersion(lobby);
        return string.IsNullOrEmpty(version) || version == Application.version;
    }

    // Host-side cache so the toggle reads back instantly, before Steam echoes the data change.
    private bool _hostLobbyIsPublic;

    private bool IsLocalLobbyOwner =>
        CurrentLobby.Id != 0 && SteamClient.IsValid && CurrentLobby.Owner.Id == SteamClient.SteamId;

    /// <summary>True when the active Steam lobby is listed in the public lobby browser.</summary>
    public bool IsLobbyPublic
    {
        get
        {
            if (CurrentLobby.Id == 0 || !SteamClient.IsValid) return false;
            if (IsLocalLobbyOwner) return _hostLobbyIsPublic;
            return CurrentLobby.GetData(LobbyDataKeyVisibility) == VisibilityPublic;
        }
    }

    /// <summary>
    /// Only the host can change public/private. Lobby ownership alone isn't enough: when the host
    /// drops, Steam hands ownership to another member who isn't running the session.
    /// </summary>
    public bool CanChangeLobbyVisibility =>
        IsLocalLobbyOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    /// <summary>
    /// Host only: lists or unlists the lobby in the public browser. Join codes and Steam invites
    /// keep working either way. Returns false when the local player isn't the host.
    /// </summary>
    public bool SetLobbyPublic(bool isPublic)
    {
        if (!CanChangeLobbyVisibility)
        {
            Debug.LogWarning("[LobbyManager] Only the host can change lobby visibility.");
            return false;
        }

        _hostLobbyIsPublic = isPublic;
        CurrentLobby.SetData(LobbyDataKeyVisibility, isPublic ? VisibilityPublic : VisibilityPrivate);
        Debug.Log($"[LobbyManager] Lobby {CurrentLobby.Id} is now {(isPublic ? "public" : "private")}.");
        OnLobbyUpdated?.Invoke();
        return true;
    }

    /// <summary>
    /// Requests every public lobby worldwide for the Join Game browser, excluding the local
    /// player's own lobby and lobbies whose visible player cap is already met.
    /// </summary>
    public async Task<Lobby[]> RequestPublicLobbiesAsync()
    {
        if (!SteamClient.IsValid || !(NetworkManager.Singleton?.NetworkConfig.NetworkTransport is FacepunchTransport))
            return Array.Empty<Lobby>();

        Lobby[] lobbies = await SteamMatchmaking.LobbyList
            .FilterDistanceWorldwide()
            .WithKeyValue(LobbyDataKeyVisibility, VisibilityPublic)
            .WithMaxResults(PublicLobbyMaxResults)
            .RequestAsync();

        if (lobbies == null)
            return Array.Empty<Lobby>();

        ulong ownLobby = CurrentLobby.Id.Value;
        return lobbies.Where(l => l.Id.Value != ownLobby && !IsBrowserLobbyFull(l)).ToArray();
    }

    /// <summary>Visible-player-cap check for a lobby seen from outside (no member data available).</summary>
    private static bool IsBrowserLobbyFull(Lobby lobby)
    {
        int count = int.TryParse(lobby.GetData(DevSpectatorConfig.LobbyDataPlayerCount), out int c) ? c : lobby.MemberCount;
        int max = int.TryParse(lobby.GetData(DevSpectatorConfig.LobbyDataMaxPlayers), out int m) ? m : MaxLobbyMembers;
        return count >= max;
    }

    /// <summary>Joins a lobby picked from the public lobby browser. Fires <see cref="OnJoinFailed"/> on failure.</summary>
    public async void JoinPublicLobby(ulong lobbyId)
    {
        if (NetworkManager.Singleton == null || lobbyId == 0)
            return;

        if (!TryBeginJoin(out int requestId))
            return;

        string blocker = SteamJoinBlocker();
        if (blocker != null)
        {
            await FailJoin(requestId, blocker);
            return;
        }

        if (CurrentLobby.Id.Value == lobbyId)
        {
            await FailJoin(requestId, JoinFailAlreadyInLobby);
            return;
        }

        // Same as JoinLobbyByCode: leave any lingering lobby/session so StartClient isn't skipped.
        if (CurrentLobby.Id != 0 || NetworkManager.Singleton.IsListening)
            await ExitLobbyAsync();
        if (!IsJoinCurrent(requestId))
            return;

        DevSpectatorRegistry.IsLocalSpectating = false;
        Debug.Log($"[JoinPublicLobby] Joining public lobby {lobbyId}.");
        await JoinLobbyInternal(requestId, lobbyId, "127.0.0.1");
    }

    /// <summary>
    /// The active lobby's join code. Only the host generates the code locally; everyone else
    /// (code/invite/browser joins) reads it from the Steam lobby's "join_code" metadata.
    /// </summary>
    public string CurrentJoinCode
    {
        get
        {
            if (!string.IsNullOrEmpty(_localJoinCode))
                return _localJoinCode;

            if (CurrentLobby.Id == 0 || !SteamClient.IsValid)
                return null;

            string lobbyCode = CurrentLobby.GetData(LobbyDataKeyJoinCode);
            return string.IsNullOrEmpty(lobbyCode) ? null : lobbyCode;
        }
        private set => _localJoinCode = value;
    }

    /// <summary>Generates a random uppercase alphanumeric join code.</summary>
    private static string GenerateJoinCode()
    {
        var result = new System.Text.StringBuilder(JoinCodeLength);
        for (int i = 0; i < JoinCodeLength; i++)
            result.Append(JoinCodeChars[UnityEngine.Random.Range(0, JoinCodeChars.Length)]);
        return result.ToString();
    }

    // =========================
    // HOST
    // =========================

    /// <summary>
    /// Creates a lobby and starts the NGO host. Returns true on success, false on failure.
    /// Awaiting this ensures NetworkManager is fully started before any RPC calls are made.
    /// </summary>
    public async Task<bool> CreateLobby()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[CreateLobby] NetworkManager.Singleton is null.");
            return false;
        }

        await ExitLobbyAsync();

        DevSpectatorRegistry.IsLocalSpectating = false;
        DevSpectatorRegistry.ClearServerState();
        PlayerSessionIdentity.ClearServerState();
        NetworkManager.Singleton.NetworkConfig.ConnectionData = DevSpectatorConfig.BuildConnectionPayload(false);

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        Debug.Log($"[CreateLobby] Transport type: {(transport != null ? transport.GetType().Name : "null")}");

        if (transport is FacepunchTransport facepunch)
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("[CreateLobby] Steam is not initialized — cannot create lobby.");
                return false;
            }
            // Steam can be initialized while the client is disconnected from Steam's servers.
            // CreateLobbyAsync then times out (~10s per attempt) and the retry loop below would
            // leave the player on a black fade for close to a minute. Fail fast instead.
            if (!SteamClient.IsLoggedOn)
            {
                Debug.LogError("[CreateLobby] Steam client is not connected to Steam servers (offline or reconnecting) — cannot create lobby.");
                return false;
            }



            Debug.Log("[CreateLobby] FacepunchTransport detected — creating Steam lobby...");

            if (!await TryCreateSteamLobbyAsync(facepunch))
                return false;

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                Debug.Log("[CreateLobby] Calling StartHost...");
                if (!NetworkManager.Singleton.StartHost())
                {
                    Debug.LogError("[CreateLobby] Failed to start NGO host.");
                    CurrentLobby.Leave();
                    CurrentLobby = default;
                    return false;
                }
                Debug.Log("[CreateLobby] StartHost succeeded.");
            }
            else
            {
                Debug.Log($"[CreateLobby] Skipping StartHost — already IsHost={NetworkManager.Singleton.IsHost} IsServer={NetworkManager.Singleton.IsServer}");
            }

            OnLobbyUpdated?.Invoke();
            Debug.Log($"[CreateLobby] Done. Lobby: {CurrentLobby.Id}");
            return true;
        }
        else if (transport is UnityTransport)
        {
            Debug.Log("Starting LAN Host via UnityTransport");

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                if (!NetworkManager.Singleton.StartHost())
                {
                    Debug.LogError("[CreateLobby] Failed to start LAN host.");
                    return false;
                }
            }

            return true;
        }

        Debug.LogError($"[CreateLobby] No matching transport path — transport is {(transport != null ? transport.GetType().Name : "null")}.");
        return false;
    }

    /// <summary>
    /// Creates and configures a Steam lobby (searchable, joinable, join code, player cap, private
    /// visibility by default) and points the
    /// Facepunch transport at the local host. Does NOT touch the NGO session — callers decide whether
    /// to start a host afterwards. Shared by <see cref="CreateLobby"/> and
    /// <see cref="CreateLobbyForRunningHost"/>.
    /// </summary>
    private async Task<bool> TryCreateSteamLobbyAsync(FacepunchTransport facepunch)
    {
        const int MaxLobbyCreateAttempts = 5;
        const float LobbyCreateRetryDelaySeconds = 1f;
        Steamworks.Data.Lobby? createdLobby = null;

        for (int attempt = 1; attempt <= MaxLobbyCreateAttempts; attempt++)
        {
            createdLobby = await SteamMatchmaking.CreateLobbyAsync(SteamLobbyCapacity);
            if (createdLobby.HasValue)
                break;

            Debug.LogWarning($"[CreateLobby] Attempt {attempt}/{MaxLobbyCreateAttempts} failed — retrying in {LobbyCreateRetryDelaySeconds}s...");
            await Task.Delay(TimeSpan.FromSeconds(LobbyCreateRetryDelaySeconds));
        }

        if (!createdLobby.HasValue)
        {
            Debug.LogError("[CreateLobby] Failed to create Steam lobby after all attempts.");
            return false;
        }

        CurrentLobby = createdLobby.Value;
        CurrentLobby.SetPublic();
        CurrentLobby.SetJoinable(true);
        CurrentLobby.SetData("host", SteamClient.Name);
        CurrentLobby.SetData(LobbyDataKeyVersion, Application.version);

        CurrentLobby.SetMemberData(DevSpectatorConfig.MemberRoleKey, DevSpectatorConfig.RolePlayer);
        CurrentLobby.SetData(DevSpectatorConfig.LobbyDataMaxPlayers, MaxLobbyMembers.ToString());
        PublishVisiblePlayerCount();

        // New lobbies are private (code/invite only) until the host lists them publicly.
        _hostLobbyIsPublic = false;
        CurrentLobby.SetData(LobbyDataKeyVisibility, VisibilityPrivate);

        CurrentJoinCode = GenerateJoinCode();
        CurrentLobby.SetData(LobbyDataKeyJoinCode, CurrentJoinCode);
        Debug.Log($"[CreateLobby] Join code: {CurrentJoinCode}");

        // Host targets self for Facepunch transport.
        facepunch.targetSteamId = SteamClient.SteamId;
        Debug.Log($"[CreateLobby] Steam lobby created: {CurrentLobby.Id}, targetSteamId={facepunch.targetSteamId}");
        return true;
    }

    /// <summary>
    /// Attaches a Steam lobby to an NGO session that is ALREADY running, without shutting it down.
    /// Used by the in-game invite payphone: <see cref="CreateLobby"/> starts with
    /// <see cref="ExitLobbyAsync"/>, which shuts down NetworkManager and despawns the local player
    /// (and its camera) mid-game, leaving the player stuck on a dark screen with no way back.
    /// Only a Facepunch host can do this; clients and LAN (UnityTransport) sessions return false.
    /// </summary>
    private async Task<bool> CreateLobbyForRunningHost()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost)
        {
            Debug.LogWarning("[CreateLobbyForRunningHost] Only the host can create a lobby for a running session.");
            return false;
        }

        if (!(nm.NetworkConfig.NetworkTransport is FacepunchTransport facepunch))
        {
            Debug.LogWarning("[CreateLobbyForRunningHost] Running session is not using the Steam transport — friends couldn't connect via a Steam lobby.");
            return false;
        }

        if (!SteamClient.IsValid || !SteamClient.IsLoggedOn)
        {
            Debug.LogWarning("[CreateLobbyForRunningHost] Steam is not initialized or not connected to Steam servers.");
            return false;
        }

        if (!await TryCreateSteamLobbyAsync(facepunch))
            return false;

        OnLobbyUpdated?.Invoke();
        return true;
    }

    // =========================
    // CLIENT
    // =========================

    /// <summary>Joins a Steam lobby by ID (Facepunch transport) or connects to a LAN host at 127.0.0.1 (UnityTransport).
    /// Pass lobbyId = 0 when using UnityTransport to fall through to the LAN path.</summary>
    public async void JoinLobby(ulong lobbyId)
    {
        if (!TryBeginJoin(out int requestId))
            return;

        // A regular join (invite, browser, code) always drops any previous spectator state.
        DevSpectatorRegistry.IsLocalSpectating = false;
        await JoinLobbyInternal(requestId, lobbyId, "127.0.0.1");
    }

    /// <summary>
    /// DEVELOPER ONLY: joins a Steam lobby as a hidden spectator. The host approves the NGO
    /// connection without spawning a player object, other players never see this member in
    /// the lobby UI, and the reserved developer slot is used so full lobbies can be joined.
    /// Returns false if the local user is not whitelisted or the join fails.
    /// </summary>
    public async Task<bool> JoinLobbyAsDevSpectator(ulong lobbyId)
    {
        if (!DevSpectatorConfig.IsLocalDev)
        {
            Debug.LogWarning("[DevSpectator] Local Steam user is not whitelisted in DevSpectatorConfig.DevSteamIds.");
            return false;
        }

        if (!(NetworkManager.Singleton?.NetworkConfig.NetworkTransport is FacepunchTransport))
        {
            Debug.LogWarning("[DevSpectator] Spectating requires the Facepunch (Steam) transport.");
            return false;
        }

        if (!TryBeginJoin(out int requestId))
            return false;

        await ExitLobbyAsync();

        DevSpectatorRegistry.IsLocalSpectating = true;
        await JoinLobbyInternal(requestId, lobbyId, "127.0.0.1");

        bool joined = CurrentLobby.Id.Value == lobbyId;
        if (!joined)
            DevSpectatorRegistry.IsLocalSpectating = false;
        return joined;
    }

    /// <summary>Joins a LAN host at the given IP address using UnityTransport.
    /// Ignored when FacepunchTransport is the active transport.</summary>
    public async void JoinLobbyLAN(string address)
    {
        if (!TryBeginJoin(out int requestId))
            return;

        await JoinLobbyInternal(requestId, 0, address);
    }

    /// <summary>
    /// Searches public Steam lobbies for one whose join_code metadata matches <paramref name="code"/>
    /// and joins it. Fires <see cref="OnJoinFailed"/> if no matching lobby is found.
    /// Only valid when FacepunchTransport is active.
    /// </summary>
    public async void JoinLobbyByCode(string code)
    {
        if (NetworkManager.Singleton == null)
            return;

        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (!TryBeginJoin(out int requestId))
            return;

        string blocker = SteamJoinBlocker();
        if (blocker != null)
        {
            Debug.LogError($"[JoinLobbyByCode] Steam is not ready ({blocker}) — cannot search for lobbies.");
            await FailJoin(requestId, blocker);
            return;
        }

        // The host typing their own code would otherwise leave their own lobby and then fail to rejoin it.
        if (CurrentLobby.Id != 0 && string.Equals(CurrentJoinCode, normalizedCode, StringComparison.Ordinal))
        {
            await FailJoin(requestId, JoinFailAlreadyInLobby);
            return;
        }

        Debug.Log($"[JoinLobbyByCode] Searching for lobby with join code: {normalizedCode} (AppId={SteamClient.AppId.Value})");

        // Steam's lobby search is (a) region-limited by default, so friends in other regions
        // were never found, and (b) eventually consistent: a freshly created lobby's join_code
        // metadata can take a few seconds to become searchable. That is why joining on the
        // pre-game campaign screen (right after the host created the lobby) failed while
        // later joins succeeded. Search worldwide and retry briefly before giving up.
        const int MaxSearchAttempts = 4;
        const float SearchRetryDelaySeconds = 1.5f;
        Lobby[] lobbies = null;

        for (int attempt = 1; attempt <= MaxSearchAttempts; attempt++)
        {
            lobbies = await SteamMatchmaking.LobbyList
                .FilterDistanceWorldwide()
                .WithKeyValue(LobbyDataKeyJoinCode, normalizedCode)
                .RequestAsync();

            // Player backed out (or another join replaced this one) while Steam was searching.
            if (!IsJoinCurrent(requestId))
                return;

            // Never "find" the lobby we're already in.
            if (lobbies != null)
                lobbies = lobbies.Where(l => l.Id != CurrentLobby.Id || CurrentLobby.Id == 0).ToArray();

            if (lobbies != null && lobbies.Length > 0)
                break;

            if (attempt < MaxSearchAttempts)
            {
                Debug.LogWarning($"[JoinLobbyByCode] Attempt {attempt}/{MaxSearchAttempts}: no lobby for '{normalizedCode}' yet — retrying in {SearchRetryDelaySeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(SearchRetryDelaySeconds));
                if (!IsJoinCurrent(requestId))
                    return;
            }
        }

        if (lobbies == null || lobbies.Length == 0)
        {
            Debug.LogWarning($"[JoinLobbyByCode] No lobby found for code '{normalizedCode}'. " +
                             $"If the host is definitely online, confirm both players run the game under the same Steam AppId (local={SteamClient.AppId.Value}).");
            await FailJoin(requestId, JoinFailCodeNotFound);
            return;
        }

        // Leave any lobby/session we're still in (e.g. after backing out of hosting),
        // otherwise StartClient would be skipped because NetworkManager is still running.
        if (CurrentLobby.Id != 0 || NetworkManager.Singleton.IsListening)
            await ExitLobbyAsync();
        if (!IsJoinCurrent(requestId))
            return;

        DevSpectatorRegistry.IsLocalSpectating = false;

        // Codes are unique per active session, but a stale lobby from an earlier session can
        // linger in search results; prefer one that isn't full.
        var target = lobbies.FirstOrDefault(l => !IsBrowserLobbyFull(l));
        if (target.Id == 0)
            target = lobbies[0];
        Debug.Log($"[JoinLobbyByCode] Found lobby {target.Id} for code '{normalizedCode}'.");
        await JoinLobbyInternal(requestId, target.Id, "127.0.0.1");
    }

    private async Task JoinLobbyInternal(int requestId, ulong lobbyId, string lanAddress)
    {
        if (NetworkManager.Singleton == null)
        {
            await FailJoin(requestId, JoinFailError);
            return;
        }

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport)
        {
            // Joining a lobby you (or another local session) just left can race Steam's
            // backend: Lobby.Leave() takes effect locally immediately, but the matchmaking
            // servers process the membership change asynchronously. A rejoin attempted in
            // that window can spuriously fail (e.g. RoomEnter.Error) even though the lobby
            // still exists, surfacing as "Lobby not found" to the player. Retry a few times
            // with a short backoff instead of failing on the first attempt.
            const int MaxJoinAttempts = 4;
            const float JoinRetryDelaySeconds = 0.75f;

            var lobby = new Lobby(lobbyId);
            RoomEnter joinResult = RoomEnter.Error;

            for (int attempt = 1; attempt <= MaxJoinAttempts; attempt++)
            {
                joinResult = await lobby.Join();

                // Player backed out while Steam was processing the join: don't keep the seat.
                if (!IsJoinCurrent(requestId))
                {
                    if (joinResult == RoomEnter.Success)
                        LeaveRejectedLobby(lobby);
                    return;
                }

                if (joinResult == RoomEnter.Success)
                    break;

                // Only transient results are worth retrying; Full, Banned, NotAllowed... won't change.
                bool retryable = joinResult == RoomEnter.Error || joinResult == RoomEnter.DoesntExist;
                Debug.LogWarning($"[JoinLobby] Attempt {attempt}/{MaxJoinAttempts} failed to join lobby {lobbyId}. Result: {joinResult}" +
                    (retryable && attempt < MaxJoinAttempts ? $" — retrying in {JoinRetryDelaySeconds}s..." : ""));

                if (!retryable)
                    break;

                if (attempt < MaxJoinAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(JoinRetryDelaySeconds));
                    if (!IsJoinCurrent(requestId))
                        return;
                }
            }

            if (joinResult != RoomEnter.Success)
            {
                Debug.LogError($"[JoinLobby] Failed to join lobby {lobbyId}. Result: {joinResult}");
                await FailJoin(requestId, joinResult.ToString());
                return;
            }

            bool asSpectator = DevSpectatorRegistry.IsLocalSpectating;

            // Abandoned lobby: the host is gone and Steam made us the owner of what's left.
            // Nobody is running a session to connect to.
            if (SteamClient.IsValid && lobby.Owner.Id == SteamClient.SteamId)
            {
                Debug.LogWarning($"[JoinLobby] Lobby {lobbyId} has no host (we became its owner) — leaving.");
                LeaveRejectedLobby(lobby);
                await FailJoin(requestId, JoinFailAbandoned);
                return;
            }

            // Different builds can't connect (NGO rejects the config hash), so stop here with a clear
            // reason instead of a generic connection failure. Code and invite joins never saw the
            // browser's version check.
            if (!IsLobbyVersionCompatible(lobby))
            {
                Debug.LogWarning($"[JoinLobby] Lobby {lobbyId} runs version '{GetLobbyVersion(lobby)}', local is '{Application.version}' — leaving.");
                LeaveRejectedLobby(lobby);
                await FailJoin(requestId, JoinFailVersionMismatch);
                return;
            }

            // Publish our role immediately so every other member can filter hidden spectators.
            lobby.SetMemberData(DevSpectatorConfig.MemberRoleKey,
                asSpectator ? DevSpectatorConfig.RoleSpectator : DevSpectatorConfig.RolePlayer);

            // The Steam lobby has hidden developer slot(s) on top of the visible player cap.
            // Regular players must never occupy them: leave if the visible cap is already met.
            if (!asSpectator && DevSpectatorConfig.CountVisibleMembers(lobby) > MaxLobbyMembers)
            {
                Debug.LogWarning($"[JoinLobby] Lobby {lobbyId} is full ({MaxLobbyMembers} players) — leaving.");
                LeaveRejectedLobby(lobby);
                await FailJoin(requestId, JoinFailLobbyFull);
                return;
            }

            _rejectedFullLobbyId = 0;

            CurrentLobby = lobby;

            if (facepunchTransport == null)
            {
                Debug.LogError("[JoinLobby] facepunchTransport is null — cannot connect.");
                await FailJoin(requestId, JoinFailError, leaveSession: true);
                return;
            }

            NetworkManager.Singleton.NetworkConfig.ConnectionData = DevSpectatorConfig.BuildConnectionPayload(asSpectator);

            facepunchTransport.targetSteamId = CurrentLobby.Owner.Id;
            Debug.Log($"[JoinLobby] Steam join success. targetSteamId={facepunchTransport.targetSteamId}");

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                // Hidden dev spectators drive their own setup and never get the late-join
                // bootstrap RPC that dismisses the loader, so only players see it.
                if (!asSpectator)
                    JoinLoaderOverlay.Show();

                bool started = NetworkManager.Singleton.StartClient();
                Debug.Log($"[JoinLobby] StartClient={started}");

                if (!started)
                {
                    JoinLoaderOverlay.Cancel();
                    await FailJoin(requestId, JoinFailError, leaveSession: true);
                    return;
                }
            }
            else
            {
                Debug.LogWarning($"[JoinLobby] Skipping StartClient — already IsClient={NetworkManager.Singleton.IsClient} IsHost={NetworkManager.Singleton.IsHost}");
            }

            // Spectators run their own tooling flow; the hidden join needs no player feedback.
            if (asSpectator)
                EndJoinQuietly(requestId);
            else
                _ = WatchJoinCompletion(requestId, lobbyId);
        }
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData(lanAddress, 7777);
            NetworkManager.Singleton.NetworkConfig.ConnectionData = DevSpectatorConfig.BuildConnectionPayload(false);
            Debug.Log($"[JoinLobby] Connecting to LAN host at {lanAddress}:7777");

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                JoinLoaderOverlay.Show();
                if (!NetworkManager.Singleton.StartClient())
                {
                    JoinLoaderOverlay.Cancel();
                    await FailJoin(requestId, JoinFailError, leaveSession: true);
                    return;
                }
            }

            _ = WatchJoinCompletion(requestId, 0);
        }
        else
        {
            await FailJoin(requestId, JoinFailError);
        }
    }

    // =========================
    // JOIN STATE
    // =========================

    // One join at a time. Every awaited step re-checks the request id, so a join the player
    // cancelled (or that was replaced) can't pull them into a lobby afterwards.
    private int _joinRequestId;
    private bool _joinInProgress;

    /// <summary>Give up if the host never accepts the connection within this time.</summary>
    private const float JoinConnectTimeoutSeconds = 25f;

    /// <summary>Stop watching once connected for this long; the join loader's own safety net takes over.</summary>
    private const float JoinSyncTimeoutSeconds = 240f;

    /// <summary>
    /// True from the moment a join starts until it reaches the lobby/game, fails, or is cancelled.
    /// <see cref="ConnectionLossHandler"/> ignores disconnects meanwhile: a failed join is reported
    /// with its real reason here, not as "Lost connection" plus a scene reload.
    /// </summary>
    public static bool IsJoinPending => Instance != null && Instance._joinInProgress;

    private bool TryBeginJoin(out int requestId)
    {
        requestId = 0;

        // Safety net: an exception inside a join task must not lock out every later join.
        if (_joinInProgress && Time.realtimeSinceStartup - _joinStartedAt > JoinSyncTimeoutSeconds + 30f)
        {
            Debug.LogWarning("[LobbyManager] Previous join never finished — releasing it.");
            _joinInProgress = false;
        }

        if (_joinInProgress)
        {
            Debug.LogWarning("[LobbyManager] A join is already in progress — ignoring the new request.");
            ReportJoinFailure(JoinFailInProgress);
            return false;
        }

        _joinInProgress = true;
        _joinStartedAt = Time.realtimeSinceStartup;
        requestId = ++_joinRequestId;
        return true;
    }

    private float _joinStartedAt;

    private bool IsJoinCurrent(int requestId) => this != null && _joinInProgress && requestId == _joinRequestId;

    /// <summary>Null when Steam can be used for a join, otherwise the failure reason.</summary>
    private static string SteamJoinBlocker()
    {
        if (!SteamClient.IsValid) return JoinFailSteamNotReady;
        if (!SteamClient.IsLoggedOn) return JoinFailSteamOffline;
        return null;
    }

    /// <summary>Leaves a Steam lobby we entered but won't stay in, and ignores its late enter callback.</summary>
    private void LeaveRejectedLobby(Lobby lobby)
    {
        _rejectedFullLobbyId = lobby.Id.Value;
        lobby.Leave();
        if (CurrentLobby.Id == lobby.Id)
            CurrentLobby = default;
    }

    /// <summary>
    /// Ends a join as failed and reports why. <paramref name="leaveSession"/> tears down the Steam
    /// lobby / NGO client this join already started (flagged intentional, so it isn't a lost connection).
    /// </summary>
    private async Task FailJoin(int requestId, string reason, bool leaveSession = false)
    {
        if (!IsJoinCurrent(requestId))
            return;

        _joinInProgress = false;
        _joinRequestId++;
        Debug.LogWarning($"[LobbyManager] Join failed: {reason}");

        JoinLoaderOverlay.Cancel();
        if (leaveSession && (CurrentLobby.Id != 0 || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)))
            await ExitLobbyAsync();

        ReportJoinFailure(reason);
    }

    private void ReportJoinFailure(string reason)
    {
        OnJoinFailed?.Invoke(reason);

        // The join screen shows the reason in its status line; anywhere else (invite accepted from
        // the home screen, pre-game lobby, ...) needs a toast or the player never learns why.
        if (!JoinLobbyScreen.IsOpen)
            LobbyToast.Error(Loc.T(DescribeJoinFailure(reason)));
    }

    private void CompleteJoin(int requestId)
    {
        if (!IsJoinCurrent(requestId))
            return;

        _joinInProgress = false;

        string host = CurrentLobby.Id != 0 ? CurrentLobby.GetData("host") : null;
        LobbyToast.Success(string.IsNullOrWhiteSpace(host)
            ? Loc.T("Joined the lobby.")
            // The Steam name must never go through localization.
            : Loc.Verbatim(Loc.T("Joined {0}'s lobby.", host)));
    }

    private void EndJoinQuietly(int requestId)
    {
        if (IsJoinCurrent(requestId))
            _joinInProgress = false;
    }

    /// <summary>
    /// Player backed out of the join screen: abandons whatever join is in progress (code search,
    /// Steam join or NGO connection) and leaves anything it already joined.
    /// </summary>
    public async void CancelPendingJoin()
    {
        bool wasLaunchInvite = IsLaunchInviteJoinPending;
        if (wasLaunchInvite)
            CancelLaunchInviteJoin(); // leaves the session itself

        if (!_joinInProgress)
            return;

        _joinInProgress = false;
        _joinRequestId++;
        Debug.Log("[LobbyManager] Pending join cancelled by player.");

        JoinLoaderOverlay.Cancel();
        if (!wasLaunchInvite && (CurrentLobby.Id != 0 || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)))
            await ExitLobbyAsync();
    }

    /// <summary>
    /// After StartClient: waits for the host to route us to the ready-up screen or into gameplay.
    /// Reports approval rejections (full), hosts that never answer, and drops mid-handshake.
    /// </summary>
    private async Task WatchJoinCompletion(int requestId, ulong lobbyId)
    {
        // Cold-launch invites have their own watchdog (TryJoinLobbyFromLaunchArgs) that reports
        // failures on the join screen; only add the success toast for them.
        bool launchInvite = IsLaunchInviteJoinPending;
        bool everConnected = false;
        float waited = 0f;

        while (true)
        {
            await Task.Delay(100);
            waited += 0.1f;

            if (!IsJoinCurrent(requestId))
                return;

            if (launchInvite)
            {
                if (!_launchInviteResolved && waited < JoinSyncTimeoutSeconds)
                    continue;

                if (HasReachedLobbyOrGame())
                    CompleteJoin(requestId);
                else
                    EndJoinQuietly(requestId);
                return;
            }

            if (HasReachedLobbyOrGame())
            {
                CompleteJoin(requestId);
                return;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsClient)
            {
                // NGO's approval reason ("LobbyFull", ...) when the host rejected us.
                string reason = nm != null && !string.IsNullOrEmpty(nm.DisconnectReason)
                    ? nm.DisconnectReason
                    : everConnected ? JoinFailDisconnected : JoinFailHostUnreachable;
                await FailJoin(requestId, reason, leaveSession: true);
                return;
            }

            if (nm.IsConnectedClient)
                everConnected = true;

            if (lobbyId != 0 && CurrentLobby.Id.Value != lobbyId)
            {
                await FailJoin(requestId, JoinFailDisconnected, leaveSession: true);
                return;
            }

            if (!everConnected && waited > JoinConnectTimeoutSeconds)
            {
                await FailJoin(requestId, JoinFailTimeout, leaveSession: true);
                return;
            }

            if (waited > JoinSyncTimeoutSeconds)
            {
                Debug.LogWarning("[LobbyManager] Join still synchronizing after the watch window — no longer tracking it.");
                EndJoinQuietly(requestId);
                return;
            }
        }
    }

    /// <summary>The host routed us: ready-up screen open, or the menu hidden for gameplay.</summary>
    private static bool HasReachedLobbyOrGame()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient)
            return false;

        MainMenuController menu = MainMenuController.Instance;
        if (menu != null)
            return menu.IsInLobbyOrGame;

        return nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
    }

    // =========================
    // INVITES
    // =========================

    public async void OpenInviteFriendsPopup()
    {
        int requestId = ++inviteRequestId;
        inviteOverlayWasOpenedByUs = false;
        inviteOverlayActivated = false;

        // SteamUtils/SteamFriends dereference Facepunch's native interfaces, which are null until
        // SteamClient.Init succeeds (Steam not running, not logged in, Editor without Steam), so
        // reading SteamUtils.IsOverlayEnabled would throw a NullReferenceException.
        if (!SteamClient.IsValid)
        {
            Debug.LogWarning("[OpenInviteFriendsPopup] Steam client is not initialized — cannot open the invite overlay.");
            CloseInviteFriendsPopUp(InviteSteamNotRunningMessage);
            return;
        }

        // The Steam overlay only renders when this game is running as a real build
        // launched through the Steam client (it never renders inside the Unity
        // Editor's Game View). If it isn't available, OpenGameInviteOverlay() is a
        // silent no-op — OnGameOverlayActivated never fires, and the caller-side
        // panel would otherwise stay open forever with no way to dismiss it. Bail
        // out up front so the UI never gets stuck in that state.
        bool overlayEnabled;
        try
        {
            overlayEnabled = SteamUtils.IsOverlayEnabled;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OpenInviteFriendsPopup] Failed to query Steam overlay state: {e.Message}");
            overlayEnabled = false;
        }

        if (!overlayEnabled)
        {
            Debug.LogWarning("[OpenInviteFriendsPopup] Steam overlay is not enabled/available " +
                "(e.g. running in the Editor, or Overlay disabled in Steam settings). " +
                "Cannot open the invite overlay.");
            CloseInviteFriendsPopUp(InviteOverlayUnavailableMessage);
            return;
        }

        if (CurrentLobby.Id == 0)
        {
            // No lobby yet (e.g. the scene was entered directly instead of via the
            // normal host flow, or the session was started offline/LAN). If a network
            // session is already running, NEVER call CreateLobby(): it begins with
            // ExitLobbyAsync(), which shuts NetworkManager down and despawns the local
            // player and camera mid-game. Attach a lobby to the running host instead.
            Debug.LogWarning("[OpenInviteFriendsPopup] No active Steam lobby — creating one now.");
            NetworkManager nm = NetworkManager.Singleton;
            bool sessionRunning = nm != null && (nm.IsListening || nm.IsHost || nm.IsServer || nm.IsClient);
            bool created = sessionRunning ? await CreateLobbyForRunningHost() : await CreateLobby();
            if (requestId != inviteRequestId)
                return; // Cancelled (Escape) or superseded while the lobby was being created.
            if (!created)
            {
                Debug.LogError("[OpenInviteFriendsPopup] Failed to create a lobby — cannot open invite overlay.");
                CloseInviteFriendsPopUp(InviteLobbyFailedMessage);
                return;
            }
        }

        inviteOverlayWasOpenedByUs = true;
        try
        {
            SteamFriends.OpenGameInviteOverlay(CurrentLobby.Id);
        }
        catch (Exception e)
        {
            Debug.LogError($"[OpenInviteFriendsPopup] OpenGameInviteOverlay threw: {e.Message}");
            inviteOverlayWasOpenedByUs = false;
            CloseInviteFriendsPopUp(InviteLobbyFailedMessage);
            return;
        }
        Debug.Log($"Opened Steam invite popup for lobby {CurrentLobby.Id}");

        // Safety net: if the overlay never actually reports as opened within a few
        // seconds (overlay hook failed, big-picture mode issue, etc.), auto-close
        // the panel instead of leaving the player stuck on a dark screen forever.
        _ = WatchdogCloseInviteIfOverlayNeverOpens(requestId);
    }

    private async Task WatchdogCloseInviteIfOverlayNeverOpens(int requestId)
    {
        await Task.Delay(4000);
        if (this == null || requestId != inviteRequestId)
            return;

        // Overlay opened normally — its close callback will dismiss the panel.
        if (inviteOverlayWasOpenedByUs && !inviteOverlayActivated)
        {
            Debug.LogWarning("[OpenInviteFriendsPopup] Steam overlay never reported as activated — " +
                "closing the invite panel to avoid a stuck screen.");
            inviteOverlayWasOpenedByUs = false;
            CloseInviteFriendsPopUp(InviteOverlayUnavailableMessage);
        }
    }

    /// <summary>Call when the invite panel is dismissed by something other than the
    /// Steam overlay's own activation callback (e.g. a manual cancel/escape input),
    /// so a later overlay callback or watchdog doesn't act on stale state.</summary>
    public void CancelInviteOverlayTracking()
    {
        inviteOverlayWasOpenedByUs = false;
        inviteOverlayActivated = false;
        inviteRequestId++;
    }

    private static string InviteOverlayUnavailableMessage =>
        Loc.T("Steam overlay unavailable - share the lobby code from the pause menu");
    private static string InviteLobbyFailedMessage => Loc.T("Couldn't open Steam invites");
    private static string InviteSteamNotRunningMessage => Loc.T("Steam invites unavailable - Steam isn't running");

    /// <param name="failureMessage">When set, shows it as an error toast (in-game invite panel only)
    /// so the player knows why nothing opened.</param>
    public void CloseInviteFriendsPopUp(string failureMessage = null)
    {
        if (UIController.Instance != null)
        {
            if (!string.IsNullOrEmpty(failureMessage))
                UIController.Instance.ShowInviteUnavailableNotification(failureMessage);
            UIController.Instance.CloseInviteFriendsScreen();
        }
    }

    private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
    {
        Debug.Log($"Steam invite accepted from {friendId} for lobby {lobby.Id}");

        if (CurrentLobby.Id == lobby.Id)
        {
            LobbyToast.Show(Loc.T(MsgAlreadyInLobby));
            return;
        }

        if (IsInGameplay)
        {
            await JoinInviteFromGameplay(lobby.Id);
            return;
        }

        await JoinLobbyFromInvite(lobby.Id);
    }

    /// <summary>Gameplay (or the committed transition into it) rather than the main menu screens.</summary>
    private static bool IsInGameplay
    {
        get
        {
            MainMenuController menu = MainMenuController.Instance;
            if (menu == null)
                return false;
            return menu.IsGameStartLocked || menu.mainMenu == null || !menu.mainMenu.activeSelf;
        }
    }

    /// <summary>
    /// Invite accepted mid-game. Joining in place used to shut the session down under the running
    /// gameplay scene (player and camera despawned, stale game state), so instead leave like
    /// "Return to main menu" does, reload the scene, and join through the cold-launch invite flow,
    /// which shows the join screen's connecting state and reports failures there.
    /// </summary>
    private async Task JoinInviteFromGameplay(ulong lobbyId)
    {
        if (_joinInProgress || IsLaunchInviteJoinPending)
        {
            LobbyToast.Error(Loc.T(MsgJoinInProgress));
            return;
        }

        Debug.Log($"[Invite] Invite accepted during gameplay — returning to the menu to join lobby {lobbyId}.");
        LobbyToast.Show(Loc.T("Leaving the current game to join your friend..."));

        // Same as the pause menu's Return to Main Menu: progress since the day start isn't kept.
        SaveDataManager.Instance?.StopPlaytimeTracking();
        SaveDataManager.Instance?.RevertToLastCheckpoint();

        _launchInviteLobbyId = lobbyId;
        _launchInviteResolved = false;
        _launchArgsConsumed = false;
        MainMenuSplashScreen.Suppress();

        await ExitLobbyAsync();
        if (this == null)
            return;

        SceneManager.sceneLoaded += OnSceneReloadedForInvite;
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    private void OnSceneReloadedForInvite(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneReloadedForInvite;
        if (this == null)
            return;

        _ = TryJoinLobbyFromLaunchArgs();
    }

    /// <summary>Shared invite join path for both in-game (callback) and cold-launch (command line) invites.</summary>
    private async Task JoinLobbyFromInvite(ulong lobbyId)
    {
        if (CurrentLobby.Id.Value == lobbyId)
        {
            Debug.Log($"[Invite] Already in lobby {lobbyId} — ignoring.");
            return;
        }

        if (!TryBeginJoin(out int requestId))
            return;

        string blocker = SteamJoinBlocker();
        if (blocker != null)
        {
            await FailJoin(requestId, blocker);
            return;
        }

        // Leave any lobby/session we're still in, otherwise StartClient would be skipped.
        if (CurrentLobby.Id != 0 || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
            await ExitLobbyAsync();
        if (!IsJoinCurrent(requestId))
            return;

        DevSpectatorRegistry.IsLocalSpectating = false;
        await JoinLobbyInternal(requestId, lobbyId, "127.0.0.1");
    }

    // =========================
    // STEAM CALLBACKS
    // =========================
    private async void OnLobbyEntered(Lobby lobby)
    {
        // Ignore the enter callback of a lobby we immediately left because it was full.
        if (lobby.Id.Value == _rejectedFullLobbyId)
            return;

        CurrentLobby = lobby;

        await Task.Delay(50);

        if (lobby.Id.Value == _rejectedFullLobbyId)
        {
            if (CurrentLobby.Id == lobby.Id)
                CurrentLobby = default;
            return;
        }

        CacheSpectatorMembers(lobby);

        Debug.Log($"[OnLobbyEntered] IsHost={NetworkManager.Singleton.IsHost} IsClient={NetworkManager.Singleton.IsClient} Members={CurrentLobby.Members.Count()} OwnerSteamId={lobby.Owner.Id}");

        OnLobbyUpdated?.Invoke();
    }

    private async void OnLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50);

        Debug.Log($"[OnLobbyMemberJoined] {friend.Name}");
        Debug.Log($"Members now: {CurrentLobby.Members.Count()}");

        CacheSpectatorMembers(lobby);

        // A fresh member hasn't published a role yet; IsSpectatorMember treats whitelisted dev
        // Steam IDs as spectators in that window, so a spectating developer never gets announced.
        if (ShouldShowMembershipNotification(lobby, friend))
            ShowMembershipNotification(friend.Name, joined: true);

        PublishVisiblePlayerCount();
        OnLobbyUpdated?.Invoke();
    }

    private async void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        // Decide before the delay: the leaver's member data (spectator role) may be gone afterwards.
        bool showLeftNotification = ShouldShowMembershipNotification(lobby, friend);
        _spectatorMemberIds.Remove(friend.Id.Value);

        await Task.Delay(50);

        Debug.Log($"[OnLobbyMemberLeave] {friend.Name}");

        if (showLeftNotification)
            ShowMembershipNotification(friend.Name, joined: false);

        PublishVisiblePlayerCount();
        OnLobbyUpdated?.Invoke();
    }

    // Steam IDs of lobby members known to be hidden dev spectators. Cached while they're present
    // because their role member data can't be read reliably once they've left.
    private readonly System.Collections.Generic.HashSet<ulong> _spectatorMemberIds =
        new System.Collections.Generic.HashSet<ulong>();

    private void CacheSpectatorMembers(Lobby lobby)
    {
        _spectatorMemberIds.Clear();
        foreach (var member in lobby.Members)
        {
            if (DevSpectatorConfig.IsSpectatorMember(lobby, member))
                _spectatorMemberIds.Add(member.Id.Value);
        }
    }

    /// <summary>
    /// True when a joining/leaving lobby member should produce a "X has joined/left the game"
    /// toast here: we're in a live session, it isn't us, it isn't a hidden dev spectator, and it
    /// isn't the host (losing the host is handled by <see cref="ConnectionLossHandler"/>).
    /// </summary>
    private bool ShouldShowMembershipNotification(Lobby lobby, Friend friend)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || IsIntentionalDisconnect)
            return false;

        if (SteamClient.IsValid && friend.Id == SteamClient.SteamId)
            return false;

        if (_spectatorMemberIds.Contains(friend.Id.Value) || DevSpectatorConfig.IsSpectatorMember(lobby, friend))
            return false;

        if (!nm.IsServer && facepunchTransport != null && friend.Id == facepunchTransport.targetSteamId)
            return false;

        return true;
    }

    private static void ShowMembershipNotification(string steamName, bool joined)
    {
        bool hasName = !string.IsNullOrWhiteSpace(steamName);
        string message = joined
            ? (hasName ? Loc.T("{0} has joined the game.", steamName) : Loc.T("A player has joined the game."))
            : (hasName ? Loc.T("{0} has left the game.", steamName) : Loc.T("A player has left the game."));
        if (hasName)
            message = Loc.Verbatim(message); // the Steam name must never go through localization

        // The HUD stacks are hidden on the main menu (pre-game ready-up screen), so use the
        // lobby toast there.
        MainMenuController menu = MainMenuController.Instance;
        if (menu != null && menu.mainMenu != null && menu.mainMenu.activeInHierarchy)
        {
            LobbyToast.Show(message);
            return;
        }

        if (UIController.Instance == null)
            return;

        // Small plain-text line in the top-left cash notification stack, kept out of the large
        // bottom-centre HUD queue so it doesn't compete with gameplay alerts.
        UIController.Instance.ShowTextNotification(message);
    }

    private void OnLobbyMemberDataChanged(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        CacheSpectatorMembers(lobby);

        // Role changes (player/spectator) affect which members the UI shows.
        PublishVisiblePlayerCount();
        OnLobbyUpdated?.Invoke();
    }

    /// <summary>
    /// Lobby metadata (e.g. join_code) can arrive after OnLobbyEntered on joining clients,
    /// so refresh the UI whenever it changes.
    /// </summary>
    private void OnLobbyDataChanged(Lobby lobby)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        OnLobbyUpdated?.Invoke();
    }

    /// <summary>
    /// Lobby owner only: publishes the visible (non-spectator) player count as lobby data so
    /// lobby browsers never reveal a hidden developer spectator through MemberCount.
    /// </summary>
    private void PublishVisiblePlayerCount()
    {
        if (CurrentLobby.Id == 0 || !SteamClient.IsValid || CurrentLobby.Owner.Id != SteamClient.SteamId)
            return;

        CurrentLobby.SetData(DevSpectatorConfig.LobbyDataPlayerCount,
            DevSpectatorConfig.CountVisibleMembers(CurrentLobby).ToString());
    }

    // =========================
    // CONNECTION APPROVAL
    // =========================

    /// <summary>
    /// SERVER: approves every connection without an automatic player object (PlayerSpawner owns
    /// spawning). Whitelisted developer spectators are registered so they're never spawned;
    /// regular players are rejected once the visible player cap is reached.
    /// </summary>
    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;

        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        if (DevSpectatorConfig.TryParseSpectatorPayload(request.Payload, out ulong devSteamId))
        {
            if (!DevSpectatorConfig.VerifyConnectionSteamId(NetworkManager.Singleton, request.ClientNetworkId, devSteamId))
            {
                response.Approved = false;
                response.Reason = "SpectatorIdentityMismatch";
                Debug.LogWarning($"[DevSpectator] Rejected spectator request clientId={request.ClientNetworkId} — claimed steamId={devSteamId} does not match the Steam connection.");
                return;
            }

            DevSpectatorRegistry.RegisterSpectator(request.ClientNetworkId);
            response.Approved = true;
            Debug.Log($"[DevSpectator] Approved hidden spectator clientId={request.ClientNetworkId} steamId={devSteamId}.");
            return;
        }

        int playerCount = HandshakeCompletePlayerCount(NetworkManager.Singleton);
        if (playerCount >= MaxLobbyMembers)
        {
            response.Approved = false;
            response.Reason = "LobbyFull";
            Debug.LogWarning($"[LobbyManager] Rejected clientId={request.ClientNetworkId} — player cap ({MaxLobbyMembers}) reached. " +
                             $"NGO connected ids=[{string.Join(",", NetworkManager.Singleton.ConnectedClientsIds)}], " +
                             $"handshake-complete=[{string.Join(",", _handshakeCompleteClientIds)}].");
            return;
        }

        // Late-join sync throws (and drops the joiner) if any spawned NetworkObject still lists a
        // destroyed NetworkBehaviour — see NetworkSyncGuard. Clean that up before NGO serializes.
        NetworkSyncGuard.PurgeDestroyedBehaviours(NetworkManager.Singleton);

        // Remember who this is so a player who left while dead can be recognised on rejoin
        // (see ReviveManager.ShouldSpawnAsDeadServer).
        PlayerSessionIdentity.RegisterClient(request.ClientNetworkId, request.Payload);
        response.Approved = true;
    }

    /// <summary>
    /// SERVER: NGO client ids whose connection fully completed (OnClientConnectedCallback fired).
    /// A join that throws mid-handshake never reaches that callback but can linger in
    /// ConnectedClientsIds as a ghost; counting only completed handshakes keeps ghosts from
    /// filling the player cap and locking everyone out.
    /// </summary>
    private readonly System.Collections.Generic.HashSet<ulong> _handshakeCompleteClientIds = new();

    /// <summary>SERVER: real (non-spectator) players that are connected AND finished the handshake.</summary>
    private int HandshakeCompletePlayerCount(NetworkManager networkManager)
    {
        int count = 0;
        foreach (ulong id in DevSpectatorRegistry.PlayerClientIds(networkManager))
        {
            if (_handshakeCompleteClientIds.Contains(id))
                count++;
        }
        return count;
    }

    private void OnServerStopped(bool wasHost) => _handshakeCompleteClientIds.Clear();

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            _handshakeCompleteClientIds.Remove(clientId);
            DevSpectatorRegistry.UnregisterSpectator(clientId);
            PlayerSessionIdentity.UnregisterClient(clientId);
        }
    }

    private async void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        _handshakeCompleteClientIds.Add(clientId);

        // NGO fires OnClientConnectedCallback for the host's own LocalClientId too, synchronously
        // inside StartHost() — not just for remote clients joining. The normal main-menu "Play"
        // flow is shielded from this because it calls GameManager.BeginLobbyTransition() first,
        // routing the host's self-connect into the harmless "IsTransitioningToLobby" branch below.
        // Debug/editor skip flows (DebugConsole.EnsureGameStartedThen — used by every "Game Start
        // Point" option and cheat-console skip) never set that flag, so without this guard the
        // host's self-connect fell into the "game started, intro not started" branch and called
        // GameManager.InitializeLobbyJoinClient(..., gameAlreadyStarted: true), which re-invokes
        // CampaignManager.StartCampaign() a second time — independently of and later than any
        // debug JumpToDay() call. That second StartCampaign() re-reads SaveDataManager's day,
        // which (with no save slot selected, as these skips bypass the campaign-select screen)
        // always falls back to Day 1, silently reverting the debug jump every time. This handler
        // is only meant for OTHER clients connecting to the host's lobby, so skip self entirely.
        if (clientId == NetworkManager.Singleton.LocalClientId)
            return;

        // Hidden developer spectators get no player object, no lobby/late-join bootstrap and no
        // UI refresh — the dev client drives its own spectator setup (DevSpectatorController).
        if (DevSpectatorRegistry.IsSpectator(clientId))
        {
            Debug.Log($"[Host] clientId={clientId} is a hidden dev spectator — skipping spawn/bootstrap.");
            return;
        }

        await Task.Delay(50);

        Debug.Log($"[Host] OnClientConnected clientId={clientId} GameManager.Instance={GameManager.Instance != null}");

        if (CurrentLobby.Id != 0)
            Debug.Log($"[Host] Steam members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();

        if (GameManager.Instance == null)
        {
            Debug.LogError("[Host] OnClientConnected: GameManager.Instance is null — cannot spawn or send RPC.");
            return;
        }

        Debug.Log($"[Host] HasGameStarted={GameManager.Instance.HasGameStarted} IsTransitioningToLobby={GameManager.Instance.IsTransitioningToLobby}");

        bool isLaterDay = CampaignManager.Instance != null && CampaignManager.Instance.CurrentDay > 1;

        if (GameManager.Instance.HasGameStarted && isLaterDay)
        {
            // Day 2+ late joiners always wake up inside the bunker, regardless of where the host
            // is. This must be checked before the intro-cutscene branches: HasIntroCutsceneStarted
            // is only set by the Day 1 intro, so on a resumed/retried Day 2+ session it stays
            // false and the joiner would otherwise fall into the lobby-joiner path below.
            Debug.Log($"[Host] Game started on Day {CampaignManager.Instance.CurrentDay} — spawning late joiner inside bunker for clientId={clientId}");
            GameManager.Instance.SpawnPlayerInBunkerServer(clientId);
            GameManager.Instance.InitializeLateJoinClient(clientId);
        }
        else if (GameManager.Instance.HasGameStarted && GameManager.Instance.HasIntroCutsceneStarted)
        {
            // Spawn relative to where the host currently is.
            // Read IsOutside from the host's networked PlayerObject directly rather than
            // PlayerInstance.Instance (which is a local-player singleton and can be null
            // on the host when the client connects, causing a silent false → booth spawn).
            ulong hostClientId = NetworkManager.Singleton.LocalClientId;
            bool hostIsOutside = false;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(hostClientId, out var hostClient) &&
                hostClient.PlayerObject != null)
            {
                var hostPlayerInstance = hostClient.PlayerObject.GetComponent<PlayerInstance>();
                hostIsOutside = hostPlayerInstance != null && hostPlayerInstance.IsOutside;
            }

            // The intro cutscene moves the host from outside to inside, but the host's own
            // IsOutside NetworkVariable doesn't flip to false until partway through the
            // cutscene's fade-in — a brief window where HasIntroCutsceneStarted is already
            // true but hostIsOutside still reads stale. A client connecting in that window
            // would otherwise be routed to the lobby even though the cutscene is about to
            // (or already did) place everyone at the booth. Treat that window as "inside".
            if (GameManager.Instance.IsIntroCutsceneEntering)
            {
                Debug.Log("[Host] IsIntroCutsceneEntering=true — overriding stale hostIsOutside, treating host as inside.");
                hostIsOutside = false;
            }

            Debug.Log($"[Host] hostClientId={hostClientId} hostIsOutside={hostIsOutside}");

            if (hostIsOutside)
            {
                Debug.Log($"[Host] Game started, host is outside — spawning client at lobby for clientId={clientId}");
                GameManager.Instance.SpawnPlayerAtLobbyServer(clientId);
            }
            else
            {
                Debug.Log($"[Host] Game started, host is at booth — spawning client at booth for clientId={clientId}");
                PlayerSpawner.Instance.SpawnPlayerAtBooth(clientId);
            }
            GameManager.Instance.InitializeLateJoinClient(clientId);
        }
        else if (GameManager.Instance.HasGameStarted && !GameManager.Instance.HasIntroCutsceneStarted)
        {
            // Game started but intro cutscene not yet — treat this joiner as a lobby joiner so they get OnGameStart.
            // Pass gameAlreadyStarted=true so StartCampaign() fires on the client (it missed StartGameClientRpc).
            Debug.Log($"[Host] Game started, intro cutscene not yet — spawning as lobby joiner for clientId={clientId}");
            GameManager.Instance.SpawnPlayerAtLobbyServer(clientId);
            GameManager.Instance.InitializeLobbyJoinClient(clientId, gameAlreadyStarted: true);
        }
        else if (!GameManager.Instance.IsTransitioningToLobby)
        {
            // Host is still on the pre-game ready-up screen — bring the joining client there too.
            // Player spawning is deferred until the host actually presses Start Game
            // (GameManager.SpawnAllPlayersAtLobby handles every connected client at that point).
            Debug.Log($"[Host] Host still in pre-game lobby — sending clientId={clientId} to the ready-up screen");
            GameManager.Instance.InitializeLobbyJoinClient(clientId, gameAlreadyStarted: false);
        }
        else
        {
            Debug.Log($"[Host] IsTransitioningToLobby=true — spawn deferred to LobbyTransitionSequence for clientId={clientId}");
            _ = RerouteIfDeferredClientMissed(clientId);
        }
    }

    /// <summary>
    /// SERVER: the deferred branch above assumes LobbyTransitionSequence will spawn this client.
    /// That's only true when the transition actually runs. The host/new-game flows clear the flag
    /// via CancelLobbyTransition without running it, which would leave the joiner stuck on the
    /// main menu. Once the flag clears, re-run routing if the client still has no player object.
    /// </summary>
    private async Task RerouteIfDeferredClientMissed(ulong clientId)
    {
        const float TimeoutSeconds = 30f;
        float waited = 0f;

        while (waited < TimeoutSeconds)
        {
            await Task.Delay(250);
            waited += 0.25f;

            NetworkManager nm = NetworkManager.Singleton;
            if (this == null || nm == null || !nm.IsServer || GameManager.Instance == null)
                return;
            if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
                return; // Disconnected meanwhile.
            if (GameManager.Instance.IsTransitioningToLobby)
                continue;

            if (client.PlayerObject != null)
                return; // LobbyTransitionSequence (or a resumed-day spawn) handled it.

            Debug.Log($"[Host] Lobby transition ended without routing clientId={clientId} — re-routing.");
            OnClientConnected(clientId);
            return;
        }

        Debug.LogWarning($"[Host] clientId={clientId} still deferred after {TimeoutSeconds}s — IsTransitioningToLobby never cleared.");
    }

    // =========================
    // HELPERS
    // =========================
    /// <summary>Visible lobby members — hidden developer spectators are excluded.</summary>
    public Friend[] GetMembersSnapshot()
    {
        if (CurrentLobby.Id == 0)
            return Array.Empty<Friend>();

        Lobby lobby = CurrentLobby;
        return lobby.Members.Where(m => !DevSpectatorConfig.IsSpectatorMember(lobby, m)).ToArray();
    }

    /// <summary>All Steam lobby members including hidden spectators (developer tooling only).</summary>
    public Friend[] GetAllMembersSnapshot()
    {
        if (CurrentLobby.Id == 0)
            return Array.Empty<Friend>();

        return CurrentLobby.Members.ToArray();
    }

    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    public async void ExitLobby()
    {
        await ExitLobbyAsync();
    }

    public async Task ExitLobbyAsync()
    {
        IsIntentionalDisconnect = true;

        try
        {
            inviteOverlayWasOpenedByUs = false;
            CurrentJoinCode = null;

            if (CurrentLobby.Id != 0)
            {
                CurrentLobby.Leave();
                CurrentLobby = default;

                // Leave() takes effect locally right away, but Steam's matchmaking backend
                // processes the membership change asynchronously. Give it a brief moment so
                // an immediate rejoin of the same lobby (e.g. re-entering a join code) doesn't
                // race the leave and spuriously fail with "lobby not found".
                await Task.Delay(300);
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null &&
                (networkManager.IsListening || networkManager.IsHost || networkManager.IsServer || networkManager.IsClient || networkManager.ShutdownInProgress))
            {
                networkManager.Shutdown();
                await WaitForNetworkShutdownAsync(networkManager);
                networkManager.NetworkConfig?.NetworkTransport?.Shutdown();
            }

            OnLobbyUpdated?.Invoke();
        }
        finally
        {
            DevSpectatorRegistry.IsLocalSpectating = false;
            DevSpectatorRegistry.ClearServerState();
            IsIntentionalDisconnect = false;
        }
    }

    private static async Task WaitForNetworkShutdownAsync(NetworkManager networkManager)
    {
        const int TimeoutMs = 3000;
        const int PollDelayMs = 50;
        int elapsedMs = 0;

        while (networkManager != null &&
               (networkManager.IsListening || networkManager.IsHost || networkManager.IsServer || networkManager.IsClient || networkManager.ShutdownInProgress) &&
               elapsedMs < TimeoutMs)
        {
            await Task.Delay(PollDelayMs);
            elapsedMs += PollDelayMs;
        }

        if (networkManager != null &&
            (networkManager.IsListening || networkManager.IsHost || networkManager.IsServer || networkManager.IsClient || networkManager.ShutdownInProgress))
        {
            Debug.LogWarning($"[LobbyManager] Timed out waiting for NetworkManager shutdown. IsListening={networkManager.IsListening}, IsHost={networkManager.IsHost}, IsServer={networkManager.IsServer}, IsClient={networkManager.IsClient}, ShutdownInProgress={networkManager.ShutdownInProgress}");
        }
    }

    private void OnApplicationQuit()
    {
        ShutdownNetworkSessionImmediate();
    }

    private void ShutdownNetworkSessionImmediate()
    {
        inviteOverlayWasOpenedByUs = false;
        CurrentJoinCode = null;
        DevSpectatorRegistry.IsLocalSpectating = false;
        DevSpectatorRegistry.ClearServerState();

        if (CurrentLobby.Id != 0)
        {
            CurrentLobby.Leave();
            CurrentLobby = default;
        }

        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown(discardMessageQueue: true);
        }

        NetworkManager.Singleton?.NetworkConfig?.NetworkTransport?.Shutdown();
    }
}
