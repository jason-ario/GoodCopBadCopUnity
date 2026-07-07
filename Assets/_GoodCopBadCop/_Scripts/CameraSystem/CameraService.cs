using System.Collections.Generic;
using GoodCopBadCop.Player;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    public interface ICameraService
    {
        void ShakeLocalPlayer();
        void SetCaptureVisibility(global::SuspectCharacter suspect, bool visible);
        bool IsCaptureVisible(global::SuspectCharacter suspect);
    }

    public sealed class CameraService : ICameraService
    {
        private readonly IPlayerRuntimeModel playerRuntimeModel;
        private readonly HashSet<global::SuspectCharacter> hiddenCaptureSuspects = new();

        public CameraService(IPlayerRuntimeModel playerRuntimeModel)
        {
            this.playerRuntimeModel = playerRuntimeModel;
        }

        public void ShakeLocalPlayer()
        {
            global::PlayerInstance player = playerRuntimeModel.LocalPlayer.CurrentValue;
            if (player == null)
            {
                return;
            }

            global::PlayerCameraController cameraController = player.GetComponent<global::PlayerCameraController>();
            if (cameraController == null)
            {
                Debug.LogWarning("[CameraService] Local player has no PlayerCameraController.");
                return;
            }

            cameraController.TriggerHitImpulse();
        }

        public void SetCaptureVisibility(global::SuspectCharacter suspect, bool visible)
        {
            if (suspect == null)
            {
                return;
            }

            if (visible)
            {
                hiddenCaptureSuspects.Remove(suspect);
            }
            else
            {
                hiddenCaptureSuspects.Add(suspect);
            }
        }

        public bool IsCaptureVisible(global::SuspectCharacter suspect)
        {
            return suspect == null || !hiddenCaptureSuspects.Contains(suspect);
        }
    }
}