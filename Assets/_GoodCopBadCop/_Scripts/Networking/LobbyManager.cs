using System;
using System.Linq;
using System.Threading.Tasks;
using Netcode.Transports.Facepunch;
using UnityEngine;
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
    public event Action<string> OnJoinFailed;

    private FacepunchTransport facepunchTransport;
    private bool inviteOverlayWasOpenedByUs;
    private ulong _rejectedFullLobbyId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
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
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamFriends.OnGameOverlayActivated += OnOverlayToggled;
        }

        // Connection approval is enabled for every build (it is part of NGO's config hash) so the
        // host can identify hidden developer spectators and enforce the visible-player cap.
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
            if (NetworkManager.Singleton.ConnectionApprovalCallback == ApproveConnection)
                NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }

        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
        SteamMatchmaking.OnLobbyMemberDataChanged -= OnLobbyMemberDataChanged;
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
        if (!isActive && inviteOverlayWasOpenedByUs)
        {
            inviteOverlayWasOpenedByUs = false;
            CloseInviteFriendsPopUp();
        }
    }

    /// <summary>Visible player cap. The Steam lobby itself gets extra hidden developer slot(s).</summary>
    private const int MaxLobbyMembers = DevSpectatorConfig.MaxPlayers;
    private const int SteamLobbyCapacity = DevSpectatorConfig.MaxPlayers + DevSpectatorConfig.ReservedDevSlots;

    /// <summary>True while the local machine is joining/connected as a hidden developer spectator.</summary>
    public bool IsDevSpectating => DevSpectatorRegistry.IsLocalSpectating;

    private const string LobbyDataKeyJoinCode = "join_code";
    private const int JoinCodeLength = 6;
    private const string JoinCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string CurrentJoinCode { get; private set; }

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

            Debug.Log("[CreateLobby] FacepunchTransport detected — creating Steam lobby...");

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

            CurrentLobby.SetMemberData(DevSpectatorConfig.MemberRoleKey, DevSpectatorConfig.RolePlayer);
            CurrentLobby.SetData(DevSpectatorConfig.LobbyDataMaxPlayers, MaxLobbyMembers.ToString());
            PublishVisiblePlayerCount();

            CurrentJoinCode = GenerateJoinCode();
            CurrentLobby.SetData(LobbyDataKeyJoinCode, CurrentJoinCode);
            Debug.Log($"[CreateLobby] Join code: {CurrentJoinCode}");

            // Host targets self for Facepunch transport.
            facepunch.targetSteamId = SteamClient.SteamId;
            Debug.Log($"[CreateLobby] Steam lobby created: {CurrentLobby.Id}, targetSteamId={facepunch.targetSteamId}");

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

    // =========================
    // CLIENT
    // =========================

    /// <summary>Joins a Steam lobby by ID (Facepunch transport) or connects to a LAN host at 127.0.0.1 (UnityTransport).
    /// Pass lobbyId = 0 when using UnityTransport to fall through to the LAN path.</summary>
    public async void JoinLobby(ulong lobbyId)
    {
        // A regular join (invite, browser, code) always drops any previous spectator state.
        DevSpectatorRegistry.IsLocalSpectating = false;
        await JoinLobbyInternal(lobbyId, "127.0.0.1");
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

        await ExitLobbyAsync();

        DevSpectatorRegistry.IsLocalSpectating = true;
        await JoinLobbyInternal(lobbyId, "127.0.0.1");

        bool joined = CurrentLobby.Id.Value == lobbyId;
        if (!joined)
            DevSpectatorRegistry.IsLocalSpectating = false;
        return joined;
    }

    /// <summary>Joins a LAN host at the given IP address using UnityTransport.
    /// Ignored when FacepunchTransport is the active transport.</summary>
    public async void JoinLobbyLAN(string address) => await JoinLobbyInternal(0, address);

    /// <summary>
    /// Searches public Steam lobbies for one whose join_code metadata matches <paramref name="code"/>
    /// and joins it. Fires <see cref="OnJoinFailed"/> if no matching lobby is found.
    /// Only valid when FacepunchTransport is active.
    /// </summary>
    public async void JoinLobbyByCode(string code)
    {
        if (NetworkManager.Singleton == null)
            return;

        var normalizedCode = code.Trim().ToUpperInvariant();

        if (!SteamClient.IsValid)
        {
            Debug.LogError("[JoinLobbyByCode] Steam is not initialized — cannot search for lobbies.");
            OnJoinFailed?.Invoke("STEAM_NOT_READY");
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

            if (lobbies != null && lobbies.Length > 0)
                break;

            if (attempt < MaxSearchAttempts)
            {
                Debug.LogWarning($"[JoinLobbyByCode] Attempt {attempt}/{MaxSearchAttempts}: no lobby for '{normalizedCode}' yet — retrying in {SearchRetryDelaySeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(SearchRetryDelaySeconds));
            }
        }

        if (lobbies == null || lobbies.Length == 0)
        {
            Debug.LogWarning($"[JoinLobbyByCode] No lobby found for code '{normalizedCode}'. " +
                             $"If the host is definitely online, confirm both players run the game under the same Steam AppId (local={SteamClient.AppId.Value}).");
            OnJoinFailed?.Invoke("CODE_NOT_FOUND");
            return;
        }

        // Leave any lobby/session we're still in (e.g. after backing out of hosting),
        // otherwise StartClient would be skipped because NetworkManager is still running.
        if (CurrentLobby.Id != 0 || NetworkManager.Singleton.IsListening)
            await ExitLobbyAsync();

        DevSpectatorRegistry.IsLocalSpectating = false;

        // Take the first match; codes are unique per active session.
        var target = lobbies[0];
        Debug.Log($"[JoinLobbyByCode] Found lobby {target.Id} for code '{normalizedCode}'.");
        await JoinLobbyInternal(target.Id, "127.0.0.1");
    }

    private async Task JoinLobbyInternal(ulong lobbyId, string lanAddress)
    {
        if (NetworkManager.Singleton == null)
            return;

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
                if (joinResult == RoomEnter.Success)
                    break;

                Debug.LogWarning($"[JoinLobby] Attempt {attempt}/{MaxJoinAttempts} failed to join lobby {lobbyId}. Result: {joinResult}" +
                    (attempt < MaxJoinAttempts ? $" — retrying in {JoinRetryDelaySeconds}s..." : ""));

                if (attempt < MaxJoinAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(JoinRetryDelaySeconds));
            }

            if (joinResult != RoomEnter.Success)
            {
                Debug.LogError($"[JoinLobby] Failed to join lobby {lobbyId} after {MaxJoinAttempts} attempts. Result: {joinResult}");
                OnJoinFailed?.Invoke(joinResult.ToString());
                return;
            }

            bool asSpectator = DevSpectatorRegistry.IsLocalSpectating;

            // Publish our role immediately so every other member can filter hidden spectators.
            lobby.SetMemberData(DevSpectatorConfig.MemberRoleKey,
                asSpectator ? DevSpectatorConfig.RoleSpectator : DevSpectatorConfig.RolePlayer);

            // The Steam lobby has hidden developer slot(s) on top of the visible player cap.
            // Regular players must never occupy them: leave if the visible cap is already met.
            if (!asSpectator && DevSpectatorConfig.CountVisibleMembers(lobby) > MaxLobbyMembers)
            {
                Debug.LogWarning($"[JoinLobby] Lobby {lobbyId} is full ({MaxLobbyMembers} players) — leaving.");
                _rejectedFullLobbyId = lobby.Id.Value;
                lobby.Leave();
                if (CurrentLobby.Id == lobby.Id)
                    CurrentLobby = default;
                OnJoinFailed?.Invoke("LobbyFull");
                return;
            }

            _rejectedFullLobbyId = 0;

            CurrentLobby = lobby;

            if (facepunchTransport == null)
            {
                Debug.LogError("[JoinLobby] facepunchTransport is null — cannot connect.");
                return;
            }

            NetworkManager.Singleton.NetworkConfig.ConnectionData = DevSpectatorConfig.BuildConnectionPayload(asSpectator);

            facepunchTransport.targetSteamId = CurrentLobby.Owner.Id;
            Debug.Log($"[JoinLobby] Steam join success. targetSteamId={facepunchTransport.targetSteamId}");

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                bool started = NetworkManager.Singleton.StartClient();
                Debug.Log($"[JoinLobby] StartClient={started}");
            }
            else
            {
                Debug.LogWarning($"[JoinLobby] Skipping StartClient — already IsClient={NetworkManager.Singleton.IsClient} IsHost={NetworkManager.Singleton.IsHost}");
            }
        }
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData(lanAddress, 7777);
            NetworkManager.Singleton.NetworkConfig.ConnectionData = DevSpectatorConfig.BuildConnectionPayload(false);
            Debug.Log($"[JoinLobby] Connecting to LAN host at {lanAddress}:7777");

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                NetworkManager.Singleton.StartClient();
            }
        }
    }

    // =========================
    // INVITES
    // =========================

    public async void OpenInviteFriendsPopup()
    {
        // The Steam overlay only renders when this game is running as a real build
        // launched through the Steam client (it never renders inside the Unity
        // Editor's Game View). If it isn't available, OpenGameInviteOverlay() is a
        // silent no-op — OnGameOverlayActivated never fires, and the caller-side
        // panel would otherwise stay open forever with no way to dismiss it. Bail
        // out up front so the UI never gets stuck in that state.
        if (!SteamUtils.IsOverlayEnabled)
        {
            Debug.LogWarning("[OpenInviteFriendsPopup] Steam overlay is not enabled/available " +
                "(e.g. running in the Editor, or Overlay disabled in Steam settings). " +
                "Cannot open the invite overlay.");
            CloseInviteFriendsPopUp();
            return;
        }

        if (CurrentLobby.Id == 0)
        {
            // No lobby yet (e.g. the scene was entered directly instead of via the
            // normal host flow). Create one on the fly so the invite overlay always
            // has a valid lobby to invite friends into.
            Debug.LogWarning("[OpenInviteFriendsPopup] No active Steam lobby — creating one now.");
            bool created = await CreateLobby();
            if (!created)
            {
                Debug.LogError("[OpenInviteFriendsPopup] Failed to create a lobby — cannot open invite overlay.");
                CloseInviteFriendsPopUp();
                return;
            }
        }

        inviteOverlayWasOpenedByUs = true;
        SteamFriends.OpenGameInviteOverlay(CurrentLobby.Id);
        Debug.Log($"Opened Steam invite popup for lobby {CurrentLobby.Id}");

        // Safety net: if the overlay never actually reports as opened within a few
        // seconds (overlay hook failed, big-picture mode issue, etc.), auto-close
        // the panel instead of leaving the player stuck on a dark screen forever.
        _ = WatchdogCloseInviteIfOverlayNeverOpens();
    }

    private async Task WatchdogCloseInviteIfOverlayNeverOpens()
    {
        await Task.Delay(4000);
        if (inviteOverlayWasOpenedByUs)
        {
            Debug.LogWarning("[OpenInviteFriendsPopup] Steam overlay never reported as activated — " +
                "closing the invite panel to avoid a stuck screen.");
            inviteOverlayWasOpenedByUs = false;
            CloseInviteFriendsPopUp();
        }
    }

    /// <summary>Call when the invite panel is dismissed by something other than the
    /// Steam overlay's own activation callback (e.g. a manual cancel/escape input),
    /// so a later overlay callback doesn't act on stale state.</summary>
    public void CancelInviteOverlayTracking()
    {
        inviteOverlayWasOpenedByUs = false;
    }

    public void CloseInviteFriendsPopUp()
    {
        if (UIController.Instance != null)
        {
            UIController.Instance.CloseInviteFriendsScreen();
        }
    }

    private void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
    {
        Debug.Log($"Steam invite accepted from {friendId} for lobby {lobby.Id}");
        JoinLobby(lobby.Id);
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

        PublishVisiblePlayerCount();
        OnLobbyUpdated?.Invoke();
    }

    private async void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50);

        Debug.Log($"[OnLobbyMemberLeave] {friend.Name}");

        PublishVisiblePlayerCount();
        OnLobbyUpdated?.Invoke();
    }

    private void OnLobbyMemberDataChanged(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        // Role changes (player/spectator) affect which members the UI shows.
        PublishVisiblePlayerCount();
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
            DevSpectatorRegistry.RegisterSpectator(request.ClientNetworkId);
            response.Approved = true;
            Debug.Log($"[DevSpectator] Approved hidden spectator clientId={request.ClientNetworkId} steamId={devSteamId}.");
            return;
        }

        if (DevSpectatorRegistry.PlayerClientCount(NetworkManager.Singleton) >= MaxLobbyMembers)
        {
            response.Approved = false;
            response.Reason = "LobbyFull";
            Debug.LogWarning($"[LobbyManager] Rejected clientId={request.ClientNetworkId} — player cap ({MaxLobbyMembers}) reached.");
            return;
        }

        response.Approved = true;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            DevSpectatorRegistry.UnregisterSpectator(clientId);
    }

    private async void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

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
        }
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
