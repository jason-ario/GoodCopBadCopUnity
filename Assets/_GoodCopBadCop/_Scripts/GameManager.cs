using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    public UnityAction OnGameStart;

    [SerializeField] private AudioClip transitionToGameplayStinger;
    [SerializeField] private Transform folderPos;
    public Transform FolderPos => folderPos;

    public StampContainer.StampType verdictDelivered;

    public GateController GateController;

    /// <summary>
    /// Whether this session only has one connected player. Written by the server only, but
    /// replicated via NetworkVariable so every client can read a correct value — plain
    /// <c>NetworkManager.Singleton.ConnectedClients</c> checks are unreliable on non-host
    /// clients (that collection is only fully populated on the server), which previously
    /// caused non-host clients to always evaluate as single-player and use the wrong
    /// (single-player) spawn transform, e.g. during bunker/day-transition teleports.
    /// </summary>
    private readonly NetworkVariable<bool> _networkIsSinglePlayer = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsSinglePlayer => _networkIsSinglePlayer.Value;

    /// <summary>
    /// Set to true on the server before a Restart Day scene reload so that
    /// <see cref="OnNetworkSpawn"/> can auto-trigger the game start sequence after reload.
    /// Static so it survives the scene reload within the same AppDomain.
    /// </summary>
    private static bool _isRestartingDay;

    /// <summary>
    /// Captured by <see cref="RestartDay"/> right before the reload — the day phase the player
    /// was in when they died. Read by <see cref="RestartDaySequence"/> after reload to decide
    /// whether to resume normally (PreShift/Shift) or fast-forward straight to Dusk (PostShift).
    /// Static so it survives the scene reload within the same AppDomain.
    /// </summary>
    private static ShiftManager.DayPhase _restartDayPhase = ShiftManager.DayPhase.PreShift;

    public bool HasGameStarted { get; private set; }

    /// <summary>
    /// True once the intro cutscene has been initiated on the server.
    /// Used to distinguish clients who join during the lobby phase (game started but cutscene not yet)
    /// from clients who join after the cutscene is already playing.
    /// </summary>
    public bool HasIntroCutsceneStarted { get; private set; }

    /// <summary>
    /// Marks the intro cutscene as started. Call this server-side when the cutscene is initiated.
    /// </summary>
    public void SetIntroCutsceneStarted()
    {
        HasIntroCutsceneStarted = true;
        IsIntroCutsceneEntering = true;
    }

    /// <summary>
    /// True only for the brief server-side window between <see cref="SetIntroCutsceneStarted"/>
    /// and the moment the host's own player has actually been repositioned inside for the
    /// cutscene (<see cref="ClearIntroCutsceneEntering"/>). The host's <c>PlayerInstance.IsOutside</c>
    /// NetworkVariable doesn't flip to false until partway through the cutscene's fade-in, so a
    /// client connecting during this window would otherwise read a stale "host is outside" value
    /// in <see cref="Networking.LobbyManager"/>'s late-join spawn check and get routed to the lobby
    /// instead of the booth. See <see cref="ClearIntroCutsceneEntering"/>.
    /// </summary>
    public bool IsIntroCutsceneEntering { get; private set; }

    /// <summary>
    /// Call this server-side once the host has been positioned inside for the intro cutscene
    /// (right after the first <c>ResetEverything</c> pass in <see cref="Game_Systems.ShiftManager"/>'s
    /// PlayIntroCutscene, or the equivalent point in EndIntroCutsceneSequence). Ends the race window
    /// described on <see cref="IsIntroCutsceneEntering"/>.
    /// </summary>
    public void ClearIntroCutsceneEntering() => IsIntroCutsceneEntering = false;

    /// <summary>
    /// True while a lobby transition sequence is in progress and the spawn is being deferred.
    /// Set before CreateLobby() so OnClientConnected skips the immediate spawn.
    /// </summary>
    public bool IsTransitioningToLobby { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>Reserves the lobby spawn for the transition sequence. Call before CreateLobby().</summary>
    public void BeginLobbyTransition() => IsTransitioningToLobby = true;

    /// <summary>Clears the transition flag without running the sequence. Call on lobby creation failure.</summary>
    public void CancelLobbyTransition() => IsTransitioningToLobby = false;

    /// <summary>
    /// Plays the transition-to-gameplay stinger immediately.
    /// Call this at the same moment the screen-fade starts so audio and visual are in sync.
    /// </summary>
    public void PlayTransitionStinger()
    {
        SFXController.Instance.Play(transitionToGameplayStinger);
    }

    public void TryStartGame(bool skipTransition = false)
    {
        if (IsServer)
            StartGameServer(skipTransition);
        else
            RequestStartGameServerRpc(skipTransition);
    }

    /// <summary>
    /// Runs the visual transition from the main menu into the lobby area on all clients.
    /// Does not start the game or move players to the booth — call TryStartGame for that.
    /// SERVER ONLY.
    /// </summary>
    public void TransitionToLobby()
    {
        if (!IsServer) return;
        TransitionToLobbyClientRpc();
    }

    [ClientRpc]
    private void TransitionToLobbyClientRpc()
    {
        StartCoroutine(LobbyTransitionSequence());
    }

    /// <summary>
    /// Set to true by <see cref="LobbySpawnCompleteClientRpc"/> once the server has finished
    /// spawning all players. Clients wait on this before starting the fade-out so the camera
    /// never switches mid-reveal.
    /// </summary>
    private bool _lobbyRevealReady;

    private IEnumerator LobbyTransitionSequence()
    {
        AudioManager.Instance.FadeOutAmbientAudio();
        SFXController.Instance.Play(transitionToGameplayStinger);

        // Wait until the screen is fully dark before spawning or moving anything.
        yield return StartCoroutine(UIController.Instance.FadeInAndWait());

        // Stop the main menu cutscene's visual playback now that the screen is black, before the
        // intro story cinematic starts — its background music keeps playing (see
        // MainMenuController.FadeOutCutsceneMusic below) so it doesn't cut off abruptly.
        MainMenuController.Instance.TransitionToGameplay();

        // Plays the intro story cinematic while the screen is still black. Local/client-side
        // only (no network sync needed) and a no-op after the first time it has played this
        // application session — see IntroCinematicController.PlayIfNeeded.
        if (IntroCinematicController.Instance != null)
            yield return StartCoroutine(IntroCinematicController.Instance.PlayIfNeeded());

        // The main menu's background music has been playing underneath the black screen and
        // the intro cutscene above — stop it now that the intro cutscene has finished.
        MainMenuController.Instance.StopMainMenuMusic();

        if (IsServer)
        {
            SpawnAllPlayersAtLobby();
            IsTransitioningToLobby = false;
            // Signal all clients (including self) that all players are spawned
            // and it is safe to start the reveal.
            LobbySpawnCompleteClientRpc();
        }

        // All clients — including the server-as-host — wait for the server's spawn-complete
        // signal before calling FadeOut. This prevents the camera from switching mid-fade
        // on non-server clients whose spawn RPC arrives after their fade completes.
        yield return new WaitUntil(() => _lobbyRevealReady);
        _lobbyRevealReady = false;

        AudioManager.Instance.StartAmbientAudio();
        UIController.Instance.FadeOut();

        // The main menu cutscene's visual playback already stopped when the screen went black
        // (see MainMenuController.TransitionToGameplay above); its background music keeps
        // playing until now, then fades out alongside the reveal so it doesn't cut off
        // abruptly behind the black screen.
        MainMenuController.Instance.FadeOutCutsceneMusic();

        // Wait for the fade-out animation to finish before revealing the HUD.
        const float FadeOutDuration = 0.8f;
        yield return new WaitForSeconds(FadeOutDuration);

        UIController.Instance.ShowPlayerUI();
        OnGameStart?.Invoke();
    }

    /// <summary>
    /// Sent by the server after all players have been spawned in the lobby.
    /// Sets the reveal-ready flag so every client's transition coroutine can proceed.
    /// </summary>
    [ClientRpc]
    private void LobbySpawnCompleteClientRpc()
    {
        _lobbyRevealReady = true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartGameServerRpc(bool skipTransition)
    {
        StartGameServer(skipTransition);
    }

    private void StartGameServer(bool skipTransition = false)
    {
        if (!IsServer) return;
        HasGameStarted = true;
        StartGameClientRpc(skipTransition);
    }

    /// <summary>
    /// Initializes gameplay UI for a single late-joining client without restarting the game.
    /// SERVER ONLY.
    /// </summary>
    public void InitializeLateJoinClient(ulong clientId)
    {
        if (!IsServer) return;

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        InitializeLateJoinClientRpc(rpcParams);
    }

    /// <summary>
    /// Runs the lobby transition sequence for a single client that joined while the host
    /// is already waiting in the lobby (game not yet started). SERVER ONLY.
    /// Pass <paramref name="gameAlreadyStarted"/> = true when the game has already begun
    /// (e.g. host started before this client connected) so the client bootstraps the
    /// current day immediately rather than waiting for StartGameClientRpc.
    /// </summary>
    public void InitializeLobbyJoinClient(ulong clientId, bool gameAlreadyStarted = false)
    {
        if (!IsServer) return;

        Debug.Log($"[GameManager] InitializeLobbyJoinClient sending RPC to clientId={clientId} gameAlreadyStarted={gameAlreadyStarted}");

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        InitializeLobbyJoinClientRpc(gameAlreadyStarted, rpcParams);
    }

    [ClientRpc]
    private void InitializeLobbyJoinClientRpc(bool gameAlreadyStarted, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[GameManager] InitializeLobbyJoinClientRpc received — starting lobby join sequence (gameAlreadyStarted={gameAlreadyStarted})");
        StartCoroutine(LobbyJoinClientSequence(gameAlreadyStarted));
    }

    private IEnumerator LobbyJoinClientSequence(bool gameAlreadyStarted)
    {
        if (!gameAlreadyStarted)
        {
            // Host hasn't pressed Start Game yet — show the same ready-up screen the host is
            // using instead of dropping the client straight into gameplay. Player info and
            // ready state are already kept in sync via LobbyManager (Steam lobby members) and
            // PlayerReadyManager, so simply switching screens here is enough.
            Debug.Log("[GameManager] LobbyJoinClientSequence: host still in pre-game lobby — opening ready-up screen");
            MainMenuController.Instance.OpenPreGameLobbyScreen(multiplayerMode: true);
            yield break;
        }

        Debug.Log("[GameManager] LobbyJoinClientSequence: fading in");
        AudioManager.Instance.FadeOutAmbientAudio();

        // Wait until the screen is fully dark before transitioning.
        yield return StartCoroutine(UIController.Instance.FadeInAndWait());

        Debug.Log("[GameManager] LobbyJoinClientSequence: waiting for local player spawn");

        // The server spawns this client's player object around the same time it sends
        // this RPC, but the NetworkObject spawn message may arrive a frame or two later.
        // Wait until PlayerInstance is live before revealing to avoid a mid-fade camera cut.
        const float SpawnTimeout = 5f;
        float spawnWaited = 0f;
        while (PlayerInstance.Instance == null && spawnWaited < SpawnTimeout)
        {
            yield return null;
            spawnWaited += Time.unscaledDeltaTime;
        }

        Debug.Log("[GameManager] LobbyJoinClientSequence: transitioning to gameplay");
        MainMenuController.Instance.TransitionToGameplay();
        MainMenuController.Instance.FadeOutCutsceneMusic();
        MainMenuController.Instance.StopMainMenuMusic();
        UIController.Instance.ShowPlayerUI();
        AudioManager.Instance.StartAmbientAudio();
        UIController.Instance.FadeOut();

        // The game was already running when this client joined (missed StartGameClientRpc),
        // so bootstrap the current day here so DayActivated() fires and all tutorial
        // subscriptions are set up.
        CampaignManager.Instance.StartCampaign();

        OnGameStart?.Invoke();
    }

    [ClientRpc]
    private void InitializeLateJoinClientRpc(ClientRpcParams clientRpcParams = default)
    {
        ShiftManager.Instance.StopIntroCutscene();
        UIController.Instance.ShowPlayerUI();
        MainMenuController.Instance.TransitionToGameplay();
        MainMenuController.Instance.FadeOutCutsceneMusic();
        MainMenuController.Instance.StopMainMenuMusic();
        // Bootstrap the current day on this client — it joined after StartGameClientRpc was
        // already sent, so DayActivated() was never called and no tutorial state was set up.
        CampaignManager.Instance.StartCampaign();
    }

    [ClientRpc]
    private void StartGameClientRpc(bool skipTransition = false)
    {
        CampaignManager.Instance.StartCampaign();
    }

    /// <summary>
    /// Spawns a single connecting player at the lobby spawn point.
    /// Call this from the networking layer when a client joins (server only).
    /// </summary>
    public void SpawnPlayerAtLobbyServer(ulong clientId)
    {
        if (!IsServer) return;

        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;
        _networkIsSinglePlayer.Value = isSinglePlayer;

        PlayerSpawner.Instance.SpawnPlayerAtLobby(clientId, isSinglePlayer);
    }

    private void SpawnAllPlayersAtLobby()
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;
        _networkIsSinglePlayer.Value = isSinglePlayer;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerSpawner.Instance.SpawnPlayerAtLobby(client.ClientId, isSinglePlayer);
        }
    }

    /// <summary>
    /// Public wrapper around <see cref="SpawnAllPlayersAtLobby"/> for callers that need to
    /// guarantee every connected client has a spawned player without running the full
    /// lobby-transition coroutine (<see cref="TransitionToLobby"/>) — namely resuming a save
    /// past Day 1, where <see cref="ShiftManager.ResumeSavedDay"/> repositions the player from
    /// this lobby spawn straight into the bunker instead. Without an explicit spawn here, a
    /// client whose connection was deferred while <see cref="IsTransitioningToLobby"/> was true
    /// (see <see cref="LobbyManager.OnClientConnected"/>'s last branch) would never get a
    /// player object at all if <see cref="TransitionToLobby"/> is skipped. Safe to call even if
    /// some clients already have a player object — <see cref="PlayerSpawner"/> skips duplicates.
    /// SERVER ONLY.
    /// </summary>
    public void SpawnAllPlayersForResumedDay()
    {
        if (!IsServer) return;
        SpawnAllPlayersAtLobby();
    }

    /// <summary>
    /// Teleports all connected players from the lobby to their gameplay spawn points.
    /// Called after the intro cutscene begins so players land at the booth for Day 1.
    /// SERVER ONLY.
    /// </summary>
    public void TeleportPlayersToGameplaySpawnPoints()
    {
        if (!IsServer) return;

        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                Transform spawnPoint = PlayerSpawner.Instance.GetSpawnPoint(client.ClientId, isSinglePlayer);
                client.PlayerObject.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

                PlayerInstance playerInstance = client.PlayerObject.GetComponent<PlayerInstance>();
                if (playerInstance != null)
                    playerInstance.SetIsOutside(false);
            }
        }
    }

    private IEnumerator TransitionToGameplay(bool skipTransition = false)
    {
        if (IsServer)
            _networkIsSinglePlayer.Value = NetworkManager.Singleton.ConnectedClientsList.Count == 1;

        if (skipTransition)
        {
            MainMenuController.Instance.TransitionToGameplay();
            MainMenuController.Instance.FadeOutCutsceneMusic();
            MainMenuController.Instance.StopMainMenuMusic();

            if (IsServer)
            {
                SpawnAllPlayersAtLobby();
                TeleportPlayersToGameplaySpawnPoints();
            }

            // StartCampaign is called from StartGameClientRpc on all clients — no duplicate call here.
            yield break;
        }

        UIController.Instance.FadeIn();
        AudioManager.Instance.FadeOutAmbientAudio();
        SFXController.Instance.Play(transitionToGameplayStinger);

        yield return new WaitForSeconds(1);

        MainMenuController.Instance.TransitionToGameplay();
        MainMenuController.Instance.FadeOutCutsceneMusic();
        MainMenuController.Instance.StopMainMenuMusic();
        AudioManager.Instance.StartAmbientAudio();

        if (IsServer)
        {
            SpawnAllPlayersAtLobby();
        }

        UIController.Instance.FadeOut();
        OnGameStart?.Invoke();
    }
    
    public void ResetPlayerPositions()
    {
        PlayerInstance.Instance.transform.position =
            PlayerSpawner.Instance.GetSpawnPoint(
                PlayerInstance.Instance.OwnerClientId,
                IsSinglePlayer
            ).position;

        PlayerInstance.Instance.SetIsOutside(false);
    }

    // -------------------------------------------------------------------------
    // Restart Day
    // -------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // After a Restart Day scene reload the server auto-runs the full lobby
        // transition + game start sequence so all clients land back in gameplay
        // on the same saved day, without any manual interaction.
        if (IsServer && _isRestartingDay)
        {
            _isRestartingDay = false;
            StartCoroutine(RestartDaySequence());
        }
    }

    /// <summary>
    /// Reloads the active scene for all connected clients via NGO's network scene manager,
    /// preserving the network session and active save slot. After reload,
    /// <see cref="OnNetworkSpawn"/> automatically restarts the day from the host's save file —
    /// resuming at Dusk instead of repeating the whole day if the player died during
    /// <see cref="ShiftManager.DayPhase.PostShift"/>. SERVER ONLY.
    /// </summary>
    public void RestartDay()
    {
        if (!IsServer) return;

        _isRestartingDay = true;
        _restartDayPhase = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentPhase : ShiftManager.DayPhase.PreShift;
        string sceneName = SceneManager.GetActiveScene().name;

        if (NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }

    private IEnumerator RestartDaySequence()
    {
        // Wait one frame for the new scene's objects to fully initialize.
        yield return null;
        BeginLobbyTransition();
        TransitionToLobby();
        TryStartGame();

        if (_restartDayPhase != ShiftManager.DayPhase.PostShift)
            yield break;

        // The player died at Dusk — wait for the reloaded day's normal Dawn setup to finish
        // spawning in, then fast-forward past suspect processing straight back to Dusk instead
        // of making them redo the whole shift.
        yield return new WaitUntil(() =>
            ShiftManager.Instance != null &&
            CampaignManager.Instance != null &&
            CampaignManager.Instance.ActiveDay != null);

        yield return new WaitForSeconds(1f);

        ShiftManager.Instance.RestartIntoPostShiftPhase();
    }
}