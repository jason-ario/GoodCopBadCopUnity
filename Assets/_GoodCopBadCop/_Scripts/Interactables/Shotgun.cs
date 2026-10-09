using System;
using System.Collections;
using System.Collections.Generic;
using GoodCopBadCop.Effects;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A pump-action shotgun with networked ammo tracking.
///
/// LMB (while held) fires a pellet blast, playing muzzle VFX, camera impulse, recoil, and a
/// "Shoot" animation trigger. Firing is blocked when <see cref="_roundsRemaining"/> reaches
/// zero — a dry-fire click sound plays instead.
///
/// R (while held) reloads shells from the holder's <see cref="PlayerAmmoReserve"/>.
/// Ammo enters the reserve by left-clicking a held <see cref="ShotgunAmmo"/> box.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Shotgun : PickableObject, IAmmoProvider, IInventoryReloadable
{
    /// <summary>Maximum number of shells the shotgun can hold.</summary>
    public const int MaxRounds = 15;

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

    /// <summary>Hitscan range in metres. Read by the reticle to show the enemy-in-range color.</summary>
    public float BulletRange => _bulletRange;

    [Header("Shotgun — Audio")]
    [Tooltip("Dry-fire click played locally when the shotgun is empty.")]
    [SerializeField] private AudioClip _emptySound;

    [Tooltip("Sound played on every client when the shotgun is reloaded.")]
    [SerializeField] private AudioClip _reloadSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _roundsRemaining = new(
        MaxRounds,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current number of shells loaded in the shotgun.</summary>
    public int RoundsRemaining => _roundsRemaining.Value;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _roundsRemaining.Value;
    public float MaxAmmo => MaxRounds;
    public event Action OnAmmoChanged;

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = _roundsRemaining.Value;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _roundsRemaining.Value = Mathf.Clamp(Mathf.RoundToInt(data.ResourceAmount), 0, MaxRounds);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        UpdateInteractText(MaxRounds);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _roundsRemaining.OnValueChanged += OnRoundsChanged;

        // Server initialises the authoritative count so late-joining clients replicate correctly.
        if (IsServer)
            _roundsRemaining.Value = MaxRounds;

        // Sync text immediately — OnValueChanged won't fire when value equals the NetworkVariable default.
        UpdateInteractText(_roundsRemaining.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _roundsRemaining.OnValueChanged -= OnRoundsChanged;
    }

    private void OnRoundsChanged(int previous, int current)
    {
        UpdateInteractText(current);
        OnAmmoChanged?.Invoke();
    }

    private void UpdateInteractText(int rounds)
        => interactText = $"Shotgun ({rounds}/{MaxRounds})";

    /// <summary>LMB fires the shotgun.</summary>
    public override string GetHeldUseVerb() => "Shoot";

    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_roundsRemaining.Value <= 0)
        {
            if (_emptySound != null)
                SFXController.Instance.PlayAtPosition(_emptySound, transform.position);
            return;
        }

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
        {
            // Client-authoritative hitscan: the pellet cone is traced here, against the world as
            // this player sees it, and the resolved per-target pellet counts are reported to the
            // server. The server used to re-trace the cone from the reported origin — a different
            // random spread against already-moved targets — so a blast that clearly connected
            // locally could deal little or nothing. See FireServerRpc.
            ResolveBlast(cam.transform.position, cam.transform.forward,
                out NetworkObjectReference[] mutantRefs, out int[] mutantPellets, out Vector3[] mutantPelletPoints,
                out NetworkObjectReference[] playerRefs, out int[] playerPellets,
                out NetworkObjectReference[] suspectRefs, out Vector3[] suspectPoints,
                out bool hitGlass, out Vector3[] propPoints);

            // Cosmetic prop reactions (wobble / shatter) play instantly for the shooter; the server
            // relays them to everyone else.
            foreach (Vector3 point in propPoints)
                HittableProp.TryHitAt(point, cam.transform.forward);

            FireServerRpc(cam.transform.forward, mutantRefs, mutantPellets, mutantPelletPoints, playerRefs, playerPellets,
                suspectRefs, suspectPoints, hitGlass, propPoints);
        }
    }

    /// <summary>
    /// Traces the pellet cone locally on the shooter's machine and accumulates how many pellets
    /// landed on each mutant and each fellow player, plus whether the booth glass was struck.
    /// Mutant pellet impact points are returned flattened in <paramref name="mutantPelletPoints"/>,
    /// grouped per mutant in the same order as <paramref name="mutantRefs"/> (counts = <paramref name="mutantPellets"/>).
    /// Living subjects / world NPCs are collected once each (first pellet point) — shots only make
    /// them flinch. Runs on the shooter only — the spread the player sees is the spread that is reported.
    /// </summary>
    private void ResolveBlast(Vector3 rayOrigin, Vector3 rayDirection,
        out NetworkObjectReference[] mutantRefs, out int[] mutantPellets, out Vector3[] mutantPelletPoints,
        out NetworkObjectReference[] playerRefs, out int[] playerPellets,
        out NetworkObjectReference[] suspectRefs, out Vector3[] suspectPoints,
        out bool hitGlass, out Vector3[] propPoints)
    {
        Dictionary<NetworkObject, List<Vector3>> mutantHits = new();
        Dictionary<NetworkObject, int> playerHits = new();
        Dictionary<NetworkObject, Vector3> suspectHits = new();
        Dictionary<HittableProp, Vector3> propHits = new();
        hitGlass = false;

        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

        for (int i = 0; i < _pelletCount; i++)
        {
            Vector3 pelletDirection = RandomConeDirection(rayDirection, _spreadAngle);

            // Uses RaycastAll (sorted by distance) instead of a plain Raycast so trigger colliders
            // can be inspected without letting them silently block the pellet. A resurrected
            // corpse (see CorpseResurrectionController) exposes its ragdoll limb colliders as
            // trigger hitboxes so individual limbs/body parts can be hit — but this project also
            // uses trigger colliders everywhere for non-physical logic volumes (interaction zones,
            // click detectors, task areas, etc.), and those must stay completely transparent to
            // pellets, exactly like before.
            RaycastHit[] pelletHits = Physics.RaycastAll(rayOrigin, pelletDirection, _bulletRange, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (pelletHits.Length == 0)
                continue;

            System.Array.Sort(pelletHits, (a, b) => a.distance.CompareTo(b.distance));

            for (int h = 0; h < pelletHits.Length; h++)
            {
                RaycastHit hit = pelletHits[h];

                // IsActive matters here: every Player prefab carries a dormant MutantEnemy (see
                // CorpseResurrectionController) so it can later resurrect into a chasing mutant
                // while still alive/uninfected. Without the IsActive check, a pellet hitting a
                // perfectly living player would always be misclassified as a mutant hit and
                // silently swallowed by MutantEnemy's own dormancy guard, never reaching
                // PlayerHealth at all.
                // CanBeDamagedByPlayers = IsActive, plus dormant lineup mutants attacking the booth window.
                MutantEnemy enemy = hit.collider.GetComponentInParent<MutantEnemy>();
                if (enemy != null && enemy.CanBeDamagedByPlayers)
                {
                    if (enemy.NetworkObject != null)
                    {
                        if (!mutantHits.TryGetValue(enemy.NetworkObject, out List<Vector3> points))
                        {
                            points = new List<Vector3>();
                            mutantHits[enemy.NetworkObject] = points;
                        }

                        // Prefer the LimbHitbox behind a broad body capsule so the pellet's point
                        // lands on the limb it actually struck (drives LimbHitReactor reactions).
                        int limbIndex = CombatHitUtility.PreferLimbAlongRay(pelletHits, h, enemy);
                        points.Add(pelletHits[limbIndex].point);
                    }
                    break;
                }

                // Living subjects / world NPCs (Soldier, Vlad, guards) — harmless flinch only.
                SuspectCharacter suspect = hit.collider.GetComponentInParent<SuspectCharacter>();
                if (suspect != null && !suspect.IsDead && suspect.NetworkObject != null)
                {
                    if (!suspectHits.ContainsKey(suspect.NetworkObject))
                        suspectHits[suspect.NetworkObject] = hit.point;
                    break;
                }

                // An irrelevant trigger (interaction zone, click detector, task area, etc.) —
                // pellets must pass straight through it, exactly as if
                // QueryTriggerInteraction.Collide had never been requested.
                if (hit.collider.isTrigger)
                    continue;

                // Fences and gates are open mesh — pellets pass through them, matching melee weapons.
                if (CombatHitUtility.IsBulletPassthrough(hit.collider))
                    continue;

                Transform root = hit.collider.transform.root;
                if (root.CompareTag("Player"))
                {
                    NetworkObject playerNetObj = hit.collider.GetComponentInParent<NetworkObject>();
                    if (playerNetObj == null || playerNetObj.OwnerClientId == localClientId)
                        break;

                    if (hit.collider.GetComponentInParent<PlayerHealth>() != null)
                    {
                        playerHits.TryGetValue(playerNetObj, out int pCount);
                        playerHits[playerNetObj] = pCount + 1;
                    }
                    break;
                }

                BreakableGlassController glassHit = hit.collider.GetComponentInParent<BreakableGlassController>();
                if (glassHit != null && !glassHit.IsSmashed)
                {
                    // A closed shutter on the shooter's side shields the pane: the pellet stops dead.
                    if (glassHit.IsShieldedFrom(rayOrigin))
                        break;
                    hitGlass = true;
                }

                // Cosmetic props (signs, bottles…) — one reaction per prop per blast.
                HittableProp prop = hit.collider.GetComponentInParent<HittableProp>();
                if (prop != null && !propHits.ContainsKey(prop))
                    propHits[prop] = hit.point;

                // Solid geometry (or handled glass/prop) — this blocks the pellet either way.
                break;
            }
        }

        MutantHitsToArrays(mutantHits, out mutantRefs, out mutantPellets, out mutantPelletPoints);
        ToArrays(playerHits, out playerRefs, out playerPellets);

        propPoints = new Vector3[propHits.Count];
        propHits.Values.CopyTo(propPoints, 0);

        suspectRefs   = new NetworkObjectReference[suspectHits.Count];
        suspectPoints = new Vector3[suspectHits.Count];
        int s = 0;
        foreach (var kvp in suspectHits)
        {
            suspectRefs[s]   = new NetworkObjectReference(kvp.Key);
            suspectPoints[s] = kvp.Value;
            s++;
        }
    }

    private static void MutantHitsToArrays(Dictionary<NetworkObject, List<Vector3>> hits,
        out NetworkObjectReference[] refs, out int[] counts, out Vector3[] points)
    {
        refs   = new NetworkObjectReference[hits.Count];
        counts = new int[hits.Count];

        List<Vector3> flat = new List<Vector3>();
        int index = 0;
        foreach (var kvp in hits)
        {
            refs[index]   = new NetworkObjectReference(kvp.Key);
            counts[index] = kvp.Value.Count;
            flat.AddRange(kvp.Value);
            index++;
        }

        points = flat.ToArray();
    }

    private static void ToArrays(Dictionary<NetworkObject, int> hits,
        out NetworkObjectReference[] refs, out int[] counts)
    {
        refs   = new NetworkObjectReference[hits.Count];
        counts = new int[hits.Count];

        int index = 0;
        foreach (var kvp in hits)
        {
            refs[index]   = new NetworkObjectReference(kvp.Key);
            counts[index] = kvp.Value;
            index++;
        }
    }

    public void ShootFX()
    {
        shootVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        shootVFX.Play();
        // Cosmetic-only replay: no camera impulse (world-space, would shake observers).
        // The shooter generates it locally in OnStartUse.
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
    /// Sanity limit (metres) between a reported pellet point and the target's nearest collider.
    /// Rejects impossible reports only; never re-litigates a legitimate pellet.
    /// </summary>
    private const float MaxReportedHitDistance = 6f;

    /// <summary>
    /// Server-side: validates the shooter, spends a round, then applies the per-target pellet counts
    /// the CLIENT resolved in <see cref="ResolveBlast"/>. Damage from multiple pellets hitting the
    /// same mutant/player is accumulated into one call; the glass registers at most one hit per
    /// blast, mirroring <see cref="MutantSuspectBehaviour"/>'s hit pattern.
    /// The server does not re-trace the cone — it trusts what the shooter's own machine hit, so a
    /// blast that visibly connected always lands. Damage/health/death stay server-owned, and the
    /// pellet counts are clamped so a bad report can't inflate damage.
    /// RequireOwnership = false because ownership transfer may still be in flight when the RPC lands.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 rayDirection,
        NetworkObjectReference[] mutantRefs, int[] mutantPellets, Vector3[] mutantPelletPoints,
        NetworkObjectReference[] playerRefs, int[] playerPellets,
        NetworkObjectReference[] suspectRefs, Vector3[] suspectPoints,
        bool hitGlass, Vector3[] propPoints, ServerRpcParams rpcParams = default)
    {
        ulong shooterClientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterClientId, out var client))
            return;

        // Only the client actually holding this shotgun may fire.
        PlayerPickupController ppc = client.PlayerObject?.GetComponent<PlayerPickupController>();
        if (ppc == null) return;
        if (!ppc.HeldObjectRef.TryGet(out NetworkObject heldNetObj) || heldNetObj != NetworkObject) return;

        if (_roundsRemaining.Value <= 0) return;

        _roundsRemaining.Value--;

        if (mutantRefs != null && mutantPellets != null)
        {
            int count = Mathf.Min(mutantRefs.Length, mutantPellets.Length);
            int pointOffset = 0;
            for (int i = 0; i < count; i++)
            {
                int reportedPellets = Mathf.Max(0, mutantPellets[i]);
                int sliceStart = pointOffset;
                pointOffset += reportedPellets;

                if (!mutantRefs[i].TryGet(out NetworkObject targetObj) || targetObj == null) continue;

                MutantEnemy enemy = targetObj.GetComponent<MutantEnemy>() ?? targetObj.GetComponentInChildren<MutantEnemy>();
                if (enemy == null) continue;

                int pellets = Mathf.Clamp(reportedPellets, 0, _pelletCount);
                if (pellets <= 0) continue;

                // Pellet impact points that pass the sanity bound (to the nearest collider, not the pivot).
                List<Vector3> validPoints = new List<Vector3>(pellets);
                if (mutantPelletPoints != null)
                {
                    for (int p = sliceStart; p < sliceStart + pellets && p < mutantPelletPoints.Length; p++)
                    {
                        if (CombatHitUtility.DistanceToTarget(targetObj, mutantPelletPoints[p]) <= MaxReportedHitDistance)
                            validPoints.Add(mutantPelletPoints[p]);
                    }
                }

                Vector3 damagePoint = validPoints.Count > 0 ? validPoints[0] : enemy.transform.position;
                enemy.TakeDamage(_mutantPelletDamage * pellets, damagePoint);

                if (validPoints.Count > 0)
                    enemy.PlayLimbReactions(validPoints.ToArray(), rayDirection, _mutantPelletDamage);
            }
        }

        if (playerRefs != null && playerPellets != null)
        {
            int count = Mathf.Min(playerRefs.Length, playerPellets.Length);
            for (int i = 0; i < count; i++)
            {
                if (!playerRefs[i].TryGet(out NetworkObject targetObj) || targetObj == null) continue;

                // Never let a blast hurt the shooter, whatever the client claims.
                if (targetObj.OwnerClientId == shooterClientId) continue;

                PlayerHealth playerHealth = targetObj.GetComponent<PlayerHealth>() ?? targetObj.GetComponentInChildren<PlayerHealth>();
                if (playerHealth == null) continue;

                int pellets = Mathf.Clamp(playerPellets[i], 0, _pelletCount);
                if (pellets <= 0) continue;

                playerHealth.TakeDamage(_playerPelletDamage * pellets, EffectKeys.FriendlyGunshotDamage, targetObj.transform.position);
            }
        }

        if (suspectRefs != null && suspectPoints != null)
        {
            int count = Mathf.Min(suspectRefs.Length, suspectPoints.Length);
            for (int i = 0; i < count; i++)
            {
                if (!suspectRefs[i].TryGet(out NetworkObject targetObj) || targetObj == null) continue;

                // Sanity bound, NOT a hit test — rejects a report that could only come from a bug.
                if (Vector3.Distance(targetObj.transform.position, suspectPoints[i]) > 6f) continue;

                SuspectCharacter suspect = targetObj.GetComponent<SuspectCharacter>() ?? targetObj.GetComponentInChildren<SuspectCharacter>();
                suspect?.PlayHarmlessHitReaction(suspectPoints[i]);
            }
        }

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

        if (propPoints != null && propPoints.Length > 0)
        {
            // Cosmetic only — replay on every client except the shooter (who already played it).
            // Clamp to the pellet count so a bad report can't spam reactions.
            if (propPoints.Length > _pelletCount)
                System.Array.Resize(ref propPoints, _pelletCount);

            List<ulong> propTargets = new List<ulong>();
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                if (id != shooterClientId) propTargets.Add(id);

            if (propTargets.Count > 0)
                ShotgunPropHitClientRpc(propPoints, rayDirection, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = propTargets }
                });
        }
    }

    /// <summary>Replays cosmetic <see cref="HittableProp"/> reactions on non-shooting clients.</summary>
    [ClientRpc]
    private void ShotgunPropHitClientRpc(Vector3[] hitPoints, Vector3 direction, ClientRpcParams clientRpcParams = default)
    {
        foreach (Vector3 point in hitPoints)
            HittableProp.TryHitAt(point, direction);
    }

    /// <summary>
    /// Returns a random direction within a cone of half-angle <paramref name="maxAngleDegrees"/>
    /// around <paramref name="forward"/>, used to spread the shotgun's pellets.
    /// </summary>
    private static Vector3 RandomConeDirection(Vector3 forward, float maxAngleDegrees)
    {
        float angle = UnityEngine.Random.Range(0f, maxAngleDegrees) * Mathf.Deg2Rad;
        float rotation = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;

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

    // ── Reloading (KeyCode.R, from PlayerAmmoReserve) ──────────────────────────

    public AmmoType ReserveAmmoType => AmmoType.Shotgun;

    public bool NeedsReload => _roundsRemaining.Value < MaxRounds;

    /// <summary>
    /// Called by <see cref="PlayerInventory"/> when the local player presses R with this shotgun
    /// equipped. Skips the round-trip when the tube is full or the local reserve is empty.
    /// </summary>
    public void RequestReloadFromReserve()
    {
        if (_roundsRemaining.Value >= MaxRounds) return;

        PlayerAmmoReserve reserve = PlayerAmmoReserve.Local;
        if (reserve != null && reserve.Get(ReserveAmmoType) <= 0) return;

        ReloadFromReserveServerRpc();
    }

    /// <summary>
    /// Server: validates the sender is holding this shotgun, then moves only the shells needed to
    /// reach <see cref="MaxRounds"/> from their <see cref="PlayerAmmoReserve"/> into the shotgun.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReloadFromReserveServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!PlayerAmmoReserve.TryGetForHolder(clientId, NetworkObject, out PlayerAmmoReserve reserve))
        {
            Debug.LogWarning($"[Shotgun] ReloadFromReserveServerRpc: client {clientId} is not holding this shotgun.");
            return;
        }

        int needed = MaxRounds - _roundsRemaining.Value;
        if (needed <= 0) return;

        int taken = reserve.Take(ReserveAmmoType, needed);
        if (taken <= 0) return;

        _roundsRemaining.Value += taken;
        PlayReloadSoundClientRpc();
    }

    /// <summary>Plays the reload sound at the shotgun on every client after a successful reload.</summary>
    [ClientRpc]
    private void PlayReloadSoundClientRpc()
    {
        if (_reloadSound != null)
            SFXController.Instance.PlayAtPosition(_reloadSound, transform.position);
    }
}
