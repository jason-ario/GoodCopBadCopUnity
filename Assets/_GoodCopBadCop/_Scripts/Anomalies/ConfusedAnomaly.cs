using UnityEngine;

public class ConfusedAnomaly : BehaviorAnomaly
{
    [SerializeField] private SuspectCharacter suspectCharacter; 
    [SerializeField] AnimatorOverrideController confusedAnimatorController;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        suspectCharacter.animator.runtimeAnimatorController = confusedAnimatorController;
    }
}
