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
    [SerializeField] private AudioClip transitionToGameplayStinger;

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

        // 🔊 Tell all clients to transition
        StartGameClientRpc();
    }
    
    [ClientRpc]
    private void StartGameClientRpc()
    {
        UIController.Instance.ShowPlayerUI();
        StartCoroutine(TransitionToGameplay());

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

    IEnumerator TransitionToGameplay()
    {
        UIController.Instance.FadeIn();
        AudioManager.Instance.FadeOutAmbientAudio();
        SFXController.Instance.Play(transitionToGameplayStinger);
        // Loading
        yield return new WaitForSeconds(4);
        MainMenuController.Instance.TransitionToGameplay(); 
        AudioManager.Instance.StartAmbientAudio();

        if (IsServer)
        {
            SpawnPlayersServer();
        }
        
        //Game officially starts
        UIController.Instance.FadeOut();
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
        Debug.Log("Try Start Level");
        
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
        
        rollingShutter.SetBool("Open", true);
        
        yield return new WaitForSeconds(3);
        if (startGame)
        {
            OnRoundStart?.Invoke();
        }
    }
    
    
}