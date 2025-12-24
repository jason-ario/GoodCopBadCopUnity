using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class SimpleNetworkUI : MonoBehaviour
{
    public GameObject uiRoot;
    public string hostIP = "178.134.251.97"; // <-- HOST LAN IP
    [SerializeField] UnityTransport transport;
    [SerializeField] string ipAddress;

    
    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        ipAddress = "0.0.0.0";
        SetIpAddress(); // Set the Ip to the above address
    }



    
    void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    public void Host()
    {
        NetworkManager.Singleton.StartHost();
        GetLocalIPAddress();
        uiRoot.SetActive(false);
    }
    
    // To Join a game
    public void StartClient() {
        NetworkManager.Singleton.StartClient();
    }

    public void Join()
    {
        var transport = (UnityTransport)
            NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        SetIpAddress();
        transport.ConnectionData.Address = hostIP;

        NetworkManager.Singleton.StartClient();
        uiRoot.SetActive(false);
    }
    
    /* Gets the Ip Address of your connected network and
    shows on the screen in order to let other players join
    by inputing that Ip in the input field */
    // ONLY FOR HOST SIDE 
    public string GetLocalIPAddress() {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList) {
            if (ip.AddressFamily == AddressFamily.InterNetwork) {
                ipAddress = ip.ToString();
                Debug.Log(ipAddress);
                return ip.ToString();
            }
        }
        throw new System.Exception("No network adapters with an IPv4 address in the system!");
    }

    /* Sets the Ip Address of the Connection Data in Unity Transport
    to the Ip Address which was input in the Input Field */
    // ONLY FOR CLIENT SIDE
    public void SetIpAddress() {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.ConnectionData.Address = ipAddress;
    }

    void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        PlayerSpawner.Instance.SpawnPlayer(clientId);
    }
}