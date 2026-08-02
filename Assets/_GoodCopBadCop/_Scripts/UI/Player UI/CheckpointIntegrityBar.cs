using UnityEngine;

/// <summary>
/// Persistent HUD readout for the Checkpoint Integrity Score — how much of the base ATM
/// payout the player currently earns based on the booth's graffiti, trash, and perimeter
/// fence condition. Drives the inherited <see cref="StatBar"/> fill/percentage visuals and
/// refreshes whenever <see cref="CheckpointIntegrityService"/> recalculates.
///
/// The bar's fill amount is the score itself (e.g. 50%–100% by default), NOT a 0–100% mess
/// meter — matching the payout multiplier the player actually receives.
/// </summary>
public class CheckpointIntegrityBar : StatBar
{
    private void OnEnable()
    {
        // Day 1 keeps the integrity system disabled, so the bar has nothing meaningful to show
        // yet — hide immediately rather than displaying a static 100% bar. Day_01 calls
        // CheckpointIntegrityService.SetEnabled(true) and Show() together right when the
        // "Checkpoint Integrity Score" tutorial first appears, which re-triggers this OnEnable
        // with the system already enabled.
        if (!CheckpointIntegrityService.IsEnabled)
        {
            gameObject.SetActive(false);
            return;
        }

        CheckpointIntegrityService.OnIntegrityScoreChanged += OnIntegrityScoreChanged;

        // Force a fresh read so the bar is correct the moment it becomes visible
        // (e.g. right after a scene load or late UI enable).
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        service.Recalculate();
        UpdateBar(service.IntegrityScore, service.MaxScore);
    }

    protected override void OnDisable()
    {
        CheckpointIntegrityService.OnIntegrityScoreChanged -= OnIntegrityScoreChanged;
        base.OnDisable();
    }

    private void OnIntegrityScoreChanged(float newScore)
    {
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        UpdateBar(newScore, service.MaxScore);
    }
}
