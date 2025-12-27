using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [SerializeField] private Animator rollingShutter;
    public UnityAction OnRoundStart;

    NetworkVariable<bool> levelStarted = new NetworkVariable<bool>();
    [SerializeField] WindowLampController windowLampController;
    [SerializeField] private AudioSource _buzzerSound;

    private void Awake()
    {
        Instance = this;
    }
    
    public void TryStartGame()
    {
        if (IsServer)
            StartGameServer();
        else
            RequestStartGameServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartGameServerRpc()
    {
        StartGameServer();
    }   
    
    private void StartGameServer()
    {
        if (!IsServer) return;
        levelStarted.Value = true;

        // 🔒 SERVER decides spawning
        SpawnPlayersServer();

        // 🔊 Tell all clients to transition
        StartGameClientRpc();
    }
    
    [ClientRpc]
    private void StartGameClientRpc()
    {
        UIController.Instance.ShowPlayerUI();

        MainMenuController.Instance.HideAllMenus();

        ResetWindow();
    }
    private void SpawnPlayersServer()
    {
        bool isSinglePlayer = NetworkManager.Singleton.ConnectedClientsList.Count == 1;
        
        Debug.Log("Is Single Player " + isSinglePlayer);

        Debug.Log("Connected Clients " + NetworkManager.Singleton.ConnectedClientsList.Count);
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerSpawner.Instance.SpawnPlayer(
                client.ClientId,
                isSinglePlayer
            );
        }
    }

    public override void OnNetworkSpawn()
    {
        if (levelStarted.Value)
        {
            rollingShutter.SetBool("Open", true);
            windowLampController.TurnGreen();
        }
    }

    // 🔘 CALL THIS FROM UI (client or host)
    public void TryStartLevel()
    {
        if (IsServer)
            StartLevel();
        else
            RequestStartLevelServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartLevelServerRpc()
    {
        StartLevel();
    }

    private void StartLevel()
    {
        if (!IsServer) return;
        if (levelStarted.Value) { return; }
        
        levelStarted.Value = true;
        StartLevelClientRpc();
    }

    [ClientRpc]
    private void StartLevelClientRpc()
    {
        StartCoroutine(OpenWindowSequence(true));
    }

    public void OpenWindow()
    {
        StartCoroutine(OpenWindowSequence(false));
    }
    
    public void ResetWindow()
    {
        windowLampController.TurnRed();
        rollingShutter.SetBool("Open", false);
    }

    IEnumerator OpenWindowSequence(bool startGame)
    {
        _buzzerSound.Play(); 
        yield return new WaitForSeconds(.5f);
        windowLampController.TurnGreen();

        yield return new WaitForSeconds(3);
        
        if (startGame)
        {
            OnRoundStart?.Invoke();
        }
        
        rollingShutter.SetBool("Open", true);
    }
    
    
}