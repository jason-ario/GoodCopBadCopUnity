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
        private const string LobbyRoomName = "GoodCopBadCopLobby";

        private DissonanceComms comms;
        private VoiceBroadcastTrigger lobbyBroadcastTrigger;
        private VoiceReceiptTrigger lobbyReceiptTrigger;
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
            ApplyVoiceQualitySettings();
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

            // TODO: If all players leave the lobby, the microphone indicator can remain visible;
            // handle lobby/network disconnect events and force local speaking off.
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

                ApplyLobbyTriggers(enabled, targetMuted);
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

        private static void ApplyVoiceQualitySettings()
        {
            // Must run before DissonanceComms starts capture/encoding for these to take effect.
            Dissonance.Config.VoiceSettings settings = Dissonance.Config.VoiceSettings.Instance;
            settings.Quality = AudioQuality.High;
            settings.DenoiseAmount = Dissonance.Audio.Capture.NoiseSuppressionLevels.Low;
            settings.BackgroundSoundRemovalEnabled = false;
        }

        private void ApplyLobbyTriggers(bool enabled, bool muted)
        {
            if (lobbyBroadcastTrigger == null)
            {
                GameObject commsObject = comms.gameObject;
                if (!commsObject.TryGetComponent(out lobbyBroadcastTrigger))
                {
                    lobbyBroadcastTrigger = commsObject.AddComponent<VoiceBroadcastTrigger>();
                }

                if (!commsObject.TryGetComponent(out lobbyReceiptTrigger))
                {
                    lobbyReceiptTrigger = commsObject.AddComponent<VoiceReceiptTrigger>();
                }
            }

            // Lobby-wide, non-positional voice: independent of whether a player character is spawned.
            lobbyBroadcastTrigger.ChannelType = CommTriggerTarget.Room;
            lobbyBroadcastTrigger.RoomName = LobbyRoomName;
            lobbyBroadcastTrigger.BroadcastPosition = false;
            lobbyBroadcastTrigger.UseColliderTrigger = false;
            lobbyBroadcastTrigger.Mode = ToDissonanceMode(model.InputMode.CurrentValue);
            lobbyBroadcastTrigger.IsMuted = muted;
            lobbyBroadcastTrigger.enabled = enabled;

            lobbyReceiptTrigger.RoomName = LobbyRoomName;
            lobbyReceiptTrigger.UseColliderTrigger = false;
            lobbyReceiptTrigger.enabled = enabled;
        }

        private static CommActivationMode ToDissonanceMode(EVoiceChatInputMode mode)
        {
            switch (mode)
            {
                case EVoiceChatInputMode.PushToTalk:
                    return CommActivationMode.PushToTalk;
                case EVoiceChatInputMode.OpenMic:
                    return CommActivationMode.Open;
                default:
                    return CommActivationMode.VoiceActivation;
            }
        }

        private bool HasActiveTransmission()
        {
            return lobbyBroadcastTrigger != null
                && lobbyBroadcastTrigger.isActiveAndEnabled
                && lobbyBroadcastTrigger.IsTransmitting;
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
                return DevSpectatorRegistry.PlayerClientCount(networkManager) > 1;
            }

            return networkManager.IsClient && networkManager.IsConnectedClient;
        }
    }
}
