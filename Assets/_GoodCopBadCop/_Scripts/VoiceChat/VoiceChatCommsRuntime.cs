using System;
using Dissonance;
using Dissonance.Integrations.Unity_NFGO;
using UnityEngine;
using VContainer.Unity;

namespace GoodCopBadCop.VoiceChat
{
    public interface IVoiceChatCommsRuntime
    {
        DissonanceComms Comms { get; }
    }

    public sealed class VoiceChatCommsRuntime : IVoiceChatCommsRuntime, IInitializable, IDisposable
    {
        private const string RuntimeCommsObjectName = "---Voice Chat";

        private GameObject runtimeObject;
        private DissonanceComms comms;

        public DissonanceComms Comms => EnsureComms();

        public void Initialize()
        {
            EnsureComms();
        }

        public void Dispose()
        {
            if (runtimeObject != null)
            {
                UnityEngine.Object.Destroy(runtimeObject);
                runtimeObject = null;
                comms = null;
            }
        }

        private DissonanceComms EnsureComms()
        {
            if (comms != null)
            {
                return comms;
            }

            runtimeObject = new GameObject(RuntimeCommsObjectName);
            runtimeObject.AddComponent<NfgoCommsNetwork>();
            comms = runtimeObject.AddComponent<DissonanceComms>();
            return comms;
        }
    }
}
