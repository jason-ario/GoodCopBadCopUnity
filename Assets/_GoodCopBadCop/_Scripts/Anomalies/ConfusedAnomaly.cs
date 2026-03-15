using UnityEngine;

public class ConfusedAnomaly : BehaviorAnomaly
{
    [SerializeField] private AnimatorOverrideController confusedAnimatorController; 
    [SerializeField] Animator animator;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        animator.runtimeAnimatorController = confusedAnimatorController;
    }
}
