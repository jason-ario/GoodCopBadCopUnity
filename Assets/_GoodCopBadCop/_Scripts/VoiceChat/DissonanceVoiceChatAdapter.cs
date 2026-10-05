using System;
using System.Collections.Generic;
using Dissonance;
using GoodCopBadCop.Input;
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
        private bool voiceActivatedForSession;
        private bool lobbyTriggerUserMuted;

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
            RefreshCommsReference();

            service.SetCommsAvailable(comms != null);
            service.SetNetworkReady(comms != null && comms.IsNetworkInitialized);

            bool hasRemotePeer = HasRemoteNetworkPeer();
            UpdateSessionActivation(hasRemotePeer);
            UpdatePushToTalkGate();

            // TODO: If all players leave the lobby, the microphone indicator can remain visible;
            // handle lobby/network disconnect events and force local speaking off.
            bool localSpeaking = hasRemotePeer && HasActiveTransmission();
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
            // No ducking: remote voices were dropped 6 dB whenever the local mic transmitted
            // (including voice-activation false triggers), making voice chat hard to hear.
            settings.VoiceDuckLevel = 1f;
            // Speaker users: the MainMix master group carries the Dissonance Echo Cancellation
            // capture filter, so game audio (music) leaking into the mic is subtracted before encoding.
            settings.AecSuppressionAmount = Dissonance.Audio.Capture.AecSuppressionLevels.Low;
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
            lobbyTriggerUserMuted = muted;
            lobbyBroadcastTrigger.IsMuted = muted || IsPushToTalkGateClosed();
            lobbyBroadcastTrigger.enabled = enabled;

            lobbyReceiptTrigger.RoomName = LobbyRoomName;
            lobbyReceiptTrigger.UseColliderTrigger = false;
            lobbyReceiptTrigger.enabled = enabled;
        }

        /// <summary>
        /// Push-to-talk is gated here rather than by Dissonance's own PushToTalk mode. That mode polls
        /// legacy <c>Input.GetAxis(InputName)</c>, which can't follow the rebindable
        /// <see cref="GameAction.PushToTalk"/> key. Instead the trigger runs in Open mode and stays
        /// muted unless the bound key is held.
        /// </summary>
        private void UpdatePushToTalkGate()
        {
            if (lobbyBroadcastTrigger == null)
            {
                return;
            }

            bool targetMuted = lobbyTriggerUserMuted || IsPushToTalkGateClosed();
            if (lobbyBroadcastTrigger.IsMuted != targetMuted)
            {
                lobbyBroadcastTrigger.IsMuted = targetMuted;
            }
        }

        private bool IsPushToTalkGateClosed()
        {
            return model.InputMode.CurrentValue == EVoiceChatInputMode.PushToTalk
                && !RebindableInput.GetKeyHeld(GameAction.PushToTalk);
        }

        private static CommActivationMode ToDissonanceMode(EVoiceChatInputMode mode)
        {
            switch (mode)
            {
                case EVoiceChatInputMode.PushToTalk:
                    // Open + UpdatePushToTalkGate muting = rebindable push-to-talk.
                    return CommActivationMode.Open;
                case EVoiceChatInputMode.OpenMic:
                    return CommActivationMode.Open;
                default:
                    return CommActivationMode.VoiceActivation;
            }
        }

        /// <summary>
        /// Voice chat (DissonanceComms, and therefore the microphone) stays off while the local player
        /// is alone, because opening the mic forces Bluetooth headsets into their low-quality
        /// hands-free profile. The first time another player joins the network session it is switched
        /// on and then stays on for the rest of that session, even if the other players leave.
        /// When the session ends (lobby left / NetworkManager shut down) it switches off again so the
        /// next solo session starts with the mic closed.
        /// Turning voice chat off in settings also switches DissonanceComms off (OnDisable stops the
        /// capture pipeline and calls Microphone.End). Muting alone doesn't close the mic, so without
        /// this the headset stayed in hands-free mode after voice chat was disabled mid-game.
        /// The GameObject stays active so NfgoPlayer can still find the component on spawn.
        /// </summary>
        private void UpdateSessionActivation(bool hasRemotePeer)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            bool sessionRunning = networkManager != null && networkManager.IsListening;

            if (!sessionRunning)
            {
                if (voiceActivatedForSession)
                {
                    voiceActivatedForSession = false;
                    Debug.Log("[VoiceChat] Network session ended - voice chat switched off.");
                }
            }
            else if (!voiceActivatedForSession && hasRemotePeer)
            {
                voiceActivatedForSession = true;
                Debug.Log("[VoiceChat] Another player joined - voice chat switched on for this session.");
            }

            SetCommsActive(voiceActivatedForSession && model.IsEnabled.CurrentValue);
        }

        private void SetCommsActive(bool active)
        {
            if (comms == null || comms.enabled == active)
            {
                return;
            }

            comms.enabled = active;
            if (active)
            {
                // Re-apply mic/mute/trigger state so the lobby room is joined as soon as Dissonance starts.
                ApplySettings();
            }
        }

        /// <summary>
        /// Keeps the cached DissonanceComms in sync with the runtime (e.g. if it was destroyed and
        /// recreated during a scene/network transition) so settings and lobby triggers are never
        /// applied to a dead object.
        /// </summary>
        private void RefreshCommsReference()
        {
            DissonanceComms currentComms = commsRuntime.Comms;
            if (currentComms == comms)
            {
                return;
            }

            comms = currentComms;
            lobbyBroadcastTrigger = null;
            lobbyReceiptTrigger = null;
            ApplySettings();
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
