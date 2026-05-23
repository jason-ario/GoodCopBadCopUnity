using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

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

    private IEnumerator LobbyTransitionSequence()
    {
        UIController.Instance.FadeIn();
        AudioManager.Instance.FadeOutAmbientAudio();
        SFXController.Instance.Play(transitionToGameplayStinger);

        yield return new WaitForSeconds(2f);

        if (IsServer)
        {
            SpawnAllPlayersAtLobby();
            IsTransitioningToLobby = false;
        }

        MainMenuController.Instance.TransitionToGameplay();
        AudioManager.Instance.StartAmbientAudio();
        UIController.Instance.FadeOut();

        // Wait for the fade-out animation to finish before revealing the HUD.
        const float FadeOutDuration = 0.65f;
        yield return new WaitForSeconds(FadeOutDuration);

        UIController.Instance.ShowPlayerUI();
        OnGameStart?.Invoke();
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
    /// </summary>
    public void InitializeLobbyJoinClient(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[GameManager] InitializeLobbyJoinClient sending RPC to clientId={clientId}");

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        InitializeLobbyJoinClientRpc(rpcParams);
    }

    [ClientRpc]
    private void InitializeLobbyJoinClientRpc(ClientRpcParams clientRpcParams = default)
    {
        Debug.Log("[GameManager] InitializeLobbyJoinClientRpc received — starting lobby join sequence");
        StartCoroutine(LobbyJoinClientSequence());
    }

    private IEnumerator LobbyJoinClientSequence()
    {
        Debug.Log("[GameManager] LobbyJoinClientSequence: fading in");
        UIController.Instance.FadeIn();
        AudioManager.Instance.FadeOutAmbientAudio();

        yield return new WaitForSeconds(2f);

        Debug.Log("[GameManager] LobbyJoinClientSequence: transitioning to gameplay");
        MainMenuController.Instance.TransitionToGameplay();
        UIController.Instance.ShowPlayerUI();
        AudioManager.Instance.StartAmbientAudio();
        UIController.Instance.FadeOut();
        OnGameStart?.Invoke();
    }

    [ClientRpc]
    private void InitializeLateJoinClientRpc(ClientRpcParams clientRpcParams = default)
    {
        ShiftManager.Instance.StopIntroCutscene();
        UIController.Instance.ShowPlayerUI();
        MainMenuController.Instance.TransitionToGameplay();
    }

    [ClientRpc]
    private void StartGameClientRpc(bool skipTransition = false)
    {
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

            CampaignManager.Instance.StartCampaign();
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
}