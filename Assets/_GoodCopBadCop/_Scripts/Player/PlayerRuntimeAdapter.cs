using System;
using Unity.Netcode;
using VContainer.Unity;

namespace GoodCopBadCop.Player
{
    public sealed class PlayerRuntimeAdapter : IInitializable, IDisposable
    {
        private readonly IPlayerRuntimeService playerRuntimeService;
        private NetworkManager networkManager;

        public PlayerRuntimeAdapter(IPlayerRuntimeService playerRuntimeService)
        {
            this.playerRuntimeService = playerRuntimeService;
        }

        public void Initialize()
        {
            networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                playerRuntimeService.SetLocalPlayer(null);
                return;
            }

            networkManager.OnClientStarted += RefreshLocalPlayer;
            networkManager.OnClientStopped += OnClientStopped;
            networkManager.OnClientConnectedCallback += OnClientStateChanged;
            networkManager.OnClientDisconnectCallback += OnClientStateChanged;

            RefreshLocalPlayer();
        }

        public void Dispose()
        {
            if (networkManager == null)
            {
                return;
            }

            networkManager.OnClientStarted -= RefreshLocalPlayer;
            networkManager.OnClientStopped -= OnClientStopped;
            networkManager.OnClientConnectedCallback -= OnClientStateChanged;
            networkManager.OnClientDisconnectCallback -= OnClientStateChanged;
        }

        private void OnClientStateChanged(ulong _)
        {
            RefreshLocalPlayer();
        }

        private void OnClientStopped(bool _)
        {
            playerRuntimeService.SetLocalPlayer(null);
        }

        private void RefreshLocalPlayer()
        {
            NetworkObject playerObject = networkManager != null && networkManager.SpawnManager != null
                ? networkManager.SpawnManager.GetLocalPlayerObject()
                : null;

            playerRuntimeService.SetLocalPlayer(playerObject != null
                ? playerObject.GetComponent<global::PlayerInstance>()
                : null);
        }
    }
}
