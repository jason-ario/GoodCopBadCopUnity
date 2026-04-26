using System.Collections;
using UnityEngine;

public class PillBottle : PickableObject
{
    [SerializeField] Animator _animator;
    private bool isUsing;
    
    public override void OnStartUse()
    {
        base.OnStartUse();
        if (isUsing) return;
        
        isUsing = true;
        StartCoroutine(UsePillBottle());
    }

    IEnumerator UsePillBottle()
    {
        playerPickupController.PlayerAnimationController.EnableHoldObjectTwoArmsMask();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", true);
        _animator.SetBool("TakePill", true);
        yield return new WaitForSeconds(2.5f);
        PlayerInstance.Instance.PlayerRadiation.TakeRadiationPill();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", false);
        _animator.SetBool("TakePill", false);
        playerPickupController.PlayerAnimationController.EnableRightArmMask();
        isUsing = false;
    }
}
