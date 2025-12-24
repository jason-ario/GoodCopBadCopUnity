using System;
using System.Linq;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SimpleNetworkUI : MonoBehaviour
{
    public GameObject uiRoot;

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    // --------------------
    // HOST
    // --------------------
    public async void Host()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        // -------- STEAM / FACEPUNCH --------
        if (transport is FacepunchTransport facepunch)
        {
            Lobby lobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);
            lobby.SetPublic();

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            lobby.SetData("created_at", timestamp.ToString());

            facepunch.targetSteamId = lobby.Owner.Id;

            NetworkManager.Singleton.StartHost();
        }
        // -------- UNITY TRANSPORT (LAN) --------
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData(
                "127.0.0.1",
                7777   // Server listen port
            );
            NetworkManager.Singleton.StartHost();
        }
        else
        {
            Debug.LogError("Unsupported transport!");
            return;
        }

        uiRoot.SetActive(false);
    }

    // --------------------
    // JOIN
    // --------------------
    public async void Join()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        // -------- STEAM / FACEPUNCH --------
        if (transport is FacepunchTransport facepunch)
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

            facepunch.targetSteamId = newestLobby.Owner.Id;

            NetworkManager.Singleton.StartClient();
        }
        // -------- UNITY TRANSPORT (LAN) --------
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData(
                "127.0.0.1",
                7777   // Server port
            );
            NetworkManager.Singleton.StartClient();
            uiRoot.SetActive(false);        
        }
        else
        {
            Debug.LogError("Unsupported transport!");
            return;
        }

        uiRoot.SetActive(false);
    }

    // --------------------
    // SPAWNING
    // --------------------
    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        PlayerSpawner.Instance.SpawnPlayer(clientId);
        Debug.Log("Spawn Player");
    }
    
}
