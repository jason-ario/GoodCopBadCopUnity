using GoodCopBadCop.CameraSystem;
using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect does not appear on camera feeds.
/// </summary>
public class NotShowingInCameraAnomaly : SupernaturalAnomaly
{
    [VContainer.Inject] private ICameraService cameraService;

    private SuspectCharacter suspect;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (!TryResolveDependencies())
        {
            return;
        }

        cameraService.HideFromCapture(this, suspect);
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        cameraService?.ShowInCapture(this, suspect);
    }

    public override void InitializeDisabled()
    {
        cameraService?.ShowInCapture(this, suspect);
    }

    private bool TryResolveDependencies()
    {
        if (cameraService == null)
        {
            Debug.LogWarning($"[NotShowingInCameraAnomaly] {nameof(ICameraService)} was not injected on '{gameObject.name}'.", this);
            return false;
        }

        suspect = suspect != null ? suspect : GetComponentInParent<SuspectCharacter>();
        if (suspect == null)
        {
            Debug.LogWarning($"[NotShowingInCameraAnomaly] No {nameof(SuspectCharacter)} found in parents of '{gameObject.name}'.", this);
            return false;
        }

        return true;
    }
}
