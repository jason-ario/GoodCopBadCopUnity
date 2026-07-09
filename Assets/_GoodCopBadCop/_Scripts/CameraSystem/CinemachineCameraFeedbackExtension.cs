using Unity.Cinemachine;
using UnityEngine;

namespace GoodCopBadCop.CameraSystem
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Good Cop Bad Cop/Camera/Cinemachine Camera Feedback Extension")]
    public sealed class CinemachineCameraFeedbackExtension : CinemachineExtension
    {
        public Vector3 EulerOffset { get; set; }
        public float FieldOfViewOffset { get; set; }

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage,
            ref CameraState state,
            float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize ||
                (EulerOffset == Vector3.zero && Mathf.Approximately(FieldOfViewOffset, 0f)))
            {
                return;
            }

            if (EulerOffset != Vector3.zero)
            {
                state.OrientationCorrection *= Quaternion.Euler(EulerOffset.x, EulerOffset.y, 0f);

                LensSettings lensWithDutch = state.Lens;
                lensWithDutch.Dutch += EulerOffset.z;
                state.Lens = lensWithDutch;
            }

            if (!Mathf.Approximately(FieldOfViewOffset, 0f))
            {
                LensSettings lens = state.Lens;
                if (lens.Orthographic)
                    lens.OrthographicSize = Mathf.Max(0.01f, lens.OrthographicSize + FieldOfViewOffset);
                else
                    lens.FieldOfView = Mathf.Clamp(lens.FieldOfView + FieldOfViewOffset, 1f, 179f);
                state.Lens = lens;
            }
        }
    }
}
