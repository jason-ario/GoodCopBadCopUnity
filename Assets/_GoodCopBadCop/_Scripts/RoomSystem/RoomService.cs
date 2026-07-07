using UnityEngine;

namespace GoodCopBadCop.RoomSystem
{
    public interface IRoomService
    {
        float TemperatureOffset { get; }
        void SetTemperatureOffset(float offset);
        void ResetTemperatureOffset();
        void StartFlickeringLights();
        void StopFlickeringLights();
        void StartInsectSwarm();
        void StopInsectSwarm();
    }

    public sealed class RoomService : IRoomService
    {
        private global::BoothFlickeringLightsController lightsController;
        private global::CockroachSpawner cockroachSpawner;

        public float TemperatureOffset { get; private set; }

        public void SetTemperatureOffset(float offset)
        {
            TemperatureOffset = offset;
        }

        public void ResetTemperatureOffset()
        {
            TemperatureOffset = 0f;
        }

        public void StartFlickeringLights()
        {
            ResolveLightsController(warnIfMissing: true)?.StartFlickering();
        }

        public void StopFlickeringLights()
        {
            ResolveLightsController(warnIfMissing: false)?.StopFlickering();
        }

        public void StartInsectSwarm()
        {
            ResolveCockroachSpawner(warnIfMissing: true)?.StartSwarm();
        }

        public void StopInsectSwarm()
        {
            ResolveCockroachSpawner(warnIfMissing: false)?.StopSwarm();
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