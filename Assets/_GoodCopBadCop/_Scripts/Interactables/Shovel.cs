using System.Collections;
using UnityEngine;

public class Shovel : PickableObject
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [SerializeField] private MeleeWeaponHitbox hitbox;

    [Tooltip("Seconds after the swing starts until the hitbox sweep fires.")]
    [SerializeField] private float hitDelay = 0.5f;

    [Tooltip("Damage dealt to an enemy per successful hit.")]
    [SerializeField] private float damagePerHit = 25f;

    [Tooltip("Played when the swing animation starts.")]
    [SerializeField] private AudioClip swingSound;

    [Tooltip("Played when the hit scan connects with an enemy.")]
    [SerializeField] private AudioClip impactSound;

    [Tooltip("Played when the swing connects with geometry but not an enemy.")]
    [SerializeField] private AudioClip environmentHitSound;

    // ── Internal ───────────────────────────────────────────────────────────────

    private bool _isAttacking;
    private MeleeWeaponDurability _durability;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        _durability = GetComponent<MeleeWeaponDurability>();

        if (hitbox != null)
        {
            hitbox.OnHit += OnHit;
            hitbox.OnEnvironmentHit += OnEnvironmentHit;
        }

        if (_durability != null)
            _durability.OnDepleted += OnDurabilityDepleted;
    }

    private void OnDestroy()
    {
        if (hitbox != null)
        {
            hitbox.OnHit -= OnHit;
            hitbox.OnEnvironmentHit -= OnEnvironmentHit;
        }

        if (_durability != null)
            _durability.OnDepleted -= OnDurabilityDepleted;
    }

    // ── PickableObject overrides ───────────────────────────────────────────────

    /// <summary>Starts the attack sequence when the player uses the shovel.</summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_isAttacking)
            return;

        if (hitbox == null)
        {
            Debug.LogWarning("[Shovel] No MeleeWeaponHitbox assigned.", this);
            return;
        }

        StartCoroutine(AttackRoutine());
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the swing animation, fires the swing sound, waits for the hit frame,
    /// then triggers the authoritative hit scan on the server.
    /// </summary>
    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        SFXController.Instance.Play(swingSound);

        yield return new WaitForSeconds(hitDelay);

        hitbox.PerformHitScan(damagePerHit);

        yield return new WaitForSeconds(0.5f);
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        _isAttacking = false;
    }

    private void OnHit()
    {
        SFXController.Instance.Play(impactSound);
        _durability?.RegisterHit();
    }

    private void OnEnvironmentHit()
    {
        SFXController.Instance.Play(environmentHitSound);
        _durability?.RegisterHit();
    }

    /// <summary>
    /// Called on the owning client when durability reaches zero.
    /// Drops the shovel before despawning so the player's hands are cleared correctly,
    /// then asks the server to despawn the NetworkObject.
    /// </summary>
    private void OnDurabilityDepleted()
    {
        // Force the player to drop the shovel first so pickup state is cleaned up.
        if (playerPickupController != null)
            playerPickupController.DropObject();

        DespawnServerRpc();
    }
}
