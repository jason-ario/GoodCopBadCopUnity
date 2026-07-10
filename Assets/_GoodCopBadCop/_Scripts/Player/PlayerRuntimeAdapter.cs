using System;
using Unity.Netcode;
using VContainer.Unity;

namespace GoodCopBadCop.Player
{
    public sealed class PlayerRuntimeAdapter : IInitializable, ITickable, IDisposable
    {
        private readonly IPlayerRuntimeService playerRuntimeService;
        private NetworkManager networkManager;
        private bool isSubscribed;

        public PlayerRuntimeAdapter(IPlayerRuntimeService playerRuntimeService)
        {
            this.playerRuntimeService = playerRuntimeService;
        }

        public void Initialize()
        {
            RefreshNetworkManager();
            RefreshLocalPlayer();
        }

        public void Tick()
        {
            RefreshNetworkManager();
            RefreshLocalPlayer();
        }

        public void Dispose()
        {
            UnsubscribeNetworkManager();
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

        private void RefreshNetworkManager()
        {
            NetworkManager currentNetworkManager = NetworkManager.Singleton;
            if (networkManager == currentNetworkManager)
            {
                SubscribeNetworkManager();
                return;
            }

            UnsubscribeNetworkManager();
            networkManager = currentNetworkManager;
            SubscribeNetworkManager();
        }

        private void SubscribeNetworkManager()
        {
            if (networkManager == null || isSubscribed)
            {
                return;
            }

            networkManager.OnClientStarted += RefreshLocalPlayer;
            networkManager.OnClientStopped += OnClientStopped;
            networkManager.OnClientConnectedCallback += OnClientStateChanged;
            networkManager.OnClientDisconnectCallback += OnClientStateChanged;
            isSubscribed = true;
        }

        private void UnsubscribeNetworkManager()
        {
            if (networkManager == null || !isSubscribed)
            {
                return;
            }

            networkManager.OnClientStarted -= RefreshLocalPlayer;
            networkManager.OnClientStopped -= OnClientStopped;
            networkManager.OnClientConnectedCallback -= OnClientStateChanged;
            networkManager.OnClientDisconnectCallback -= OnClientStateChanged;
            isSubscribed = false;
        }
    }
}
