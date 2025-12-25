using Steamworks.Data;
using Netcode.Transports.Facepunch;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyRow : MonoBehaviour
{
    public TextMeshProUGUI lobbyName;
    public TextMeshProUGUI players;
    public Button joinButton;

    private Lobby lobby;
    private FacepunchTransport transport;

    public void Setup(Lobby lobby, FacepunchTransport transport)
    {
        this.lobby = lobby;
        this.transport = transport;

        lobbyName.text = lobby.GetData("name");
        players.text = $"{lobby.MemberCount}/{lobby.MaxMembers}";

        joinButton.onClick.AddListener(JoinLobby);
    }

    async void JoinLobby()
    {
        await lobby.Join();
        transport.targetSteamId = lobby.Owner.Id;
        NetworkManager.Singleton.StartClient();
    }
}