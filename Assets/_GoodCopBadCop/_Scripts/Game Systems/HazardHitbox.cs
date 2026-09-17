using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Reusable hazard hit-box: while active, damages every player caught inside its bounds once
/// per <see cref="tickInterval"/> — not on enter/exit — so a player standing in it keeps taking
/// damage every tick for as long as they stay inside. Meant to be dropped in as a prefab and
/// reused for any hazard (a vehicle's front/rear bumper, a spinning blade, an electrified fence,
/// etc.) rather than each hazard reimplementing its own hit-scan.
///
/// Uses a periodic <see cref="Physics.OverlapBoxNonAlloc"/> against this GameObject's own
/// <see cref="BoxCollider"/> bounds instead of relying on OnTriggerEnter/OnCollisionEnter — the
/// same technique as DeliveryTruckController's truck hit-scan and MutantAttackHitbox's melee
/// attacks — because that message-based approach isn't guaranteed to fire reliably against the
/// player's CharacterController.
///
/// Toggle with <see cref="SetHazardActive"/> (e.g. turn on while a vehicle is driving, off while
/// parked). Start disabled by default via <see cref="startActive"/> so a freshly-placed hazard
/// does nothing until something explicitly activates it.
///
/// Server-authoritative when nested under a NetworkBehaviour (e.g. as a child of a networked
/// vehicle) — only that owner's server instance runs the tick, so toggling/ticking on remote
/// clients is a harmless no-op. If no NetworkBehaviour owner is found in its parent hierarchy,
/// it just runs locally (e.g. for non-networked scenes).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class HazardHitbox : MonoBehaviour
{

    // Configuration

    [Header("Damage")]
    [Tooltip("Damage dealt to every player inside the box, once per tick.")]
    [SerializeField] private float damage = 40f;
    [Tooltip("How often, in seconds, the hazard checks for and damages overlapping players while active.")]
    [SerializeField] private float tickInterval = 1f;
    [Tooltip("Gameplay effect key recorded for analytics/UI whenever this hazard damages a player.")]
    [SerializeField] private string effectKey = EffectKeys.HazardDamage;
    [Tooltip("Tag used to identify player GameObjects. Must match the Player prefab's tag.")]
    [SerializeField] private string playerTag = "Player";

    [Header("State")]
    [Tooltip("Whether the hazard is already damaging overlapping players as soon as it spawns. Leave off for hazards that are switched on by other code (e.g. a vehicle while driving).")]
    [SerializeField] private bool startActive = false;

    [Header("Knockback")]
    [Tooltip("When enabled, also shoves each hit player back and up — away from this hazard's center — to push them clear of it.")]
    [SerializeField] private bool enableKnockback = false;
    [Tooltip("Horizontal speed applied away from the hazard's center on hit.")]
    [SerializeField] private float knockbackHorizontalForce = 10f;
    [Tooltip("Vertical (upward) speed applied on hit, giving the shove some lift.")]
    [SerializeField] private float knockbackVerticalForce = 6f;


    // Internal

    private static readonly Collider[] OverlapBuffer = new Collider[8];

    private BoxCollider _box;
    private NetworkBehaviour _owner;
    private bool _isActive;
    private float _nextTickTime;

    /// <summary>Whether the hazard is currently damaging overlapping players.</summary>
    public bool IsHazardActive => _isActive;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        _box.isTrigger = true;

        // Optional — if this hazard sits under a networked owner (e.g. a vehicle), only that
        // owner's server instance should actually tick/damage.
        _owner = GetComponentInParent<NetworkBehaviour>();

        _isActive = startActive;
        if (_isActive)
            _nextTickTime = Time.time;
    }

    private void Update()
    {
        if (_owner != null && !_owner.IsServer) return;
        if (!_isActive) return;
        if (Time.time < _nextTickTime) return;

        _nextTickTime = Time.time + tickInterval;
        PerformHazardTick();
    }


    // Public API

    /// <summary>Turns the hazard's periodic damage tick on or off. Safe to call every frame/on any client.</summary>
    public void SetHazardActive(bool isActive)
    {
        if (_isActive == isActive) return;

        _isActive = isActive;
        if (_isActive)
            _nextTickTime = Time.time; // damage immediately on activation instead of waiting a full interval
    }


    // Hit scan

    private void PerformHazardTick()
    {
        Vector3 worldCenter = transform.TransformPoint(_box.center);
        Vector3 halfExtents = Vector3.Scale(_box.size, transform.lossyScale) * 0.5f;

        int hitCount = Physics.OverlapBoxNonAlloc(
            worldCenter,
            halfExtents,
            OverlapBuffer,
            transform.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = OverlapBuffer[i];
            Transform root = col.transform.root;
            if (!root.CompareTag(playerTag))
                continue;

            PlayerHealth playerHealth = root.GetComponentInChildren<PlayerHealth>();
            if (playerHealth == null || playerHealth.IsDead)
                continue;

            Vector3 hitPoint = col.ClosestPoint(worldCenter);
            playerHealth.TakeDamage(damage, effectKey, hitPoint);
            Debug.Log($"[HazardHitbox] '{name}' hit player '{root.name}' for {damage} damage.", this);

            if (enableKnockback)
                ApplyKnockback(root, worldCenter);
        }
    }

    /// <summary>
    /// Shoves <paramref name="playerRoot"/> away (horizontally) from <paramref name="worldCenter"/>
    /// and gives them upward lift, so they're pushed clear of the hazard instead of standing back
    /// in it a moment later. Falls back to this hazard's forward direction if the player is
    /// (near) exactly on top of the center, where the away-from direction would otherwise be zero.
    /// </summary>
    private void ApplyKnockback(Transform playerRoot, Vector3 worldCenter)
    {
        PlayerMovementController movement = playerRoot.GetComponentInChildren<PlayerMovementController>();
        if (movement == null) return;

        Vector3 away = playerRoot.position - worldCenter;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = transform.forward;
        away.Normalize();

        movement.ApplyKnockbackServer(away * knockbackHorizontalForce, knockbackVerticalForce);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        BoxCollider box = _box != null ? _box : GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.color = _isActive ? new Color(1f, 0.1f, 0.1f, 0.6f) : new Color(1f, 0.6f, 0f, 0.35f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
