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
    [Header("Main UI")]
    public GameObject uiRoot;

    [Header("Lobby Browser UI")]
    public GameObject lobbyBrowserPanel;
    public Transform lobbyListParent;
    public LobbyRow lobbyRowPrefab;

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        OpenLobbyBrowser();
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    // =========================
    // HOST
    // =========================
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
            lobby.SetData("name", SteamClient.Name + "'s Lobby");

            facepunch.targetSteamId = lobby.Owner.Id; 

            NetworkManager.Singleton.StartHost();
        }
        // -------- UNITY TRANSPORT (LAN) --------
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData("127.0.0.1", 7777);
            NetworkManager.Singleton.StartHost();
        }
        else
        {
            Debug.LogError("Unsupported transport!");
            return;
        }
        CloseLobbyBrowser();

        uiRoot.SetActive(false);
    }

    // =========================
    // JOIN (OPEN LOBBY BROWSER)
    // =========================
    public void Join()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport)
        {
        }
        else if (transport is UnityTransport unityTransport)
        {
            // Simple LAN fallback
            unityTransport.SetConnectionData("127.0.0.1", 7777);

            uiRoot.SetActive(false);
        }
        
        CloseLobbyBrowser();
    }

    // =========================
    // LOBBY BROWSER
    // =========================
    async void OpenLobbyBrowser()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        if (transport is not FacepunchTransport facepunch)
            return;

        lobbyBrowserPanel.SetActive(true);

        // Clear old entries
        foreach (Transform child in lobbyListParent)
            Destroy(child.gameObject);

        Lobby[] lobbies = await SteamMatchmaking.LobbyList
            .WithSlotsAvailable(1)
            .RequestAsync();

        if (lobbies == null || lobbies.Length == 0)
        {
            Debug.Log("No lobbies found");
            return;
        }

        foreach (Lobby lobby in lobbies
                 .Where(l => !string.IsNullOrEmpty(l.GetData("created_at")))
                 .OrderByDescending(l => long.Parse(l.GetData("created_at"))))
        {
            LobbyRow row = Instantiate(lobbyRowPrefab, lobbyListParent);
            row.Setup(lobby);
        }
    }

    public void CloseLobbyBrowser()
    {
        lobbyBrowserPanel.SetActive(false);
    }

    // =========================
    // SPAWNING
    // =========================
    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        //PlayerSpawner.Instance.SpawnPlayer(clientId);
        Debug.Log("Player Joined");
    }
}
