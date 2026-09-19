using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent HUD readout for the Checkpoint Integrity Score — how much of the base ATM
/// payout the player currently earns based on the booth's graffiti, trash, and perimeter
/// fence condition. It displays the precise score as text and as a row of discrete
/// filled/unfilled integrity squares.
///
/// Each square represents an equal portion of the full payout multiplier. The indicator is
/// rounded to the nearest square while the percentage label preserves the exact score.
/// </summary>
public class CheckpointIntegrityBar : StatBar
{
    [Header("Integrity Squares")]
    [SerializeField] private Image[] integritySquares;
    [SerializeField] private Color filledSquareColor = new(0.75f, 0.82f, 0.25f, 1f);
    [SerializeField] private Color emptySquareColor = new(0.18f, 0.19f, 0.08f, 1f);

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
        UpdateIntegrityDisplay(service.IntegrityScore, service.MaxScore);
    }

    protected override void OnDisable()
    {
        CheckpointIntegrityService.OnIntegrityScoreChanged -= OnIntegrityScoreChanged;
        base.OnDisable();
    }

    private void OnIntegrityScoreChanged(float newScore)
    {
        CheckpointIntegrityService service = CheckpointIntegrityService.Instance;
        UpdateIntegrityDisplay(newScore, service.MaxScore);
    }

    private void UpdateIntegrityDisplay(float current, float max)
    {
        UpdateBar(current, max);

        if (integritySquares == null || integritySquares.Length == 0)
            return;

        float normalizedScore = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        int filledSquareCount = Mathf.Clamp(
            Mathf.RoundToInt(normalizedScore * integritySquares.Length),
            0,
            integritySquares.Length);

        for (int i = 0; i < integritySquares.Length; i++)
        {
            if (integritySquares[i] != null)
                integritySquares[i].color = i < filledSquareCount ? filledSquareColor : emptySquareColor;
        }
    }
}
