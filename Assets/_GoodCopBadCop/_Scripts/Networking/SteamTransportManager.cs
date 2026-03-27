using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

public class SteamTransportManager : MonoBehaviour
{
    private void HandleTransport(SteamId id)
    {
        // Lorem Ipsum
        NetworkManager.Singleton.GetComponent<FacepunchTransport>().targetSteamId = id;
    }
    
   private void Start()
   {
       SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
   }
   
   private void OnDestroy()
   {
       SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
   }
   
   private void OnGameLobbyJoinRequested(Lobby lobby, SteamId id)
   {
       HandleTransport(id);
   }
   
}
