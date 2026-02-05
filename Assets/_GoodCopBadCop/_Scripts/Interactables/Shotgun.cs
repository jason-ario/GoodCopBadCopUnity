using UnityEngine;

public class Shotgun : PickableObject
{
    [SerializeField] private ParticleSystem shootVFX;
    public override void OnStartUse()
    {
        base.OnStartUse();
        Debug.Log("Player shotgun!");
        shootVFX.Play();
        playerPickupController.PlayerAnimationController.SetAnimTrigger("Shoot");
    }

    public override void OnBodyStartUse()
    {
        playerPickupController.GetComponent<RagdollController>().ActivateRagdollWithForce(-playerPickupController.transform.forward * 100);

        shootVFX.Play();
    }
    
    
}
