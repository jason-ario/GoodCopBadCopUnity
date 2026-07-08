using System;
using Dissonance;
using Dissonance.Integrations.Unity_NFGO;
using UnityEngine;

namespace GoodCopBadCop.VoiceChat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NfgoPlayer))]
    [RequireComponent(typeof(VoiceProximityBroadcastTrigger))]
    [RequireComponent(typeof(VoiceProximityReceiptTrigger))]
    public sealed class PlayerVoiceChatAdapter : MonoBehaviour
    {
        [SerializeField] private string roomName = "GoodCopBadCopProximity";
        [SerializeField, Range(1, 100)] private int fallbackProximityRange = 10;

        private VoiceProximityBroadcastTrigger broadcastTrigger;
        private VoiceProximityReceiptTrigger receiptTrigger;
        private bool hasAppliedSettings;
        private bool isEnabled = true;
        private bool isMuted;
        private EVoiceChatInputMode inputMode = EVoiceChatInputMode.VoiceActivation;
        private int proximityRange;
        private bool isRegistered;

        public static event Action<PlayerVoiceChatAdapter> Registered;
        public static event Action<PlayerVoiceChatAdapter> Unregistered;

        private void Awake()
        {
            broadcastTrigger = GetComponent<VoiceProximityBroadcastTrigger>();
            receiptTrigger = GetComponent<VoiceProximityReceiptTrigger>();
            proximityRange = fallbackProximityRange;
            SetTriggersEnabled(false);
        }

        private void OnEnable()
        {
            RegisterInstance();

            if (hasAppliedSettings)
            {
                ApplyToTriggers();
            }
        }

        private void OnDisable()
        {
            UnregisterInstance();

            if (broadcastTrigger != null)
            {
                broadcastTrigger.enabled = false;
            }

            if (receiptTrigger != null)
            {
                receiptTrigger.enabled = false;
            }
        }

        private void OnDestroy()
        {
            UnregisterInstance();
        }

        public void ApplySettings(
            bool enabled,
            bool muted,
            EVoiceChatInputMode mode,
            int range)
        {
            isEnabled = enabled;
            isMuted = muted;
            inputMode = mode;
            proximityRange = Mathf.Clamp(
                range,
                VoiceChatService.MinimumProximityRange,
                VoiceChatService.MaximumProximityRange);
            hasAppliedSettings = true;

            ApplyToTriggers();
        }

        private void RegisterInstance()
        {
            if (isRegistered)
            {
                return;
            }

            isRegistered = true;
            Registered?.Invoke(this);
        }

        private void UnregisterInstance()
        {
            if (!isRegistered)
            {
                return;
            }

            isRegistered = false;
            Unregistered?.Invoke(this);
        }

        private void ApplyToTriggers()
        {
            if (broadcastTrigger == null || receiptTrigger == null)
            {
                return;
            }

            broadcastTrigger.RoomName = roomName;
            receiptTrigger.RoomName = roomName;
            broadcastTrigger.Range = proximityRange;
            receiptTrigger.Range = proximityRange;
            broadcastTrigger.UseColliderTrigger = false;
            receiptTrigger.UseColliderTrigger = false;
            broadcastTrigger.Mode = ToDissonanceMode(inputMode);
            broadcastTrigger.IsMuted = !isEnabled || isMuted;
            SetTriggersEnabled(isEnabled && isActiveAndEnabled);
        }

        private void SetTriggersEnabled(bool enabled)
        {
            if (broadcastTrigger != null)
            {
                broadcastTrigger.enabled = enabled;
            }

            if (receiptTrigger != null)
            {
                receiptTrigger.enabled = enabled;
            }
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
    }
}
