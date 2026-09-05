using System.Collections;
using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Pairs a renderer with the material slot and replacement material to apply on resurrection.
/// Add one entry per face mesh in <see cref="CorpseResurrectionController.faceMaterialSwaps"/>.
/// </summary>
[System.Serializable]
public struct FaceMaterialSwap
{
    [Tooltip("The Renderer containing the face material (e.g. a head SkinnedMeshRenderer).")]
    public Renderer renderer;
    [Tooltip("Index of the material slot to replace inside renderer.sharedMaterials.")]
    public int materialIndex;
    [Tooltip("Material to apply to this slot on resurrection.")]
    public Material uncannyMaterial;
}


///
/// After the player dies, a server-authoritative countdown begins. If the corpse
/// is NOT burned before <see cref="resurrectionDelay"/> seconds elapse, the ragdoll
/// deactivates, the body stands up with a tall, twisted silhouette, and control of
/// chasing/attacking is handed off to <see cref="MutantEnemy"/> — exactly like a
/// normal mutant — so it aggros and pursues living players using the same
/// NavMeshAgent-driven chase loop, door bashing, and melee attack pipeline.
///
/// Burning is triggered by external code (e.g. <see cref="Flamethrower"/>) calling
/// <see cref="BurnCorpse"/> on the server, which permanently cancels resurrection.
/// Once resurrected, the same fire (via <see cref="SetOnFire"/>) applies damage
/// directly to <see cref="MutantEnemy"/>, which is the only thing able to finish it off
/// permanently (see <c>fleeInsteadOfDie</c> on the MutantEnemyData/MutantEnemy setup).
///
/// The corpse — whether still inert or already resurrected — persists independently of
/// the owning client's respawn cycle: <see cref="HasActiveCorpse"/> and
/// <see cref="DetachFromPlayer"/> let <see cref="ReviveManager"/> detach this
/// GameObject from the reviving client instead of destroying it, so it keeps existing
/// in the world until it is killed by fire.
///
/// Prefab requirements:
///   - NavMeshAgent component on the same GameObject (disabled by default)
///   - MutantEnemy component on the same GameObject (disabled/dormant by default;
///     configured with a MutantEnemyData asset, an attack hitbox, etc.)
///   - SetOnFire component on the same GameObject (for fire VFX/damage)
///   - Humanoid Animator for the bone distortion pass
/// </summary>
public class CorpseResurrectionController : NetworkBehaviour
{
    [Header("Resurrection Timing")]
    [Tooltip("Seconds after death before the corpse resurrects if not burned.")]
    [SerializeField] private float resurrectionDelay = 30f;

    [Header("Mutant Control")]
    [Tooltip("The MutantEnemy component on this same GameObject that drives chasing, attacking, and death once resurrected. Kept dormant until Resurrect() calls InitialiseServer() on it.")]
    [SerializeField] private MutantEnemy mutantEnemy;

    [Tooltip("Animator Controller swapped onto the Animator on resurrection so the corpse uses mutant locomotion/attack/death animations instead of the player's own controller (e.g. the Mutant Human override controller).")]
    [SerializeField] private RuntimeAnimatorController resurrectedAnimatorController;

    [Header("Twisted Visuals — Segment Stretch")]
    [Tooltip("Multiplies the Spine → Chest bone distance.")]
    [SerializeField] private float spineStretch    = 1.5f;
    [Tooltip("Multiplies the Chest → UpperChest bone distance.")]
    [SerializeField] private float chestStretch    = 1.5f;
    [Tooltip("Multiplies the Neck (UpperChest → Neck) bone distance.")]
    [SerializeField] private float neckStretch     = 1.0f;
    [Tooltip("Multiplies the UpperArm (shoulder → elbow) bone distance.")]
    [SerializeField] private float upperArmStretch = 1.4f;
    [Tooltip("Multiplies the LowerArm (elbow → wrist) bone distance.")]
    [SerializeField] private float lowerArmStretch = 1.4f;
    [Tooltip("Multiplies the Hand (wrist → hand root) bone distance.")]
    [SerializeField] private float handStretch     = 1.0f;
    [Tooltip("Multiplies the UpperLeg (hip → knee) bone distance. The Hips bone is automatically raised to compensate so feet remain on the ground.")]
    [SerializeField] private float upperLegStretch = 1.4f;
    [Tooltip("Multiplies the LowerLeg (knee → ankle) bone distance. The Hips bone is automatically raised to compensate so feet remain on the ground.")]
    [SerializeField] private float lowerLegStretch = 1.4f;

