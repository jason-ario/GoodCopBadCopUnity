using System;
using System.Collections.Generic;
using Dissonance;
using R3;
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
            comms.OnPlayerStartedSpeaking += OnPlayerStartedSpeaking;
            comms.OnPlayerStoppedSpeaking += OnPlayerStoppedSpeaking;

            PlayerVoiceChatAdapter.Registered += OnPlayerAdapterRegistered;
            PlayerVoiceChatAdapter.Unregistered += OnPlayerAdapterUnregistered;

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
        }

        public void Dispose()
        {
            PlayerVoiceChatAdapter.Registered -= OnPlayerAdapterRegistered;
            PlayerVoiceChatAdapter.Unregistered -= OnPlayerAdapterUnregistered;

            if (comms != null)
            {
                comms.OnPlayerStartedSpeaking -= OnPlayerStartedSpeaking;
                comms.OnPlayerStoppedSpeaking -= OnPlayerStoppedSpeaking;
            }

            disposables.Dispose();
            playerAdapters.Clear();
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
                comms.IsMuted = !enabled || model.IsMuted.CurrentValue;
                comms.IsDeafened = !enabled || model.IsDeafened.CurrentValue;
                comms.MicrophoneName = string.IsNullOrWhiteSpace(model.MicrophoneName.CurrentValue)
                    ? null
                    : model.MicrophoneName.CurrentValue;
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

        private void OnPlayerStartedSpeaking(VoicePlayerState playerState)
        {
            if (playerState != null && playerState.IsLocalPlayer)
            {
                service.SetLocalSpeaking(true);
            }
        }

        private void OnPlayerStoppedSpeaking(VoicePlayerState playerState)
        {
            if (playerState != null && playerState.IsLocalPlayer)
            {
                service.SetLocalSpeaking(false);
            }
        }
    }
}
