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

    private bool _isSinglePlayer;
    public bool IsSinglePlayer => _isSinglePlayer;

    /// <summary>
    /// Set to true on the server before a Restart Day scene reload so that
    /// <see cref="OnNetworkSpawn"/> can auto-trigger the game start sequence after reload.
    /// Static so it survives the scene reload within the same AppDomain.
    /// </summary>
    private static bool _isRestartingDay;

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
    public void SetIntroCutsceneStarted() => HasIntroCutsceneStarted = true;

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

        MainMenuController.Instance.TransitionToGameplay();
        AudioManager.Instance.StartAmbientAudio();
        UIController.Instance.FadeOut();

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
        _isSinglePlayer = isSinglePlayer;

        PlayerSpawner.Instance.SpawnPlayerAtLobby(clientId, isSinglePlayer);
    }

    private void SpawnAllPlayersAtLobby()
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;
        _isSinglePlayer = isSinglePlayer;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerSpawner.Instance.SpawnPlayerAtLobby(client.ClientId, isSinglePlayer);
        }
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
        _isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;

        if (skipTransition)
        {
            MainMenuController.Instance.TransitionToGameplay();

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
                _isSinglePlayer
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
    /// <see cref="OnNetworkSpawn"/> automatically restarts the day from the host's save file.
    /// SERVER ONLY.
    /// </summary>
    public void RestartDay()
    {
        if (!IsServer) return;

        _isRestartingDay = true;
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
    }
}