    [Header("Twisted Visuals — Spine Rotation (LateUpdate offset)")]
    [Tooltip("Euler angle offsets applied to the Spine bone every LateUpdate on top of the Animator's pose.")]
    [SerializeField] private Vector3 spineEulerOffset      = new Vector3(0f,  18f,  12f);
    [Tooltip("Euler angle offsets applied to the Chest bone every LateUpdate on top of the Animator's pose.")]
    [SerializeField] private Vector3 chestEulerOffset      = new Vector3(0f, -22f,  -9f);
    [Tooltip("Euler angle offsets applied to the UpperChest bone every LateUpdate on top of the Animator's pose.")]
    [SerializeField] private Vector3 upperChestEulerOffset = new Vector3(4f,  14f,   0f);

    [Header("Face Swap")]
    [Tooltip("One entry per face mesh that needs a material swap on resurrection.")]
    [SerializeField] private FaceMaterialSwap[] faceMaterialSwaps = System.Array.Empty<FaceMaterialSwap>();

    // ── Networked state ────────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _isResurrected = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Component cache ────────────────────────────────────────────────────────

    private PlayerHealth _playerHealth;
    private RagdollController _ragdollController;
    private Animator _animator;
    private NavMeshAgent _navMeshAgent;
    private Unity.Netcode.Components.NetworkTransform _networkTransform;

    // ── Bone transform cache ───────────────────────────────────────────────────

    private Transform _boneHips;
    private Transform _boneSpine;
    private Transform _boneChest;
    private Transform _boneUpperChest;
    private Transform _boneNeck;
    private Transform _boneLeftHand;
    private Transform _boneRightHand;
    private Transform _boneLeftUpperArm;
    private Transform _boneLeftLowerArm;
    private Transform _boneRightUpperArm;
    private Transform _boneRightLowerArm;
    private Transform _boneLeftUpperLeg;
    private Transform _boneLeftLowerLeg;
    private Transform _boneRightUpperLeg;
    private Transform _boneRightLowerLeg;

    // Original localPositions captured at Awake (bind/spawn pose), used as the stretch baseline.
    private Vector3 _origChestLocalPos;
    private Vector3 _origUpperChestLocalPos;
    private Vector3 _origNeckLocalPos;
    private Vector3 _origLeftUpperArmLocalPos;
    private Vector3 _origLeftLowerArmLocalPos;
    private Vector3 _origRightUpperArmLocalPos;
    private Vector3 _origRightLowerArmLocalPos;
    private Vector3 _origLeftHandLocalPos;
    private Vector3 _origRightHandLocalPos;
    private Vector3 _origLeftUpperLegLocalPos;
    private Vector3 _origLeftLowerLegLocalPos;
    private Vector3 _origRightUpperLegLocalPos;
    private Vector3 _origRightLowerLegLocalPos;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private bool _hasBeenBurned;
    private Coroutine _resurrectionCoroutine;

    /// <summary>
    /// True from the moment this player dies until the corpse (or the mutant it resurrects
    /// into) is permanently destroyed — i.e. killed by fire. Server-only.
    /// <see cref="ReviveManager"/> reads this to decide whether the reviving client's old
    /// PlayerObject must be detached and kept alive instead of destroyed.
    /// </summary>
    public bool HasActiveCorpse => _hasActiveCorpse;
    private bool _hasActiveCorpse;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _playerHealth     = GetComponent<PlayerHealth>();
        _ragdollController = GetComponent<RagdollController>();
        _animator         = GetComponent<Animator>();
        _navMeshAgent     = GetComponent<NavMeshAgent>();
        _networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

        if (mutantEnemy == null)
            mutantEnemy = GetComponent<MutantEnemy>();

        // Agent stays disabled until resurrection so it doesn't fight CharacterController.
        if (_navMeshAgent != null)
            _navMeshAgent.enabled = false;

        if (mutantEnemy != null)
        {
            // Must run before this NetworkObject spawns so MutantEnemy stays dormant until
            // Resurrect() explicitly calls InitialiseServer() on it. MutantEnemy's own Unity
            // 'enabled' flag is never toggled (it stays permanently true from its own Awake) —
            // dormancy is tracked by its internal _isActive NetworkVariable instead, which
            // already defaults to false, so there is nothing to disable here.
            mutantEnemy.DisableAutoInit();
        }

        // Cache bones and their spawn-pose localPositions now, before any ragdoll
        // or animation has a chance to move them. These are the stretch baseline values.
        CacheBoneTransforms();
        CacheOriginalLocalPositions();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (_playerHealth != null)
            _playerHealth.OnDeath += OnPlayerDeath;

