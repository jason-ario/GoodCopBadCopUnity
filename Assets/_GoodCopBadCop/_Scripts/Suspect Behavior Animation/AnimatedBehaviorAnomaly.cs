using GoodCopBadCop.SuspectBehaviorAnimation;
using UnityEngine;

public abstract class AnimatedBehaviorAnomaly : BehaviorAnomaly
{
    [SerializeField] private SuspectBehaviorAnimationAdapter animationAdapter;
    [SerializeField] private BehaviorAnimationPreset animationPreset;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        ResolveAdapter()?.Apply(this, animationPreset);
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        ResolveAdapter()?.Release(this);
    }

    public override void InitializeDisabled()
    {
        ResolveAdapter()?.Release(this);
    }

    private SuspectBehaviorAnimationAdapter ResolveAdapter()
    {
        if (animationAdapter != null)
            return animationAdapter;

        animationAdapter = GetComponentInParent<SuspectBehaviorAnimationAdapter>();
        if (animationAdapter == null)
            animationAdapter = GetComponentInChildren<SuspectBehaviorAnimationAdapter>(true);

        return animationAdapter;
    }
}
