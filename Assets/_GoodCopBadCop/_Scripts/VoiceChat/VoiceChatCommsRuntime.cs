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

            DissonanceComms[] existingComms = UnityEngine.Object.FindObjectsByType<DissonanceComms>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (existingComms.Length == 1)
            {
                comms = existingComms[0];
                return comms;
            }

            if (existingComms.Length > 1)
            {
                throw new InvalidOperationException($"Expected a single {nameof(DissonanceComms)} instance, found {existingComms.Length}.");
            }

            runtimeObject = new GameObject(RuntimeCommsObjectName);
            comms = runtimeObject.AddComponent<DissonanceComms>();
            runtimeObject.AddComponent<NfgoCommsNetwork>();
            return comms;
        }
    }
}
