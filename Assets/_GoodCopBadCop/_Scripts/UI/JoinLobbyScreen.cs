using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

public class JoinCampaignScreen : MonoBehaviour
{
    [SerializeField] TMP_InputField inviteCodeInput;
    
    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
    }
    
    public async void JoinWithCode()
    {
        if (!ulong.TryParse(inviteCodeInput.text, out ulong lobbyId))
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