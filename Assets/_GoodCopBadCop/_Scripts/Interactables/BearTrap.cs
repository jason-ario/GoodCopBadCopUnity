using GoodCopBadCop.Effects;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A placeable bear trap that inherits from <see cref="PickableObject"/>.
///
/// Interaction model (when placed in the world, not held):
///   LMB  -> picks the trap up (auto-disarms if armed).
///   E    -> arms the trap; triggers the "BearTrapSet" animator bool and
///           activates the two trigger colliders so victims can be caught.
///
/// Two separate trigger zones handle player vs. enemy detection independently:
///   - <see cref="_playerTrigger"/>: catches <see cref="PlayerMovementController"/> victims.
///   - <see cref="_enemyTrigger"/>:  catches <see cref="MutantEnemy"/> victims.
///
/// If either victim type is already inside the trigger zone when the trap is
/// armed, they are ignored until they exit and re-enter the zone.
///
/// When a victim walks into an active trigger zone:
///   - The trap snaps shut (disarmed, colliders disabled, animator bool cleared).
///   - The victim takes <see cref="_trapDamage"/> instantly.
///   - The victim is immobilized for <see cref="_trapDuration"/> seconds.
///   - After the duration the victim is released; the trap stays in the world,
///     unset, ready to be rearmed with E.
///
/// Setup requirements:
///   - Assign a <see cref="BearTrapTrigger"/> (with _isPlayerTrigger = true) on the
///     player trigger child to <see cref="_playerTrigger"/>.
///   - Assign a <see cref="BearTrapTrigger"/> (with _isPlayerTrigger = false) on the
///     enemy trigger child to <see cref="_enemyTrigger"/>.
///   - Assign an Animator that exposes a bool parameter matching <see cref="_bearTrapSetBool"/>.
/// </summary>
public class BearTrap : PickableObject
{

    // Constants

    private const string UnarmedInteractText = "Arm Trap";
    private const string ArmedInteractText = "Pick Up";


    // Inspector

    [Header("Bear Trap")]
    [Tooltip("Animator on the trap mesh. Must expose the bool defined in Bear Trap Set Bool.")]
    [SerializeField] private Animator _trapAnimator;

    [Tooltip("BearTrapTrigger on the child trigger zone that catches players (isPlayerTrigger = true).")]
    [SerializeField] private BearTrapTrigger _playerTrigger;

    [Tooltip("BearTrapTrigger on the child trigger zone that catches enemies (isPlayerTrigger = false).")]
    [SerializeField] private BearTrapTrigger _enemyTrigger;

    [Tooltip("Animator bool parameter name that represents the trap being armed/set.")]
    [SerializeField] private string _bearTrapSetBool = "BearTrapSet";

    [Tooltip("Animator trigger parameter name fired when the trap snaps shut.")]
    [SerializeField] private string _bearTrapSnapTrigger = "BearTrapSnap";

    [Tooltip("Seconds the victim remains immobilized after triggering the trap.")]
    [SerializeField] private float _trapDuration = 5f;

    [Tooltip("Instant damage dealt to the victim when the trap snaps shut.")]
    [SerializeField] private float _trapDamage = 50f;

    [Tooltip("AudioSource used to play sounds on all clients.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Sound played on all clients when the trap is armed.")]
    [SerializeField] private AudioClip _setSound;

    [Tooltip("Sound played on all clients when the trap snaps shut.")]
    [SerializeField] private AudioClip _snapSound;


    // Networked State

