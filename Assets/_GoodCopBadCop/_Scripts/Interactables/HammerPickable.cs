using System.Collections;
using UnityEngine;

/// <summary>
/// A hammer the player can pick up and swing to:
///   - Repair broken PerimiterFence segments (owner-side OverlapSphere, fence ServerRpc).
///   - Damage mutant enemies (server-authoritative via MeleeWeaponHitbox).
///
/// Mirrors the Shovel pattern. Attach MeleeWeaponHitbox to a child "AttackPoint" transform
/// and assign it here. Optionally attach MeleeWeaponDurability alongside this component
/// to give the hammer a finite lifespan.
///
/// Prefab setup:
///   - Root: NetworkObject + HammerPickable + MeleeWeaponDurability (optional) + ParentConstraint
///   - Child "AttackPoint": MeleeWeaponHitbox (NetworkBehaviour)
///   - Assign a PickableItemData ScriptableObject.
/// </summary>
public class HammerPickable : PickableObject
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [Header("Enemy Hit")]
    [Tooltip("MeleeWeaponHitbox on the 'AttackPoint' child. Performs the server-authoritative enemy scan.")]
    [SerializeField] private MeleeWeaponHitbox _hitbox;

    [Tooltip("Damage dealt to an enemy per successful hit.")]
    [SerializeField] private float _damagePerHit = 25f;

    [Header("Swing Settings")]
    [Tooltip("Seconds after the swing starts before hit-detection fires. Match to your animation.")]
    [SerializeField] private float _hitDelay = 0.35f;

    [Tooltip("Minimum total seconds per swing (cooldown resets after this window elapses from swing start).")]
    [SerializeField] private float _swingCooldown = 0.9f;

    [Tooltip("If the player attacks within this many seconds before the cooldown ends, the attack is buffered and fires immediately when the cooldown expires.")]
    [SerializeField] private float _inputBuffer = 0.2f;

    [Tooltip("Radius of the owner-side OverlapSphere used to detect nearby fence segments.")]
    [SerializeField] private float _fenceHitRadius = 1.5f;

    [Tooltip("Layer mask for the fence OverlapSphere. Restrict to your fence layer for best performance.")]
    [SerializeField] private LayerMask _fenceLayerMask = ~0;

    [Header("Animation")]
    [Tooltip("Animator bool that drives the swing animation. Mirrors the shovel 'UsingTool' slot.")]
    [SerializeField] private string _swingAnimBool = "UsingTool";

    [Header("Audio")]
    [SerializeField] private AudioClip _swingSound;
    [SerializeField] private AudioClip _impactSound;
    [SerializeField] private AudioClip _environmentHitSound;

    // ── Internal ───────────────────────────────────────────────────────────────

    private bool _isAttacking;
    private bool _bufferedAttack;
    private float _attackEndTime;
    private Coroutine _attackRoutine;
    private MeleeWeaponDurability _durability;

    /// <summary>Extra grace on top of the swing cooldown before a lingering swing is treated as stale.</summary>
    private const float STALE_SWING_GRACE = 1f;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        _durability = GetComponent<MeleeWeaponDurability>();

        if (_hitbox != null)
        {
            _hitbox.OnHit            += OnEnemyHit;
            _hitbox.OnEnvironmentHit += OnEnvironmentHit;
        }
        else
        {
            Debug.LogWarning("[HammerPickable] No MeleeWeaponHitbox assigned — enemy damage will not work.", this);
        }

        if (_durability != null)
            _durability.OnDepleted += OnDurabilityDepleted;
    }

    private void OnDestroy()
    {
        if (_hitbox != null)
        {
            _hitbox.OnHit            -= OnEnemyHit;
            _hitbox.OnEnvironmentHit -= OnEnvironmentHit;
        }

        if (_durability != null)
            _durability.OnDepleted -= OnDurabilityDepleted;
    }

    // ── PickableObject overrides ───────────────────────────────────────────────

    /// <summary>
    /// Fires when the player left-clicks while holding the hammer.
    /// Starts the attack coroutine if not already mid-swing, or buffers the
    /// input if pressed within the buffer window before the cooldown ends.
    /// Only runs on the owning client.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        // Failsafe: AttackRoutine clears _isAttacking at its end, but the coroutine is destroyed
        // without finishing if this GameObject is deactivated mid-swing (stow to inventory, day
        // transition, despawn). The latch then blocked every later swing until the hammer was
        // dropped and re-picked-up. Treat an expired swing window as stale and recover.
        if (_isAttacking && Time.time > _attackEndTime + STALE_SWING_GRACE)
        {
            Debug.LogWarning("[HammerPickable] Recovering from a stale swing state (attack coroutine was interrupted).", this);
            ClearSwingState();
        }

        if (_isAttacking)
        {
            // Buffer the input if we're within the buffer window of the cooldown ending.
            if (_attackEndTime - Time.time <= _inputBuffer)
                _bufferedAttack = true;

            return;
        }

        _attackRoutine = StartCoroutine(AttackRoutine());
    }

    /// <summary>
    /// Failsafe hook from <see cref="PickableObject"/>: wipes the swing latches and the swing
    /// animator bool so an interrupted swing can never leave the hammer permanently unusable.
    /// </summary>
    public override void ForceClearUseState()
    {
        base.ForceClearUseState();
        ClearSwingState();
    }

    private void ClearSwingState()
    {
        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
            _attackRoutine = null;
        }

        _isAttacking = false;
        _bufferedAttack = false;
        _attackEndTime = 0f;

        if (!string.IsNullOrEmpty(_swingAnimBool) && playerPickupController != null)
            playerPickupController.PlayerAnimationController.SetAnimBool(_swingAnimBool, false);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the swing animation and sound, waits for the hit frame, then simultaneously:
    ///   1. Checks for a broken PerimiterFence in range (owner-side).
    ///   2. Runs the server-authoritative enemy hit scan via MeleeWeaponHitbox.
    /// </summary>
    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _bufferedAttack = false;
        _attackEndTime = Time.time + _swingCooldown;

        if (!string.IsNullOrEmpty(_swingAnimBool) && playerPickupController != null)
            playerPickupController.PlayerAnimationController.SetAnimBool(_swingAnimBool, true);

        if (_swingSound != null)
            SFXController.Instance.Play(_swingSound);

        yield return new WaitForSeconds(_hitDelay);

        // Bail if the player dropped the hammer during the windup.
        if (playerPickupController != null && IsHeld)
        {
            Vector3 origin = playerPickupController.holdPoint != null
                ? playerPickupController.holdPoint.position
                : transform.position;

            // Owner-side fence repair detection.
            TryHitFence(origin);

            // Server-authoritative enemy damage.
            _hitbox?.PerformHitScan(_damagePerHit);
        }

        float remainingCooldown = Mathf.Max(0f, _swingCooldown - _hitDelay);
        yield return new WaitForSeconds(remainingCooldown);

        if (!string.IsNullOrEmpty(_swingAnimBool) && playerPickupController != null)
            playerPickupController.PlayerAnimationController.SetAnimBool(_swingAnimBool, false);

        _isAttacking = false;
        _attackRoutine = null;

        if (_bufferedAttack)
        {
            _bufferedAttack = false;
            _attackRoutine = StartCoroutine(AttackRoutine());
        }
    }

    /// <summary>
    /// Owner-side OverlapSphere that finds the nearest PerimiterFence and sends a single repair
    /// hit via ServerRpc. Does not interact with enemies.
    ///
    /// Two deliberate details:
    ///   - It picks the <em>nearest</em> segment rather than whichever collider the physics query
    ///     happened to return first, so a swing always lands on the fence the player is standing at.
    ///   - It never refuses to send the RPC based on this client's own view of the fence's health.
    ///     The old code skipped any fence whose replicated <c>IsBroken</c> read false locally,
    ///     which meant a client with a stale/unsynchronised health value simply could not repair
    ///     a fence the host could. The server re-validates in <c>HitWithHammerServerRpc</c> and
    ///     ignores hits on intact segments, so sending unconditionally is free and authoritative.
    /// </summary>
    private void TryHitFence(Vector3 origin)
    {
        Collider[] hits = Physics.OverlapSphere(origin, _fenceHitRadius, _fenceLayerMask);

        PerimiterFence nearest = null;
        PerimiterFence nearestBroken = null;
        float nearestSqr = float.MaxValue;
        float nearestBrokenSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            PerimiterFence fence = col.GetComponent<PerimiterFence>()
                                ?? col.GetComponentInParent<PerimiterFence>();

            if (fence == null || !fence.IsSpawned) continue;

            float sqr = (col.ClosestPoint(origin) - origin).sqrMagnitude;

            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = fence;
            }

            if (fence.IsBroken && sqr < nearestBrokenSqr)
            {
                nearestBrokenSqr = sqr;
                nearestBroken = fence;
            }
        }

        // Prefer a segment this client already knows is damaged; otherwise let the server decide.
        PerimiterFence target = nearestBroken ?? nearest;
        target?.HitWithHammerServerRpc();
    }

    // ── Hitbox callbacks ───────────────────────────────────────────────────────

    private void OnEnemyHit()
    {
        if (_impactSound != null)
            SFXController.Instance.Play(_impactSound);

        _durability?.RegisterHit();
    }

    private void OnEnvironmentHit()
    {
        if (_environmentHitSound != null)
            SFXController.Instance.Play(_environmentHitSound);

        _durability?.RegisterHit();
    }

    // ── Durability ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the owning client when durability reaches zero.
    /// Drops the hammer first so pickup state is cleaned up, then despawns it.
    /// </summary>
    private void OnDurabilityDepleted()
    {
        playerPickupController?.DropObject();
        DespawnServerRpc();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform origin = playerPickupController != null && playerPickupController.holdPoint != null
            ? playerPickupController.holdPoint
            : transform;

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawSphere(origin.position, _fenceHitRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(origin.position, _fenceHitRadius);
    }
#endif
}
