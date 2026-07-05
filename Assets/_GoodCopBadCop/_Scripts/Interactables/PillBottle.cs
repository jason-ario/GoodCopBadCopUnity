using System.Collections;
using UnityEngine;

public class PillBottle : PickableObject
{
    private const int MaxUses = 3;

    [SerializeField] Animator _animator;
    private int _usesRemaining = MaxUses;
    [SerializeField] private AudioClip drinkSound;

    /// <summary>
    /// Initiates a pill use if the bottle still has doses and is not already in use.
    /// Destroys the bottle after the last dose is consumed.
    /// </summary>
    public override void OnStartUse()
    {
        if (isUsing || _usesRemaining <= 0) return;

        base.OnStartUse();
        StartCoroutine(UsePillBottle());
    }

    IEnumerator UsePillBottle()
    {
        SFXController.Instance.Play(drinkSound);
        playerPickupController.PlayerAnimationController.EnableHoldObjectTwoArmsMask();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", true);
        _animator.SetBool("TakePill", true);
        yield return new WaitForSeconds(2.5f);
        PlayerInstance.Instance.PlayerRadiation.TakeRadiationPill();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", false);
        _animator.SetBool("TakePill", false);

        _usesRemaining--;

        if (_usesRemaining <= 0)
        {
            playerPickupController.DestroyEquippedItem();
            yield break;
        }

        playerPickupController.PlayerAnimationController.EnableRightArmMask();
        isUsing = false;
    }
}
