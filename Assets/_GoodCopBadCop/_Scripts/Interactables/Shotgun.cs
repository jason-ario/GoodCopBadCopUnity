using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class Shotgun : PickableObject
{
    [SerializeField] private ParticleSystem shootVFX;
    [SerializeField] private CinemachineImpulseSource _cinemachineImpulseSource;
    [SerializeField] private GameObject muzzleFlashLight;
    [SerializeField] private float lightOnTime = .2f;
    public override void OnStartUse()
    {
        base.OnStartUse();
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        playerPickupController.PlayerAnimationController.SetAnimTrigger("Shoot");
        _cinemachineImpulseSource.GenerateImpulse();
        StartCoroutine(LightOnOff());
        var movement = playerPickupController.GetComponent<PlayerMovementController>();
        if (movement != null)
        {
            movement.ApplyRecoil();
        }
    }

    public void ShootFX()
    {
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        _cinemachineImpulseSource.GenerateImpulse();
        StartCoroutine(LightOnOff());
    }

    IEnumerator LightOnOff()
    {
        muzzleFlashLight.SetActive(true);
        yield return new WaitForSeconds(lightOnTime);
        muzzleFlashLight.SetActive(false);
    }

    public override void OnBodyStartUse()
    {
        //playerPickupController.GetComponent<RagdollController>().ActivateRagdollWithForce(-playerPickupController.transform.forward * 100);
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        StartCoroutine(LightOnOff());

    }
    
    
}
