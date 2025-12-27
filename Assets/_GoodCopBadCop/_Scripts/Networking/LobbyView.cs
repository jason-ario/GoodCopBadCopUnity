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

    private Lobby lobby;
    private FacepunchTransport transport;
    private StartCampaignScreen _startCampaignScreen;

    public void Setup(Lobby lobby, FacepunchTransport transport)
    {
        this.lobby = lobby;
        this.transport = transport;

        lobbyName.text = lobby.GetData("Host");
        players.text = $"{lobby.MemberCount}/{lobby.MaxMembers}";
    }

    public void JoinLobbyButtonPressed()
    {
        JoinLobby();
    }

    async void JoinLobby()
    {
        await lobby.Join();
        transport.targetSteamId = lobby.Owner.Id;
        
        NetworkManager.Singleton.StartClient();
        MainMenuController.Instance.startCampaignScreenScript.SetCurrentLobby(lobby);
        MainMenuController.Instance.OpenStartCampaignScreen(true);
    }
}