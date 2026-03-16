using UnityEngine;

public class ThrowFolderAnomaly : BehaviorAnomaly
{
    [SerializeField] SuspectCharacter suspect;
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        suspect.SetFolderGivingAnimation(SuspectCharacter.FolderGivingAnimation.Throw);
    }
}
