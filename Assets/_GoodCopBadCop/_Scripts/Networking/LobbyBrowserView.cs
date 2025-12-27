using System;
using System.Linq;
using Netcode.Transports.Facepunch;
using Steamworks;
using Unity.Netcode;
using UnityEngine;
using Steamworks.Data;

public class LobbyBrowserView : MonoBehaviour
{
    [SerializeField] private GameObject lobbyBrowserPanel;
    [SerializeField] Transform lobbyListParent;
    [SerializeField] LobbyRow lobbyRowPrefab;
    
    private void OnEnable()
    {
        OpenLobbyBrowser();
    }

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
            row.Setup(lobby, facepunch);
        }
    }
}
