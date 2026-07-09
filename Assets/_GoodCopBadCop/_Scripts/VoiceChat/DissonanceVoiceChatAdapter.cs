using System;
using System.Collections.Generic;
using Dissonance;
using R3;
using Unity.Netcode;
using UnityEngine;
using VContainer.Unity;

namespace GoodCopBadCop.VoiceChat
{
    public sealed class DissonanceVoiceChatAdapter : IInitializable, ITickable, IDisposable
    {
        private readonly IVoiceChatModel model;
        private readonly IVoiceChatService service;
        private readonly IVoiceChatCommsRuntime commsRuntime;
        private readonly HashSet<PlayerVoiceChatAdapter> playerAdapters = new();
        private DissonanceComms comms;
        private DisposableBag disposables;
        private bool appliedLocalSpeaking;

        public DissonanceVoiceChatAdapter(
            IVoiceChatModel model,
            IVoiceChatService service,
            IVoiceChatCommsRuntime commsRuntime)
        {
            this.model = model;
            this.service = service;
            this.commsRuntime = commsRuntime;
        }

        public void Initialize()
        {
            comms = commsRuntime.Comms;

            PlayerVoiceChatAdapter.Registered += OnPlayerAdapterRegistered;
            PlayerVoiceChatAdapter.Unregistered += OnPlayerAdapterUnregistered;
            RegisterExistingPlayerAdapters();

            model.IsEnabled.Subscribe(_ => ApplySettings()).AddTo(ref disposables);
            model.IsMuted.Subscribe(_ => ApplySettings()).AddTo(ref disposables);
            model.IsDeafened.Subscribe(_ => ApplySettings()).AddTo(ref disposables);
            model.InputMode.Subscribe(_ => ApplySettings()).AddTo(ref disposables);
            model.ProximityRange.Subscribe(_ => ApplySettings()).AddTo(ref disposables);
            model.MicrophoneName.Subscribe(_ => ApplySettings()).AddTo(ref disposables);

            ApplySettings();
        }

        public void Tick()
        {
            service.SetCommsAvailable(commsRuntime.Comms != null);
            service.SetNetworkReady(commsRuntime.Comms != null && commsRuntime.Comms.IsNetworkInitialized);

            bool localSpeaking = HasRemoteNetworkPeer() && HasActiveTransmission();
            if (appliedLocalSpeaking != localSpeaking)
            {
                appliedLocalSpeaking = localSpeaking;
                service.SetLocalSpeaking(localSpeaking);
            }
        }

        public void Dispose()
        {
            PlayerVoiceChatAdapter.Registered -= OnPlayerAdapterRegistered;
            PlayerVoiceChatAdapter.Unregistered -= OnPlayerAdapterUnregistered;

            service.SetLocalSpeaking(false);
            disposables.Dispose();
            playerAdapters.Clear();
        }

        private void RegisterExistingPlayerAdapters()
        {
            PlayerVoiceChatAdapter[] existingAdapters = UnityEngine.Object.FindObjectsByType<PlayerVoiceChatAdapter>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (PlayerVoiceChatAdapter existingAdapter in existingAdapters)
            {
                OnPlayerAdapterRegistered(existingAdapter);
            }
        }

        private void OnPlayerAdapterRegistered(PlayerVoiceChatAdapter playerAdapter)
        {
            if (playerAdapter == null || !playerAdapters.Add(playerAdapter))
            {
                return;
            }

            ApplySettings(playerAdapter);
        }

        private void OnPlayerAdapterUnregistered(PlayerVoiceChatAdapter playerAdapter)
        {
            if (playerAdapter != null)
            {
                playerAdapters.Remove(playerAdapter);
            }
        }

        private void ApplySettings()
        {
            if (comms != null)
            {
                bool enabled = model.IsEnabled.CurrentValue;
                bool targetMuted = !enabled || model.IsMuted.CurrentValue;
                bool targetDeafened = !enabled || model.IsDeafened.CurrentValue;
                string targetMicrophoneName = string.IsNullOrWhiteSpace(model.MicrophoneName.CurrentValue)
                    ? null
                    : model.MicrophoneName.CurrentValue;

                if (comms.IsMuted != targetMuted)
                {
                    comms.IsMuted = targetMuted;
                }

                if (comms.IsDeafened != targetDeafened)
                {
                    comms.IsDeafened = targetDeafened;
                }

                if (comms.MicrophoneName != targetMicrophoneName)
                {
                    comms.MicrophoneName = targetMicrophoneName;
                }
            }

            foreach (PlayerVoiceChatAdapter playerAdapter in playerAdapters)
            {
                ApplySettings(playerAdapter);
            }
        }

        private void ApplySettings(PlayerVoiceChatAdapter playerAdapter)
        {
            playerAdapter.ApplySettings(
                model.IsEnabled.CurrentValue,
                model.IsMuted.CurrentValue,
                model.InputMode.CurrentValue,
                model.ProximityRange.CurrentValue);
        }

        private bool HasActiveTransmission()
        {
            foreach (PlayerVoiceChatAdapter playerAdapter in playerAdapters)
            {
                if (playerAdapter != null && playerAdapter.IsTransmitting)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRemoteNetworkPeer()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return false;
            }

            if (networkManager.IsHost || networkManager.IsServer)
            {
                return networkManager.ConnectedClientsIds.Count > 1;
            }

            return networkManager.IsClient && networkManager.IsConnectedClient;
        }
    }
}
