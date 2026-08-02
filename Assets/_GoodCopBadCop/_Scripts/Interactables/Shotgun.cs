using System.Collections;
using GoodCopBadCop.Effects;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

public class Shotgun : PickableObject
{
    [SerializeField] private ParticleSystem shootVFX;
    [SerializeField] private CinemachineImpulseSource _cinemachineImpulseSource;
    [SerializeField] private GameObject muzzleFlashLight;
    [SerializeField] private float lightOnTime = .2f;

    [Header("Shotgun — Combat")]
    [Tooltip("Damage dealt to a fellow player hit by this shotgun blast.")]
    [SerializeField] private float _playerDamage = 40f;

    [Tooltip("Maximum hitscan range in metres.")]
    [SerializeField] private float _bulletRange = 25f;

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

        Camera cam = Camera.main;
        if (cam != null)
            FireServerRpc(cam.transform.position, cam.transform.forward);
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

    // ── Combat ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side: raycasts from the shooter's camera in the aim direction and, if the ray
    /// lands on a fellow player, applies friendly-fire damage. Skips the shooter's own player
    /// via <see cref="NetworkObject.OwnerClientId"/> so hitting yourself never registers damage.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayOrigin, Vector3 rayDirection, ServerRpcParams rpcParams = default)
    {
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        if (!Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, _bulletRange))
            return;

        Transform root = hit.collider.transform.root;
        if (!root.CompareTag("Player"))
            return;

        NetworkObject playerNetObj = hit.collider.GetComponentInParent<NetworkObject>();
        if (playerNetObj != null && playerNetObj.OwnerClientId == shooterClientId)
            return;

        PlayerHealth playerHealth = hit.collider.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            return;

        playerHealth.TakeDamage(_playerDamage, EffectKeys.FriendlyGunshotDamage);
    }
}
