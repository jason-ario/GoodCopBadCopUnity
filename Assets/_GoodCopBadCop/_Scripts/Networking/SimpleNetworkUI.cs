using Unity.Netcode;
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

    public void Host()
    {
        NetworkManager.Singleton.StartHost();
        uiRoot.SetActive(false);
    }

    public void Join()
    {
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