using System.Collections.Generic;
using GoodCopBadCop.Player;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    public interface ICameraService
    {
        void ShakeLocalPlayer();
        void PlayLocalImpulse(CameraImpulseSettings settings);
        void HideFromCapture(object source, global::SuspectCharacter suspect);
        void ShowInCapture(object source, global::SuspectCharacter suspect);
        bool IsVisibleInCapture(global::SuspectCharacter suspect);
    }

    public sealed class CameraService : ICameraService
    {
        private readonly IPlayerRuntimeModel playerRuntimeModel;
        private readonly Dictionary<global::SuspectCharacter, HashSet<object>> hiddenCaptureSourcesBySuspect =
            new Dictionary<global::SuspectCharacter, HashSet<object>>();

        public CameraService(IPlayerRuntimeModel playerRuntimeModel)
        {
            this.playerRuntimeModel = playerRuntimeModel;
        }

        public void ShakeLocalPlayer()
        {
            PlayLocalImpulse(CameraImpulseSettings.DefaultHit());
        }

        public void PlayLocalImpulse(CameraImpulseSettings settings)
        {
            if (settings == null || !settings.Enabled)
                return;

            global::PlayerInstance player = FindLocalPlayer();
            if (player == null)
            {
                Debug.LogWarning("[CameraService] Local player was not found.");
                return;
            }

            global::PlayerCameraController cameraController = player.GetComponent<global::PlayerCameraController>();
            if (cameraController == null)
            {
                Debug.LogWarning("[CameraService] Local player has no PlayerCameraController.");
                return;
            }

            cameraController.PlayImpulse(settings);
        }

        private global::PlayerInstance FindLocalPlayer()
        {
            global::PlayerInstance player = playerRuntimeModel.LocalPlayer.CurrentValue;
            if (player != null)
                return player;

            player = global::PlayerInstance.Instance;
            if (player != null)
                return player;

            global::PlayerInstance[] players = Object.FindObjectsByType<global::PlayerInstance>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (global::PlayerInstance candidate in players)
            {
                if (candidate != null && candidate.IsOwner)
                    return candidate;
            }

            return players.Length > 0 ? players[0] : null;
        }

        public void HideFromCapture(object source, global::SuspectCharacter suspect)
        {
            if (!CanChangeCaptureVisibility(source, suspect))
            {
                return;
            }

            AddCaptureHideSource(source, suspect);
        }

        public void ShowInCapture(object source, global::SuspectCharacter suspect)
        {
            if (!CanChangeCaptureVisibility(source, suspect))
            {
                return;
            }

            RemoveCaptureHideSource(source, suspect);
        }

        public bool IsVisibleInCapture(global::SuspectCharacter suspect)
        {
            return suspect == null ||
                   !hiddenCaptureSourcesBySuspect.TryGetValue(suspect, out HashSet<object> sources) ||
                   sources.Count == 0;
        }

        private static bool CanChangeCaptureVisibility(object source, global::SuspectCharacter suspect)
        {
            if (source == null)
            {
                Debug.LogWarning("[CameraService] Capture visibility source cannot be null.");
                return false;
            }

            return suspect != null;
        }

        private void AddCaptureHideSource(object source, global::SuspectCharacter suspect)
        {
            if (!hiddenCaptureSourcesBySuspect.TryGetValue(suspect, out HashSet<object> sources))
            {
                sources = new HashSet<object>();
                hiddenCaptureSourcesBySuspect.Add(suspect, sources);
            }

            sources.Add(source);
        }

        private void RemoveCaptureHideSource(object source, global::SuspectCharacter suspect)
        {
            if (!hiddenCaptureSourcesBySuspect.TryGetValue(suspect, out HashSet<object> sources))
            {
                return;
            }

            sources.Remove(source);
            if (sources.Count == 0)
            {
                hiddenCaptureSourcesBySuspect.Remove(suspect);
            }
        }
    }
}
