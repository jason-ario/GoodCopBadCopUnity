using System.Collections;
using UnityEngine;

public class Shovel : PickableObject
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [Header("Enemy Hit")]
    [Tooltip("MeleeWeaponHitbox on the 'AttackPoint' child. Performs the server-authoritative enemy scan.")]
    [SerializeField] private MeleeWeaponHitbox _hitbox;

    [Tooltip("Damage dealt to an enemy per successful hit.")]
    [SerializeField] private float _damagePerHit = 25f;

    [Header("Swing Settings")]
    [Tooltip("Seconds after the swing starts before hit-detection fires. Match to your animation.")]
    [SerializeField] private float _hitDelay = 0.5f;

    [Tooltip("Minimum total seconds per swing (cooldown resets after this window elapses from swing start).")]
    [SerializeField] private float _swingCooldown = 0.9f;

    [Tooltip("If the player attacks within this many seconds before the cooldown ends, the attack is buffered and fires immediately when the cooldown expires.")]
    [SerializeField] private float _inputBuffer = 0.2f;

    [Header("Animation")]
    [Tooltip("Animator bool that drives the swing animation.")]
    [SerializeField] private string _swingAnimBool = "UsingTool";

    [Header("Audio")]
    [Tooltip("Played when the swing animation starts.")]
    [SerializeField] private AudioClip _swingSound;

    [Tooltip("Played when the hit scan connects with an enemy.")]
    [SerializeField] private AudioClip _impactSound;

    [Tooltip("Played when the swing connects with geometry but not an enemy.")]
    [SerializeField] private AudioClip _environmentHitSound;

    // ── Internal ───────────────────────────────────────────────────────────────

    private bool _isAttacking;
    private bool _bufferedAttack;
    private float _attackEndTime;
    private MeleeWeaponDurability _durability;

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
            Debug.LogWarning("[Shovel] No MeleeWeaponHitbox assigned — enemy damage will not work.", this);
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

    /// <summary>Starts the attack sequence when the player uses the shovel.</summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        if (_isAttacking)
        {
            // Buffer the input if we're within the buffer window of the cooldown ending.
            if (_attackEndTime - Time.time <= _inputBuffer)
                _bufferedAttack = true;

            return;
        }

        StartCoroutine(AttackRoutine());
    }

    // ── Private ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the swing animation and sound, waits for the hit frame, then triggers
    /// the authoritative hit scan on the server. Respects the full swing cooldown
    /// before allowing another attack.
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

        // Bail if the player dropped the shovel during the windup.
        if (playerPickupController != null && IsHeld)
            _hitbox?.PerformHitScan(_damagePerHit);

        float remainingCooldown = Mathf.Max(0f, _swingCooldown - _hitDelay);
        yield return new WaitForSeconds(remainingCooldown);

        if (!string.IsNullOrEmpty(_swingAnimBool) && playerPickupController != null)
            playerPickupController.PlayerAnimationController.SetAnimBool(_swingAnimBool, false);

        _isAttacking = false;

        if (_bufferedAttack)
        {
            _bufferedAttack = false;
            StartCoroutine(AttackRoutine());
        }
    }

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
    /// Drops the shovel before despawning so the player's hands are cleared correctly,
    /// then asks the server to despawn the NetworkObject.
    /// </summary>
    private void OnDurabilityDepleted()
    {
        playerPickupController?.DropObject();
        DespawnServerRpc();
    }
}
