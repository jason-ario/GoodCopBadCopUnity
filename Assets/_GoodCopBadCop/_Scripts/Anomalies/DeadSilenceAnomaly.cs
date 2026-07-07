using GoodCopBadCop.Audio;
using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect causes all ambient sound to cease entirely.
/// </summary>
public class DeadSilenceAnomaly : SupernaturalAnomaly
{
    [VContainer.Inject] private IAudioService audioService;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (audioService == null)
        {
            Debug.LogWarning($"[DeadSilenceAnomaly] {nameof(IAudioService)} was not injected on '{gameObject.name}'.", this);
            return;
        }

        audioService.SetDeadSilence(this, true);
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        audioService?.SetDeadSilence(this, false);
    }

    public override void InitializeDisabled()
    {
        audioService?.SetDeadSilence(this, false);
    }
}
