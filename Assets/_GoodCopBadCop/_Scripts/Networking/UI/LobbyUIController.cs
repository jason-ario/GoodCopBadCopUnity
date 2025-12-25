using System;
using System.Linq;
using System.Threading.Tasks;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

public class LobbyBrowser : MonoBehaviour
{
    [Header("UI")]
    public GameObject root;
    public Transform contentRoot;
    public LobbyRow lobbyRowPrefab;

    FacepunchTransport transport;

    void Start()
    {
        transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as FacepunchTransport;
        NetworkManager.Singleton.OnClientStarted += OnClientStarted;
        Open();
    }

    private void OnClientStarted()
    {
        Close();
    }

    public async void Open()
    {
        if (transport == null)
        {
            Debug.LogError("LobbyBrowser requires Facepunch transport");
            return;
        }

        root.SetActive(true);
        await Refresh();
    }

    public void Close()
    {
        root.SetActive(false);
    }

    public async Task Refresh()
    {
        Clear();

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
            LobbyRow row = Instantiate(lobbyRowPrefab, contentRoot);
            row.Setup(lobby, transport);
        }
    }

    void Clear()
    {
        foreach (Transform child in contentRoot)
            Destroy(child.gameObject);
    }
}
