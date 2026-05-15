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

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (hitbox != null)
        {
            hitbox.OnHit += PlayImpactSound;
            hitbox.OnEnvironmentHit += PlayEnvironmentHitSound;
        }
    }

    private void OnDestroy()
    {
        if (hitbox != null)
        {
            hitbox.OnHit -= PlayImpactSound;
            hitbox.OnEnvironmentHit -= PlayEnvironmentHitSound;
        }
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

    private void PlayImpactSound()
    {
        SFXController.Instance.Play(impactSound);
    }

    private void PlayEnvironmentHitSound()
    {
        SFXController.Instance.Play(environmentHitSound);
    }
}
