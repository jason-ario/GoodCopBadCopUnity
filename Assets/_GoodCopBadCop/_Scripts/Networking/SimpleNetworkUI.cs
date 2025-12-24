using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SimpleNetworkUI : MonoBehaviour
{
    public GameObject uiRoot;
    public string hostIP = "178.134.251.97"; // <-- HOST LAN IP

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    public void Host()
    {
        NetworkManager.Singleton.StartHost();
        uiRoot.SetActive(false);
    }

    public void Join()
    {
        var transport = (UnityTransport)
            NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        transport.ConnectionData.Address = hostIP;

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