using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

public class SimpleNetworkUI : MonoBehaviour
{
    public GameObject uiRoot;
    [SerializeField] string ipAddress;

    
    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }
    
    
    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    public async void Host()
    {
        Lobby lobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);
        lobby.SetPublic();

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lobby.SetData("created_at", timestamp.ToString());

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport
            as FacepunchTransport;

        transport.targetSteamId = lobby.Owner.Id;

        NetworkManager.Singleton.StartHost();
        uiRoot.SetActive(false);
    }

    public async void Join()
    {
        Lobby[] lobbies = await SteamMatchmaking.LobbyList
            .WithSlotsAvailable(1)
            .RequestAsync();

        if (lobbies == null || lobbies.Length == 0)
        {
            Debug.Log("No lobbies found");
            return;
        }

        Lobby newestLobby = lobbies
            .Where(l => l.GetData("created_at") != null)
            .OrderByDescending(l => long.Parse(l.GetData("created_at")))
            .First();

        await newestLobby.Join();

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport
            as FacepunchTransport;

        transport.targetSteamId = newestLobby.Owner.Id;

        NetworkManager.Singleton.StartClient();
        uiRoot.SetActive(false);
    }

    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        PlayerSpawner.Instance.SpawnPlayer(clientId);
    }
}