        _isResurrected.OnValueChanged += OnResurrectedChanged;

        if (mutantEnemy != null)
            mutantEnemy.OnRemovedFromPlay += OnMutantRemovedFromPlay;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_playerHealth != null)
            _playerHealth.OnDeath -= OnPlayerDeath;

        _isResurrected.OnValueChanged -= OnResurrectedChanged;

        if (mutantEnemy != null)
            mutantEnemy.OnRemovedFromPlay -= OnMutantRemovedFromPlay;
    }

    private void OnMutantRemovedFromPlay()
    {
        // The resurrected mutant has been permanently destroyed (killed by fire) —
        // this GameObject is about to be despawned by MutantEnemy itself.
        _hasActiveCorpse = false;
    }

    // ── Death trigger ─────────────────────────────────────────────────────────

    private void OnPlayerDeath()
    {
        if (!IsServer) return;

        _hasBeenBurned = false;
        _hasActiveCorpse = true;
        _resurrectionCoroutine = StartCoroutine(ResurrectionCountdown());
    }

    // ── Detach from reviving player (server-only) ───────────────────────────────

    /// <summary>
    /// Called by <see cref="ReviveManager"/> after Netcode has replaced the former owner's
    /// PlayerObject and transferred this retained corpse to server ownership. Nothing further
    /// needs to change here — RagdollController/Resurrect() have already left the Animator,
    /// NavMeshAgent, and NetworkTransform in whatever state is correct for this corpse's
    /// current stage (inert corpse vs. actively resurrected mutant); this hook exists so
    /// the detach moment is explicit and easy to extend later (e.g. renaming the object,
    /// clearing name-tag/UI references) without touching ReviveManager again.
    /// </summary>
    public void DetachFromPlayer()
    {
        if (!IsServer) return;

        Debug.Log($"[CorpseResurrection] {gameObject.name} detached from its former player — " +
                   $"will persist in the world as an independent {(_isResurrected.Value ? "resurrected mutant" : "corpse")} until killed by fire.");
    }

    /// <summary>
    /// Clears client-local player state before this retained corpse is demoted from a
    /// PlayerObject and a replacement player object is spawned for its former owner.
    /// SERVER ONLY.
    /// </summary>
    public void PrepareForPlayerObjectReplacement()
    {
        if (IsServer)
            PrepareForPlayerObjectReplacementClientRpc();
    }

    [ClientRpc]
    private void PrepareForPlayerObjectReplacementClientRpc()
    {
        GetComponent<PlayerInstance>()?.DetachFromPlayerObject();
    }


    // ── Burn API (server-only) ─────────────────────────────────────────────────

    /// <summary>
    /// Permanently cancels the pending resurrection.
    /// Must be called on the server — e.g. by <see cref="Flamethrower"/> when its
    /// flame hits a dead player body.
    /// </summary>
    public void BurnCorpse()
    {
        if (!IsServer || _isResurrected.Value) return;

        _hasBeenBurned = true;

        if (_resurrectionCoroutine != null)
        {
            StopCoroutine(_resurrectionCoroutine);
            _resurrectionCoroutine = null;
        }

        Debug.Log($"[CorpseResurrection] Corpse of {gameObject.name} burned — resurrection cancelled.");
    }

    // ── Countdown ─────────────────────────────────────────────────────────────

    private IEnumerator ResurrectionCountdown()
    {
        yield return new WaitForSeconds(resurrectionDelay);

        if (!_hasBeenBurned)
            Resurrect();
    }

    // ── Resurrection (server) ─────────────────────────────────────────────────

    private void Resurrect()
    {
        if (!IsServer || _isResurrected.Value) return;

        // Snap to the nearest valid NavMesh position — the ragdoll may have slid off-mesh.
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            transform.position = hit.position;

        _isResurrected.Value = true;

        // Trigger visual resurrection on all clients.
        ResurrectClientRpc();

        // Server: enable the NavMeshAgent so MutantEnemy can drive movement.
        if (_navMeshAgent != null)
            _navMeshAgent.enabled = true;

        // Re-enable NetworkTransform so the server-driven position replicates to clients.
        if (_networkTransform != null)
            _networkTransform.enabled = true;

        // Hand off to MutantEnemy — it owns chasing, retargeting, door bashing, attacking,
        // animation speed/grounded syncing, and death/fire handling from this point on.
        if (mutantEnemy != null)
        {
            mutantEnemy.SetAnimator(_animator);
            mutantEnemy.InitialiseServer();
        }
        else
        {
            Debug.LogWarning($"[CorpseResurrection] No MutantEnemy assigned on {gameObject.name} — " +
                               "the resurrected corpse will not chase or attack players.");
        }

        Debug.Log($"[CorpseResurrection] {gameObject.name} has resurrected!");
    }

    // ── Client-side visuals ───────────────────────────────────────────────────

    [ClientRpc]
    private void ResurrectClientRpc()
    {
        // Deactivate ragdoll physics and re-enable the Animator.
        _ragdollController?.SetRagdollActive(false);

        if (_animator != null)
        {
            // Swap to the mutant's Animator Controller so locomotion/attack/death states
            // (driven by MutantEnemy) play instead of the player's own controller.
            if (resurrectedAnimatorController != null)
                _animator.runtimeAnimatorController = resurrectedAnimatorController;

            _animator.enabled = true;
            // Reset to the entry/idle state so the corpse doesn't resume mid-walk.
            _animator.Rebind();
            _animator.Update(0f);
        }

        // Swap face materials on this client's renderer instances.
        foreach (FaceMaterialSwap swap in faceMaterialSwaps)
        {
            if (swap.renderer == null || swap.uncannyMaterial == null) continue;

            Material[] mats = swap.renderer.materials;
            if (swap.materialIndex >= 0 && swap.materialIndex < mats.Length)
                mats[swap.materialIndex] = swap.uncannyMaterial;
            swap.renderer.materials = mats;
        }

        // Bone distortions are applied every LateUpdate — nothing extra needed here.
    }

    private void OnResurrectedChanged(bool previous, bool current)
    {
        // Late-joiner sync: ensure the Animator is running (with the mutant controller) so
        // LateUpdate can distort bones and MutantEnemy's animation syncing takes effect.
        if (current && !previous && _animator != null)
        {
            if (resurrectedAnimatorController != null)
                _animator.runtimeAnimatorController = resurrectedAnimatorController;

            _animator.enabled = true;
        }
    }

    // ── Bone caching ──────────────────────────────────────────────────────────

    private void CacheBoneTransforms()
    {
        if (_animator == null || !_animator.isHuman) return;

        _boneHips          = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _boneSpine         = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _boneChest         = _animator.GetBoneTransform(HumanBodyBones.Chest);
        _boneUpperChest    = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        _boneNeck          = _animator.GetBoneTransform(HumanBodyBones.Neck);
        _boneLeftUpperArm  = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        _boneLeftLowerArm  = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        _boneLeftHand      = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        _boneRightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _boneRightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _boneRightHand     = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        _boneLeftUpperLeg  = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        _boneLeftLowerLeg  = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _boneRightUpperLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        _boneRightLowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
    }

    /// <summary>
    /// Snapshots each bone's localPosition relative to its parent at spawn/bind pose.
    /// Called once in Awake before any ragdoll or animation modifies the skeleton.
    /// These values are multiplied by the stretch fields in LateUpdate.
    /// </summary>
    private void CacheOriginalLocalPositions()
    {
        if (_boneChest != null)         _origChestLocalPos         = _boneChest.localPosition;
        if (_boneUpperChest != null)    _origUpperChestLocalPos    = _boneUpperChest.localPosition;
        if (_boneNeck != null)          _origNeckLocalPos          = _boneNeck.localPosition;
        if (_boneLeftUpperArm != null)  _origLeftUpperArmLocalPos  = _boneLeftUpperArm.localPosition;
        if (_boneLeftLowerArm != null)  _origLeftLowerArmLocalPos  = _boneLeftLowerArm.localPosition;
        if (_boneLeftHand != null)      _origLeftHandLocalPos      = _boneLeftHand.localPosition;
        if (_boneRightUpperArm != null) _origRightUpperArmLocalPos = _boneRightUpperArm.localPosition;
        if (_boneRightLowerArm != null) _origRightLowerArmLocalPos = _boneRightLowerArm.localPosition;
        if (_boneRightHand != null)     _origRightHandLocalPos     = _boneRightHand.localPosition;
        if (_boneLeftUpperLeg != null)  _origLeftUpperLegLocalPos  = _boneLeftUpperLeg.localPosition;
        if (_boneLeftLowerLeg != null)  _origLeftLowerLegLocalPos  = _boneLeftLowerLeg.localPosition;
        if (_boneRightUpperLeg != null) _origRightUpperLegLocalPos = _boneRightUpperLeg.localPosition;
        if (_boneRightLowerLeg != null) _origRightLowerLegLocalPos = _boneRightLowerLeg.localPosition;
    }

    // ── LateUpdate: stretch + twist ───────────────────────────────────────────

    /// <summary>
    /// Runs after the Animator has written its pose for this frame.
    /// Overwrites bone localPositions with stretch-multiplied versions and adds rotation
    /// offsets on top of the Animator's output. Both respond to inspector value changes
    /// in real time since they are re-applied every frame.
    /// </summary>
    private void LateUpdate()
    {
        if (!_isResurrected.Value) return;

        // ── Segment stretch ───────────────────────────────────────────────────
        // The Humanoid animator drives localRotation via muscle curves but does NOT
        // write localPosition for non-root bones, so multiplying the original offset
        // cleanly increases the inter-bone distance without disturbing the mesh topology.

        if (_boneChest != null)
            _boneChest.localPosition = _origChestLocalPos * spineStretch;

        if (_boneUpperChest != null)
            _boneUpperChest.localPosition = _origUpperChestLocalPos * chestStretch;

        if (_boneNeck != null)
            _boneNeck.localPosition = _origNeckLocalPos * neckStretch;

        if (_boneLeftUpperArm != null)
            _boneLeftUpperArm.localPosition  = _origLeftUpperArmLocalPos  * upperArmStretch;
        if (_boneLeftLowerArm != null)
            _boneLeftLowerArm.localPosition  = _origLeftLowerArmLocalPos  * lowerArmStretch;
        if (_boneLeftHand != null)
            _boneLeftHand.localPosition      = _origLeftHandLocalPos      * handStretch;
        if (_boneRightUpperArm != null)
            _boneRightUpperArm.localPosition = _origRightUpperArmLocalPos * upperArmStretch;
        if (_boneRightLowerArm != null)
            _boneRightLowerArm.localPosition = _origRightLowerArmLocalPos * lowerArmStretch;
        if (_boneRightHand != null)
            _boneRightHand.localPosition     = _origRightHandLocalPos     * handStretch;

        if (_boneLeftUpperLeg != null)
            _boneLeftUpperLeg.localPosition  = _origLeftUpperLegLocalPos  * upperLegStretch;
        if (_boneLeftLowerLeg != null)
            _boneLeftLowerLeg.localPosition  = _origLeftLowerLegLocalPos  * lowerLegStretch;
        if (_boneRightUpperLeg != null)
            _boneRightUpperLeg.localPosition = _origRightUpperLegLocalPos * upperLegStretch;
        if (_boneRightLowerLeg != null)
            _boneRightLowerLeg.localPosition = _origRightLowerLegLocalPos * lowerLegStretch;

        // ── Hip compensation ──────────────────────────────────────────────────
        // When legs are stretched, the feet descend into the ground. Compensate by
        // raising the Hips by the exact extra length added to both leg segments.
        // We read _origLeftUpperLegLocalPos.magnitude as the natural upper-leg length —
        // the left leg is used as the representative since both sides are symmetric.
        // Adding to localPosition here is additive on top of whatever the Animator
        // wrote for this frame (foot IK, root motion, etc.).
        if (_boneHips != null)
        {
            float extraLength = 0f;
            if (_boneLeftUpperLeg != null)
                extraLength += _origLeftUpperLegLocalPos.magnitude * (upperLegStretch - 1f);
            if (_boneLeftLowerLeg != null)
                extraLength += _origLeftLowerLegLocalPos.magnitude * (lowerLegStretch - 1f);

            if (extraLength > 0f)
                _boneHips.localPosition += Vector3.up * extraLength;
        }

        // ── Spine rotation offsets ────────────────────────────────────────────
        // Additive on top of the Animator's rotation output, producing an asymmetric
        // writhing twist. Applied after positions so the twist reads correctly.
        if (_boneSpine != null)
            _boneSpine.localRotation      *= Quaternion.Euler(spineEulerOffset);
        if (_boneChest != null)
            _boneChest.localRotation      *= Quaternion.Euler(chestEulerOffset);
        if (_boneUpperChest != null)
            _boneUpperChest.localRotation *= Quaternion.Euler(upperChestEulerOffset);
    }

    // ── Server chase loop ─────────────────────────────────────────────────────
    // Chasing, retargeting, door bashing, attacking, and rotation-facing are all owned by
    // MutantEnemy once InitialiseServer() is called from Resurrect() above — no duplicate
    // logic needed here.
}
