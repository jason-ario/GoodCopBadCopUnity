using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.RoomSystem
{
    public interface IRoomService
    {
        float TemperatureOffset { get; }
        void SetTemperatureOffset(object source, float offset);
        void ResetTemperatureOffset(object source);
        void StartFlickeringLights(object source);
        void StopFlickeringLights(object source);
        void StartInsectSwarm(object source);
        void StopInsectSwarm(object source);
    }

    public sealed class RoomService : IRoomService
    {
        private readonly Dictionary<object, float> temperatureOffsets = new Dictionary<object, float>();
        private readonly HashSet<object> flickeringLightSources = new HashSet<object>();
        private readonly HashSet<object> insectSwarmSources = new HashSet<object>();

        private global::BoothFlickeringLightsController lightsController;
        private global::CockroachSpawner cockroachSpawner;

        public float TemperatureOffset { get; private set; }

        public void SetTemperatureOffset(object source, float offset)
        {
            if (source == null)
            {
                Debug.LogWarning("[RoomService] Temperature offset source cannot be null.");
                return;
            }

            temperatureOffsets[source] = offset;
            RecalculateTemperatureOffset();
        }

        public void ResetTemperatureOffset(object source)
        {
            if (source == null)
            {
                Debug.LogWarning("[RoomService] Temperature offset source cannot be null.");
                return;
            }

            if (temperatureOffsets.Remove(source))
            {
                RecalculateTemperatureOffset();
            }
        }

        public void StartFlickeringLights(object source)
        {
            if (!AddSource(flickeringLightSources, source, "[RoomService] Flickering lights source cannot be null."))
            {
                return;
            }

            if (flickeringLightSources.Count == 1)
            {
                ResolveLightsController(warnIfMissing: true)?.StartFlickering();
            }
        }

        public void StopFlickeringLights(object source)
        {
            if (!RemoveSource(flickeringLightSources, source, "[RoomService] Flickering lights source cannot be null."))
            {
                return;
            }

            if (flickeringLightSources.Count == 0)
            {
                ResolveLightsController(warnIfMissing: false)?.StopFlickering();
            }
        }

        public void StartInsectSwarm(object source)
        {
            if (!AddSource(insectSwarmSources, source, "[RoomService] Insect swarm source cannot be null."))
            {
                return;
            }

            if (insectSwarmSources.Count == 1)
            {
                ResolveCockroachSpawner(warnIfMissing: true)?.StartSwarm();
            }
        }

        public void StopInsectSwarm(object source)
        {
            if (!RemoveSource(insectSwarmSources, source, "[RoomService] Insect swarm source cannot be null."))
            {
                return;
            }

            if (insectSwarmSources.Count == 0)
            {
                ResolveCockroachSpawner(warnIfMissing: false)?.StopSwarm();
            }
        }

        private void RecalculateTemperatureOffset()
        {
            var total = 0f;
            foreach (var offset in temperatureOffsets.Values)
            {
                total += offset;
            }

            TemperatureOffset = total;
        }

        private static bool AddSource(HashSet<object> sources, object source, string nullSourceWarning)
        {
            if (source == null)
            {
                Debug.LogWarning(nullSourceWarning);
                return false;
            }

            return sources.Add(source);
        }

        private static bool RemoveSource(HashSet<object> sources, object source, string nullSourceWarning)
        {
            if (source == null)
            {
                Debug.LogWarning(nullSourceWarning);
                return false;
            }

            return sources.Remove(source);
        }

        private global::BoothFlickeringLightsController ResolveLightsController(bool warnIfMissing)
        {
            if (lightsController != null)
            {
                return lightsController;
            }

            lightsController = UnityEngine.Object.FindFirstObjectByType<global::BoothFlickeringLightsController>();
            if (warnIfMissing && lightsController == null)
            {
                Debug.LogWarning("[RoomService] BoothFlickeringLightsController was not found in the scene.");
            }

            return lightsController;
        }

        private global::CockroachSpawner ResolveCockroachSpawner(bool warnIfMissing)
        {
            if (cockroachSpawner != null)
            {
                return cockroachSpawner;
            }

            cockroachSpawner = UnityEngine.Object.FindFirstObjectByType<global::CockroachSpawner>();
            if (warnIfMissing && cockroachSpawner == null)
            {
                Debug.LogWarning("[RoomService] CockroachSpawner was not found in the scene.");
            }

            return cockroachSpawner;
        }
    }
}