    private readonly NetworkVariable<bool> _isArmed = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);


    // Local State

    /// <summary>True between the moment a victim enters and the moment they are released.</summary>
    private bool _isTriggered;

    /// <summary>
    /// Colliders that were already overlapping a trigger zone when the trap was armed.
    /// They are ignored until they exit and re-enter.
    /// </summary>
    private readonly HashSet<Collider> _ignoredOnArm = new();

    // Cached Collider components from the two trigger children.
    private Collider _playerColliderCache;
    private Collider _enemyColliderCache;

    private Collider PlayerCollider
    {
        get
        {
            if (_playerColliderCache == null && _playerTrigger != null)
                _playerColliderCache = _playerTrigger.GetComponent<Collider>();
            return _playerColliderCache;
        }
    }

    private Collider EnemyCollider
    {
        get
        {
            if (_enemyColliderCache == null && _enemyTrigger != null)
                _enemyColliderCache = _enemyTrigger.GetComponent<Collider>();
            return _enemyColliderCache;
        }
    }


    // Lifecycle

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isArmed.OnValueChanged += OnArmedStateChanged;
        ApplyArmedVisuals(_isArmed.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isArmed.OnValueChanged -= OnArmedStateChanged;
    }


    // Interaction Overrides

    /// <summary>
    /// E key: arms the trap when it is placed and not yet armed.
    /// Has no effect while the trap is held, already armed, or currently triggered.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
    {
        if (IsHeld || _isArmed.Value || _isTriggered) return;
        ArmTrapServerRpc();
    }

    /// <summary>
    /// Called when the trap is picked up. Disarms the trap on the server so
    /// the trigger colliders and animator are reset before the object enters
    /// the player's hands.
    /// </summary>
    public override void OnPickedUp()
    {
        base.OnPickedUp();
        _ignoredOnArm.Clear();
        ApplyArmedVisuals(false);
        if (IsSpawned)
            DisarmServerRpc();
    }


    // Trigger Zone Callbacks (called by BearTrapTrigger children)

    /// <summary>
    /// Invoked by a <see cref="BearTrapTrigger"/> child when a relevant collider
    /// enters its zone. Colliders that were already present when the trap was armed
    /// are ignored until they exit and re-enter.
    /// </summary>
    public void OnTriggerZoneEntered(Collider other, bool isPlayer)
    {
        if (!_isArmed.Value || _ignoredOnArm.Contains(other)) return;

        if (isPlayer)
        {
            PlayerMovementController pm = other.GetComponentInParent<PlayerMovementController>();
            if (pm == null) return;
            NetworkObject netObj = pm.GetComponent<NetworkObject>();
            if (netObj != null) ReportVictimServerRpc(netObj, isPlayer: true);
        }
        else
        {
            MutantEnemy mutant = other.GetComponentInParent<MutantEnemy>();
            if (mutant == null || mutant.IsDead) return;
            NetworkObject netObj = mutant.GetComponent<NetworkObject>();
            if (netObj != null) ReportVictimServerRpc(netObj, isPlayer: false);
        }
    }

    /// <summary>
    /// Invoked by a <see cref="BearTrapTrigger"/> child when a relevant collider
    /// exits its zone. Removes the collider from the arm-time ignore list so
    /// it can trigger the trap if it re-enters.
    /// </summary>
    public void OnTriggerZoneExited(Collider other, bool isPlayer)
    {
        _ignoredOnArm.Remove(other);
    }


    // Server Helpers

    /// <summary>
    /// Received from any client that detected a victim entering a trigger zone.
    /// All snap logic runs server-side to remain authoritative.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ReportVictimServerRpc(NetworkObjectReference victimRef, bool isPlayer)
    {
        // Double-entry guard - multiple clients (or a late physics tick) may report
        // the same victim before _isArmed propagates back to false.
        if (!_isArmed.Value || _isTriggered) return;
        if (!victimRef.TryGet(out NetworkObject victimObj)) return;

        if (isPlayer)
        {
            PlayerMovementController pm = victimObj.GetComponent<PlayerMovementController>();
            if (pm == null) return;

            SnapShut();

            victimObj.GetComponent<PlayerHealth>()?.TakeDamage(_trapDamage, EffectKeys.BearTrapDamage);
            TrapPlayerClientRpc(victimObj);
            StartCoroutine(ReleasePlayerCoroutine(victimObj, _trapDuration));
        }
        else
        {
            MutantEnemy mutant = victimObj.GetComponent<MutantEnemy>();
            if (mutant == null || mutant.IsDead) return;

            SnapShut();

            mutant.TakeDamage(_trapDamage, victimObj.transform.position);
            mutant.Immobilize(_trapDuration);
            StartCoroutine(FinishTrapCoroutine(_trapDuration));
        }
    }

    /// <summary>
    /// Marks the trap as triggered and clears the armed state on the server.
    /// Pickup is blocked on all clients until the victim is released.
    /// </summary>
    private void SnapShut()
    {
        _isTriggered = true;
        _isArmed.Value = false;
        PlaySnapEffectsClientRpc();
        SetPickupAllowedClientRpc(false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ArmTrapServerRpc()
    {
        if (_isArmed.Value || _isTriggered) return;
        _isArmed.Value = true;
        PlaySetSoundClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void DisarmServerRpc()
    {
        _isArmed.Value = false;
    }

    private IEnumerator ReleasePlayerCoroutine(NetworkObject playerNetObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        ReleasePlayerClientRpc(playerNetObj);
        _isTriggered = false;
        SetPickupAllowedClientRpc(true);
    }

    private IEnumerator FinishTrapCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isTriggered = false;
        SetPickupAllowedClientRpc(true);
    }


    // Client RPCs

    /// <summary>
    /// Received on all clients. Only the matching local player freezes their
    /// own movement controller.
    /// </summary>
    [ClientRpc]
    private void TrapPlayerClientRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out NetworkObject playerObj)) return;
        PlayerMovementController m = playerObj.GetComponent<PlayerMovementController>();
        if (m != null && m.IsLocalPlayer)
        {
            m.SetCanMove(false);
            playerObj.GetComponent<PlayerAnimationController>()?.SetBearTrapStuck(true);
        }
    }

    /// <summary>
    /// Received on all clients after the trap duration expires. Restores the
    /// local player's movement controller if they were the one caught.
    /// </summary>
    [ClientRpc]
    private void ReleasePlayerClientRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out NetworkObject playerObj)) return;
        PlayerMovementController m = playerObj.GetComponent<PlayerMovementController>();
        if (m != null && m.IsLocalPlayer)
        {
            m.SetCanMove(true);
            playerObj.GetComponent<PlayerAnimationController>()?.SetBearTrapStuck(false);
        }
    }

    [ClientRpc]
    private void PlaySnapEffectsClientRpc()
    {
        if (_trapAnimator != null && !string.IsNullOrEmpty(_bearTrapSnapTrigger))
            _trapAnimator.SetTrigger(_bearTrapSnapTrigger);
        if (_audioSource != null && _snapSound != null)
            _audioSource.PlayOneShot(_snapSound);
    }

    [ClientRpc]
    private void PlaySetSoundClientRpc()
    {
        if (_audioSource != null && _setSound != null)
            _audioSource.PlayOneShot(_setSound);
    }

    /// <summary>
    /// Toggles <see cref="PickableObject.CanPickUpManually"/> on all clients so
    /// the trap cannot be picked up while a victim is held in it.
    /// </summary>
    [ClientRpc]
    private void SetPickupAllowedClientRpc(bool allowed)
    {
        CanPickUpManually = allowed;
    }
    // Visuals / State

    private void OnArmedStateChanged(bool previous, bool current)
    {
        if (current)
            PopulateIgnoreSet();
        else
            _ignoredOnArm.Clear();

        ApplyArmedVisuals(current);
    }

    /// <summary>
    /// Uses <see cref="Physics.OverlapBox"/> on each trigger zone - while the
    /// colliders are still disabled - to snapshot who is already inside the volume.
    /// Those colliders are added to <see cref="_ignoredOnArm"/> so they cannot
    /// instantly trigger the trap just by being present when it arms.
    /// </summary>
    private void PopulateIgnoreSet()
    {
        _ignoredOnArm.Clear();
        OverlapIntoIgnoreSet(PlayerCollider as BoxCollider, isPlayer: true);
        OverlapIntoIgnoreSet(EnemyCollider as BoxCollider, isPlayer: false);
    }

    private void OverlapIntoIgnoreSet(BoxCollider box, bool isPlayer)
    {
        if (box == null) return;

        Transform t = box.transform;
        Vector3 worldCenter = t.TransformPoint(box.center);
        Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, t.lossyScale);

        Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, t.rotation);
        foreach (Collider c in hits)
        {
            bool relevant = isPlayer
                ? c.GetComponentInParent<PlayerMovementController>() != null
                : c.GetComponentInParent<MutantEnemy>() != null;
            if (relevant) _ignoredOnArm.Add(c);
        }
    }

    /// <summary>
    /// Syncs the trigger colliders, animator bool, and reticle interact text to
    /// match the current armed state. Runs on all clients via the NetworkVariable callback.
    /// </summary>
    private void ApplyArmedVisuals(bool armed)
    {
        if (_trapAnimator != null)
            _trapAnimator.SetBool(_bearTrapSetBool, armed);

        if (PlayerCollider != null) PlayerCollider.enabled = armed;
        if (EnemyCollider != null) EnemyCollider.enabled = armed;

        interactText = armed ? ArmedInteractText : UnarmedInteractText;
    }
}
