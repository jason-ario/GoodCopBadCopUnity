using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("Damage dealt to a fellow player per pellet that connects.")]
    [SerializeField] private float _playerPelletDamage = 8f;

    [Tooltip("Damage dealt to a mutant per pellet that connects.")]
    [SerializeField] private float _mutantPelletDamage = 10f;

    [Tooltip("Number of pellets fired per shot, spread across the cone.")]
    [SerializeField] [Min(1)] private int _pelletCount = 8;

    [Tooltip("Half-angle (in degrees) of the pellet spread cone.")]
    [SerializeField] [Range(0f, 45f)] private float _spreadAngle = 10f;

    [Tooltip("Maximum hitscan range in metres — kept short since this is a close-range weapon.")]
    [SerializeField] private float _bulletRange = 8f;

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
    /// Server-side: fires <see cref="_pelletCount"/> pellets in a short-range cone around the
    /// shooter's aim direction. Each pellet independently raycasts and can land on a mutant,
    /// a fellow player (friendly fire, skipping the shooter via <see cref="NetworkObject.OwnerClientId"/>),
    /// or the breakable booth glass. Damage from multiple pellets hitting the same mutant/player is
    /// accumulated and applied once; the glass registers at most one hit per blast, mirroring
    /// <see cref="MutantSuspectBehaviour"/>'s hit pattern.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayOrigin, Vector3 rayDirection, ServerRpcParams rpcParams = default)
    {
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        Dictionary<MutantEnemy, int> mutantHits = new();
        Dictionary<PlayerHealth, int> playerHits = new();
        bool hitGlass = false;

        for (int i = 0; i < _pelletCount; i++)
        {
            Vector3 pelletDirection = RandomConeDirection(rayDirection, _spreadAngle);

            if (!Physics.Raycast(rayOrigin, pelletDirection, out RaycastHit hit, _bulletRange))
                continue;

            MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
            if (enemy != null)
            {
                mutantHits.TryGetValue(enemy, out int mCount);
                mutantHits[enemy] = mCount + 1;
                continue;
            }

            Transform root = hit.collider.transform.root;
            if (root.CompareTag("Player"))
            {
                NetworkObject playerNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (playerNetObj != null && playerNetObj.OwnerClientId == shooterClientId)
                    continue;

                PlayerHealth playerHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                if (playerHealth != null)
                {
                    playerHits.TryGetValue(playerHealth, out int pCount);
                    playerHits[playerHealth] = pCount + 1;
                }
                continue;
            }

            BreakableGlassController glassHit = hit.collider.GetComponentInParent<BreakableGlassController>();
            if (glassHit != null && !glassHit.IsSmashed)
                hitGlass = true;
        }

        foreach (var kvp in mutantHits)
            kvp.Key.TakeDamage(_mutantPelletDamage * kvp.Value, kvp.Key.transform.position);

        foreach (var kvp in playerHits)
            kvp.Key.TakeDamage(_playerPelletDamage * kvp.Value, EffectKeys.FriendlyGunshotDamage);

        if (hitGlass)
        {
            BreakableGlassController glass = BreakableGlassController.Instance;
            if (glass != null && !glass.IsSmashed)
            {
                int newHits = glass.RegisterHit();
                if (glass.IsSmashed)
                    ShotgunSmashGlassClientRpc();
                else
                    ShotgunUpdateGlassClientRpc(newHits);
            }
        }
    }

    /// <summary>
    /// Returns a random direction within a cone of half-angle <paramref name="maxAngleDegrees"/>
    /// around <paramref name="forward"/>, used to spread the shotgun's pellets.
    /// </summary>
    private static Vector3 RandomConeDirection(Vector3 forward, float maxAngleDegrees)
    {
        float angle = Random.Range(0f, maxAngleDegrees) * Mathf.Deg2Rad;
        float rotation = Random.Range(0f, 360f) * Mathf.Deg2Rad;

        float x = Mathf.Sin(angle) * Mathf.Cos(rotation);
        float y = Mathf.Sin(angle) * Mathf.Sin(rotation);
        float z = Mathf.Cos(angle);

        Quaternion lookRot = Quaternion.LookRotation(forward);
        return lookRot * new Vector3(x, y, z);
    }

    /// <summary>
    /// Received by all clients when a shotgun blast lands an intermediate hit on the glass.
    /// Mirrors UpdateGlassClientRpc on Pistol/MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void ShotgunUpdateGlassClientRpc(int hitCount)
    {
        BreakableGlassController.Instance?.OnHitByMutant(hitCount);
    }

    /// <summary>
    /// Received by all clients when a shotgun blast fully smashes the glass.
    /// Mirrors SmashGlassClientRpc on Pistol/MutantSuspectBehaviour.
    /// </summary>
    [ClientRpc]
    private void ShotgunSmashGlassClientRpc()
    {
        BreakableGlassController.Instance?.ApplySmash();
    }
}
