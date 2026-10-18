using System.Collections;
using UnityEngine;

/// <summary>
/// Melee shovel. All swing, hit, audio and durability behaviour lives in <see cref="MeleeWeaponPickable"/>.
/// Kept as its own type because scene/day code references it directly (e.g. <c>Day_01</c> breach shovels).
/// </summary>
public class Shovel : MeleeWeaponPickable
{
}

/// <summary>
/// Shared swing pipeline for held melee weapons (<see cref="Shovel"/>, <see cref="HammerPickable"/>).
///
/// Hits are locked to the swing animation:
///   1. A click fires the edge-triggered "MeleeSwing" animator trigger (owner-local + RPC to all
///      clients via <see cref="PlayerAnimationController.SetAnimTrigger"/>), so exactly one swing
///      animation plays per swing on every machine.
///   2. The hit timer only starts once the arms animator has actually entered the swing state
///      (tagged "MeleeSwing" in Player.controller). If the swing never starts, no hit happens.
///   3. The next swing is only allowed once the cooldown has elapsed AND the swing animation has
///      fully blended back to the hold pose. Clicks after the hit frame are buffered and fire then.
///
/// The swing used to be driven by the level-held "UsingTool" bool with a cooldown shorter than
/// the clip. The bool stayed true while the animator returned to the hold state, which re-entered
/// the swing on its own clip timing, and a buffered swing flipped the bool false→true in one
/// frame, which the animator never saw. Hits then ran on a timer unrelated to what was on screen.
///
/// Fields are serialized under their original Shovel/Hammer names so existing prefab values carry over.
/// </summary>
public abstract class MeleeWeaponPickable : PickableObject
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [Header("Enemy Hit")]
    [Tooltip("MeleeWeaponHitbox on the 'AttackPoint' child. Resolves the hit on the swinging client.")]
    [SerializeField] private MeleeWeaponHitbox _hitbox;

    [Tooltip("Damage dealt to an enemy per successful hit.")]
    [SerializeField] private float _damagePerHit = 25f;

    [Header("Swing Settings")]
    [Tooltip("Seconds after the swing animation starts before hit detection fires. Match to the impact frame.")]
    [SerializeField] private float _hitDelay = 0.4f;

    [Tooltip("Minimum seconds between swing starts. The next swing also waits for the swing animation to finish blending back to the hold pose.")]
    [SerializeField] private float _swingCooldown = 0.9f;

    [Tooltip("Clicks within this many seconds of the cooldown ending (and after the hit frame) are buffered and fire as soon as the next swing is allowed.")]
    [SerializeField] private float _inputBuffer = 0.2f;

    [Header("Animation")]
    [Tooltip("Animator trigger that starts the swing (Holding Shovel/Hammer → Attacking in Player.controller).")]
    [SerializeField] private string _swingAnimTrigger = "MeleeSwing";

    [Tooltip("Animator state tag on the swing states. Gates hit timing and the next swing on the real animation.")]
    [SerializeField] private string _swingStateTag = "MeleeSwing";

    [Tooltip("Animator bool held true for the whole swing. It does NOT start the swing; it blocks the " +
             "AnyState → Holding Shovel/Hammer transitions (condition: hold bool && !UsingTool), which " +
             "would otherwise cut the swing off the moment it starts.")]
    [SerializeField] private string _swingGuardBool = "UsingTool";

    [Header("Audio")]
    [Tooltip("Played when the swing animation starts.")]
    [SerializeField] private AudioClip _swingSound;

    [Tooltip("Played when the hit connects with a live target.")]
    [SerializeField] private AudioClip _impactSound;

    [Tooltip("Played when the swing connects with geometry or a prop.")]
    [SerializeField] private AudioClip _environmentHitSound;

    [Tooltip("Audible range (metres) of the impact sound heard by other players as a world sound.")]
    [SerializeField] private float _worldSoundMaxDistance = 20f;

    // ── Internal ───────────────────────────────────────────────────────────────

    /// <summary>Max wait for the animator to enter the swing state after the trigger fires.</summary>
    private const float SwingStartTimeout = 0.3f;

    /// <summary>Safety cap on waiting for the swing animation to finish, so a stuck animator can't lock the weapon.</summary>
    private const float SwingEndTimeout = 2.5f;

    /// <summary>Extra grace before a lingering swing latch (coroutine killed by deactivation) is treated as stale.</summary>
    private const float StaleSwingGrace = SwingStartTimeout + SwingEndTimeout + 1f;

    private bool _isAttacking;
    private bool _hitResolved;
    private bool _bufferedAttack;
    private float _swingStartTime;
    private float _attackEndTime;
    private Coroutine _attackRoutine;
    private MeleeWeaponDurability _durability;
    private int _swingStateTagHash;

    protected MeleeWeaponHitbox Hitbox => _hitbox;

    private PlayerAnimationController AnimController =>
        playerPickupController != null ? playerPickupController.PlayerAnimationController : null;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        _durability = GetComponent<MeleeWeaponDurability>();
        _swingStateTagHash = Animator.StringToHash(_swingStateTag);

        if (_hitbox != null)
        {
            _hitbox.OnHit            += OnEnemyHit;
            _hitbox.OnEnvironmentHit += OnEnvironmentHit;
            _hitbox.OnRemoteImpact   += OnRemoteImpact;
        }
        else
        {
            Debug.LogWarning($"[{GetType().Name}] No MeleeWeaponHitbox assigned — enemy damage will not work.", this);
        }

        if (_durability != null)
            _durability.OnDepleted += OnDurabilityDepleted;
    }

    public override void OnDestroy()
    {
        if (_hitbox != null)
        {
            _hitbox.OnHit            -= OnEnemyHit;
            _hitbox.OnEnvironmentHit -= OnEnvironmentHit;
            _hitbox.OnRemoteImpact   -= OnRemoteImpact;
        }

        if (_durability != null)
            _durability.OnDepleted -= OnDurabilityDepleted;

        base.OnDestroy();
    }

    // ── PickableObject overrides ───────────────────────────────────────────────

    /// <summary>LMB swings the weapon.</summary>
    public override string GetHeldUseVerb() => "Swing";

    /// <summary>Owner-only (called by <see cref="PlayerPickupController"/>): starts or buffers a swing.</summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        // Failsafe: the coroutine that clears _isAttacking dies without finishing if this object is
        // deactivated mid-swing (stow, day transition, despawn of a parent). Recover from a stale latch.
        if (_isAttacking && Time.time > _swingStartTime + _swingCooldown + StaleSwingGrace)
        {
            Debug.LogWarning($"[{GetType().Name}] Recovering from a stale swing state (attack coroutine was interrupted).", this);
            ClearSwingState();
        }

        if (_isAttacking)
        {
            // Only queue a follow-up once this swing's hit has resolved and we're near its end,
            // so spam can't stack swings ahead of the animation.
            if (_hitResolved && _attackEndTime - Time.time <= _inputBuffer)
                _bufferedAttack = true;

            return;
        }

        _attackRoutine = StartCoroutine(AttackRoutine());
    }

    /// <summary>
    /// Failsafe hook from <see cref="PickableObject"/>: wipes the swing latches and any pending
    /// swing trigger so an interrupted swing can never leave the weapon unusable.
    /// </summary>
    public override void ForceClearUseState()
    {
        base.ForceClearUseState();
        ClearSwingState();
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        // Before base: base clears hold-pose bools, and we still need the controller to reset the trigger.
        ClearSwingState();
        base.OnUnequip(player);
    }

    // ── Swing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs at the hit frame while the weapon is still held. Default: the hitbox scan.
    /// Override to add weapon-specific effects (e.g. hammer fence repair) and call base.
    /// </summary>
    protected virtual void PerformSwingHit()
    {
        _hitbox?.PerformHitScan(_damagePerHit);
    }

    private IEnumerator AttackRoutine()
    {
        _isAttacking    = true;
        _hitResolved    = false;
        _bufferedAttack = false;
        _swingStartTime = Time.time;
        _attackEndTime  = _swingStartTime + _swingCooldown;

        PlayerAnimationController anim = AnimController;
        bool animLocked = anim != null && anim.CanQueryArmsState && !string.IsNullOrEmpty(_swingAnimTrigger);

        if (anim != null && !string.IsNullOrEmpty(_swingAnimTrigger))
        {
            // Guard first, same frame as the trigger, so the AnyState hold-pose transition can't
            // pull the arms out of the swing (locally or on remote clients — RPCs arrive in order).
            SetSwingGuard(anim, true);
            anim.SetAnimTrigger(_swingAnimTrigger);
        }

        // Wait for the swing animation to actually start. The hit is timed from here, so it lands
        // on the animation's impact frame rather than on an independent timer.
        if (animLocked)
        {
            float waited = 0f;
            while (!anim.IsArmsInTaggedState(_swingStateTagHash))
            {
                waited += Time.deltaTime;
                if (waited >= SwingStartTimeout || !IsHeld)
                {
                    // The animator never picked up the swing (not in the hold pose, item dropped, ...).
                    // No animation → no hit. Clear the pending trigger so it can't fire a phantom swing later.
                    anim.ResetAnimTrigger(_swingAnimTrigger);
                    FinishSwing(allowBuffered: false);
                    yield break;
                }
                yield return null;
            }

            _swingStartTime = Time.time;
            _attackEndTime  = _swingStartTime + _swingCooldown;
        }

        if (_swingSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_swingSound);

        yield return new WaitForSeconds(_hitDelay);

        // Bail if the player dropped the weapon during the windup.
        if (playerPickupController != null && IsHeld)
            PerformSwingHit();

        _hitResolved = true;

        // Next swing waits for the cooldown AND for the swing animation to blend back to the hold pose.
        float safetyDeadline = Time.time + SwingEndTimeout;
        while (Time.time < _attackEndTime
               || (animLocked && anim != null && anim.IsArmsInTaggedState(_swingStateTagHash) && Time.time < safetyDeadline))
        {
            yield return null;
        }

        FinishSwing(allowBuffered: true);
    }

    private void FinishSwing(bool allowBuffered)
    {
        bool buffered = allowBuffered && _bufferedAttack && IsHeld && playerPickupController != null;

        _isAttacking    = false;
        _hitResolved    = false;
        _bufferedAttack = false;
        _attackRoutine  = null;

        // A buffered swing re-sets the guard in the same frame, so the animator never sees it drop.
        SetSwingGuard(AnimController, false);

        if (buffered)
            _attackRoutine = StartCoroutine(AttackRoutine());
    }

    private void ClearSwingState()
    {
        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
            _attackRoutine = null;
        }

        _isAttacking    = false;
        _hitResolved    = false;
        _bufferedAttack = false;
        _attackEndTime  = 0f;

        PlayerAnimationController anim = AnimController;
        if (anim != null && !string.IsNullOrEmpty(_swingAnimTrigger))
            anim.ResetAnimTrigger(_swingAnimTrigger);

        SetSwingGuard(anim, false);
    }

    private void SetSwingGuard(PlayerAnimationController anim, bool value)
    {
        if (anim != null && !string.IsNullOrEmpty(_swingGuardBool))
            anim.SetAnimBool(_swingGuardBool, value);
    }

    // ── Hitbox callbacks ───────────────────────────────────────────────────────

    private void OnEnemyHit()
    {
        if (_impactSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_impactSound);

        _durability?.RegisterHit();
    }

    private void OnEnvironmentHit()
    {
        if (_environmentHitSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_environmentHitSound);

        _durability?.RegisterHit();
    }

    /// <summary>Another player's swing connected: play the impact as a spatial world sound.</summary>
    private void OnRemoteImpact(bool isEnvironment, Vector3 point)
    {
        AudioClip clip = isEnvironment ? _environmentHitSound : _impactSound;
        if (clip != null && SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(clip, point, 1f, 1f, _worldSoundMaxDistance);
    }

    // ── Durability ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called on the owning client when durability reaches zero. Drops the weapon first so the
    /// player's hands are cleared correctly, then asks the server to despawn it.
    /// </summary>
    private void OnDurabilityDepleted()
    {
        playerPickupController?.DropObject();
        DespawnServerRpc();
    }
}
