using UnityEngine;

/// <summary>
/// Diegetic view for the mini fridge. Extends <see cref="DiegeticViewController"/> with
/// one piece of fridge-specific logic: closing the door when the player exits the view.
/// </summary>
public class MiniFridgeDiegeticController : DiegeticViewController
{
    private MiniFridge _fridge;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the mini fridge diegetic view for <paramref name="player"/>.
    /// Stores the <paramref name="fridge"/> reference so the door can be closed on exit.
    /// </summary>
    public void Open(PlayerInteractionController player, MiniFridge fridge)
    {
        _fridge = fridge;
        base.Open(player);
    }

    // ─── DiegeticViewController hooks ────────────────────────────────────────

    protected override void OnClosed()
    {
        if (_fridge != null)
        {
            _fridge.RequestClose();
            _fridge = null;
        }
    }
}
