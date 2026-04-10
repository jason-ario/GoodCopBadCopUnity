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
        StartGameClientRpc(skipTransition);
    }

    [ClientRpc]
    private void StartGameClientRpc(bool skipTransition = false)
    {
        UIController.Instance.ShowPlayerUI();
        StartCoroutine(TransitionToGameplay(skipTransition));
    }

    private void SpawnPlayersServer()
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;
        _isSinglePlayer = isSinglePlayer;

        Debug.Log("Is Single Player " + isSinglePlayer);
        Debug.Log("Connected Clients " + NetworkManager.Singleton.ConnectedClientsList.Count);

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerSpawner.Instance.SpawnPlayer(client.ClientId, isSinglePlayer);
        }
    }

    private IEnumerator TransitionToGameplay(bool skipTransition = false)
    {
        if (skipTransition)
        {
            MainMenuController.Instance.TransitionToGameplay();

            if (IsServer)
            {
                SpawnPlayersServer();
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
            SpawnPlayersServer();
        }

        ShiftManager.Instance.StartNewShift();

        UIController.Instance.FadeOut();
        OnGameStart?.Invoke();
    }

    public void DeliveredVerdict(StampContainer.StampType stampType)
    {
        verdictDelivered = stampType;
        SuspectController.Instance.SetCanInteract(false);

        switch (stampType)
        {
            case StampContainer.StampType.Pass:
                SuspectController.Instance.Pass();
                break;
            case StampContainer.StampType.Quarantine:
                SuspectController.Instance.Quarantine();
                break;
            case StampContainer.StampType.Kill:
                SuspectController.Instance.Kill();
                break;
        }
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