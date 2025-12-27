using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;

public class JoinCampaignScreen : MonoBehaviour
{
    public async void JoinWithCode(string code)
    {
        if (!ulong.TryParse(code, out ulong lobbyId))
        {
            Debug.LogError("Invalid invite code");
            return;
        }

        Lobby lobby = new Lobby(lobbyId);

        Debug.Log("Joining lobby...");
        await lobby.Join();

        Debug.Log("Starting client...");
        NetworkManager.Singleton.StartClient();
    }
}