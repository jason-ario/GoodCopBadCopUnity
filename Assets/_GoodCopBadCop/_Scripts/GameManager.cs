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

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (levelStarted.Value)
            rollingShutter.SetBool("Open", true);
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
        levelStarted.Value = true;
        StartLevelClientRpc();
    }

    [ClientRpc]
    private void StartLevelClientRpc()
    {
        OnRoundStart?.Invoke();
        rollingShutter.SetBool("Open", true);
    }
}