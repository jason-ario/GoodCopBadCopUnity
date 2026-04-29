using Steamworks.Data;
using TMPro;
using UnityEngine;

public class LobbyRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyName;
    [SerializeField] private TextMeshProUGUI players;

    private Lobby lobby;

    public void Setup(Lobby lobby)
    {
        this.lobby = lobby;

        // Defensive: metadata might be missing
        lobbyName.text = lobby.GetData("host");
        players.text = $"{lobby.MemberCount}/{lobby.MaxMembers}";
    }

    // Wired to the Join button
    public void JoinLobbyButtonPressed()
    {
        if (lobby.Id == 0)
        {
            Debug.LogError("Invalid lobby");
            return;
        }

        Debug.Log($"Joining lobby {lobby.Id}");

        // 🔑 ALL Steam + Netcode logic lives here now
        LobbyManager.Instance.JoinLobby(lobby.Id);

        // ✅ UI transition only (client waiting screen)
       //MainMenuController.Instance.OpenStartCampaignAsClient();
    }
}