using UnityEngine;

/// <summary>
/// Behavior anomaly in which the suspect breaks into uncontrollable laughter
/// at inappropriate moments, potentially disturbing nearby NPCs.
/// </summary>
public class LaughingAnomaly : AnimatedBehaviorAnomaly
{
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        ResolveSpeaking()?.SetLaughing(true);
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        ResolveSpeaking()?.SetLaughing(false);
    }

    public override void InitializeDisabled()
    {
        base.InitializeDisabled();
        ResolveSpeaking()?.SetLaughing(false);
    }

    private SpeakingInteraction ResolveSpeaking()
    {
        SuspectCharacter suspect = GetComponentInParent<SuspectCharacter>();
        return suspect != null ? suspect.Speaking : null;
    }
}
