/// <summary>
/// Diegetic view for the quarantine board. Extends <see cref="DiegeticViewController"/>.
/// No custom interaction — the player simply looks at the board of quarantined suspects
/// up close. <see cref="QuarantineBoardController"/> keeps the polaroid slots and title
/// text up to date independently of whether this view is open.
/// </summary>
public class QuarantineBoardDiegeticController : DiegeticViewController
{
    // Look-only view: LMB does nothing here, so no LMB prompt.
    protected override string ActionPromptText => null;
}
