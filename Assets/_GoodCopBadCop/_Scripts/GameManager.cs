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

    private void Awake()
    {
        Instance = this;
    }

    public void TryStartGame(bool skipTransition = false)
    {
        if (IsServer)
            StartGameServer(skipTransition);
        else
            RequestStartGameServerRpc(skipTransition);
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
        UIController.Instance.ShowPlayerUI();
        StartCoroutine(TransitionToGameplay(skipTransition));
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

            StoryProgressionManager.Instance.StartGame();
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
    }
}