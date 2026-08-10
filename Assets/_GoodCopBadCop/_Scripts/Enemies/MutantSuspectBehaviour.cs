using System.Collections;
using DG.Tweening;
using FIMSpace.FProceduralAnimation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative state machine that drives a mutant through the suspect lineup.
/// Handles walk-to-booth, rotation, shutter check, climb-through or bang-and-retreat.
/// Keeps MutantEnemy disabled until a successful breakthrough, then hands off to it.
/// Requires: NetworkObject, NavMeshAgent, Animator, MutantEnemy.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MutantSuspectBehaviour : NetworkBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────────

    [Tooltip("If false, this mutant will never climb through the booth window even when the shutter is open. It will bang on the window frame and retreat instead.")]
    [SerializeField] private bool canClimb = true;

    [Tooltip("How long the climbing animation bool stays true after the climb begins. Set to 0 to turn it off exactly when the movement tween finishes.")]
    [SerializeField] private float _climbAnimHoldSeconds = 0.1f;

    [Header("Sounds")]
    [Tooltip("Played on all clients at the moment the mutant begins climbing through the booth window.")]
    [SerializeField] private AudioClip _climbThroughSound;

    [Tooltip("Short stinger played on all clients the moment the mutant finishes climbing through and lands inside.")]
    [SerializeField] private AudioClip _climbLandSound;

    [Tooltip("If true, the chase music clip will play when this mutant breaks through the window. " +
             "Enable only on suspect characters that transform into a mutant mid-shift.")]
    [SerializeField] private bool _playChaseMusic = false;

    [Header("Chase Music")]
    [Tooltip("Looping music that plays on all clients once the mutant has landed inside and starts chasing. " +
             "Faded out automatically when the mutant flees or dies.")]
    [SerializeField] private AudioClip _chaseMusic;

    [Tooltip("Seconds to fade the chase music in after the mutant lands.")]
    [Min(0f)]
    [SerializeField] private float _chaseMusicFadeInSeconds = 1.5f;

    private MutantIntruderData _data;
    private Transform _standPos;
    private Transform _despawnPos;
    private Transform _climbThroughTargetPos;
    private ShutterController _shutterController;
    private SuspectController _controller;

    // ── Components ─────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Animator _animator;
    private MutantEnemy _mutantEnemy;

    // ── State ──────────────────────────────────────────────────────────────────

    private Tween _activeTween;
    private bool _isDone;

    /// <summary>
    /// Optional callback fired on the server when the lineup sequence completes.
    /// Receives true if the mutant broke through, false if it retreated or despawned.
    /// Set this before calling BeginLineup() when bypassing SuspectController (e.g. AlexeiController).
    /// </summary>
    public System.Action<bool> OnSequenceComplete;

    /// <summary>
    /// When true, the mutant despawns immediately after finishing the shutter-bang sequence
    /// instead of walking back to the despawn point. Default false.
    /// Set this before calling BeginAtStandPos() for scripted entrances (e.g. Alexei cutscene).
    /// </summary>
    public bool DespawnInsteadOfRetreat { get; set; }

    private const float ArrivalPollInterval = 0.1f;
    private const float ArrivalTolerance = 0.25f;
    private const float GiveUpPauseDuration = 0.1f;
    private const string ClimbingAnimBool = "climbing";
    private const string BangOnShuttersAnimBool = "BangOnShutters";
    private const string GroundedAnimBool = "Grounded";

    /// <summary>
    /// Returns true when there is a physical barrier for the mutant to bang against.
    /// Returns false — suppressing the BangOnShutters trigger — when the shutter is open
    /// AND the glass pane is already smashed, meaning nothing is left to hit.
    /// </summary>
    private bool HasBarrierToBangOn()
    {
        bool shutterOpen = _shutterController != null && _shutterController.IsOpen;
        bool glassGone   = BreakableGlassController.Instance == null
                           || BreakableGlassController.Instance.IsSmashed;
        return !shutterOpen || !glassGone;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _mutantEnemy = GetComponent<MutantEnemy>();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _activeTween?.Kill();
        StopAllCoroutines();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins the full mutant-in-lineup sequence.
    /// Must be called on the server only, immediately after the NetworkObject is spawned.
    /// </summary>
    public void BeginLineup(
        MutantIntruderData data,
        Transform standPos,
        Transform despawnPos,
        Transform climbThroughTargetPos,
        ShutterController shutterController,
        SuspectController controller)
    {
        if (!IsServer) return;

        _data = data;
        _standPos = standPos;
        _despawnPos = despawnPos;
        _climbThroughTargetPos = climbThroughTargetPos;
        _shutterController = shutterController;
        _controller = controller;

        SubscribeLineupDeathSafetyNet();

        // Suspend the chase loop before it gets a chance to run (it defers one frame),
        // so MutantSuspectBehaviour has exclusive control during the lineup sequence.
        _mutantEnemy?.SuspendForLineup();

        // MutantEnemy.Update() is the only thing that syncs the networked "Grounded" animator
        // state (from NavMeshAgent.isOnNavMesh), and it no-ops for the whole lineup sequence
        // because SuspendForLineup() stops its coroutines while _isActive is still false and the
        // NavMeshAgent is disabled for the DOTween walk-in. Its NetworkVariable defaults to
        // false, and nothing will ever flip it back to true while those guards hold — so without
        // this the mutant plays a floating/falling pose for the entire walk-in instead of
        // walking. Force it true here since the mutant is standing on solid ground for the whole
        // lineup sequence, not falling.
        SetAnimBool(GroundedAnimBool, true);

        // Zero out procedural leg IK for the whole walk-in — it's driven by transform.DOMove,
        // not real grounded locomotion, and LegsAnimator's foot planting fights that tweened
        // motion. Restored once the mutant either breaks through the window (ClimbThroughSequence)
        // or gives up and starts a genuine NavMeshAgent-driven retreat (RetreatingSequence).
        SetLegsAnimatorsBlendClientRpc(0f);

        StartCoroutine(LineupSequence());
    }

    /// <summary>
    /// Safety net for a mutant killed (or that flees) while still under
    /// MutantSuspectBehaviour's control — i.e. any time before it climbs through and hands
    /// off to MutantEnemy. Without this, killing a mutant mid-walk-in or mid-attack (e.g.
    /// while it's banging on the shutter) would fire MutantEnemy.OnRemovedFromPlay with no
    /// listener attached (ClimbThroughSequence only subscribes its own breakthrough handler
    /// *after* a successful climb-through), so the lineup slot would never be released and
    /// the next-suspect bell/button would stay dead forever.
    /// Unsubscribed once the mutant either breaks through (ClimbThroughSequence takes over
    /// with its own brokeThrough:true handler) or the sequence finishes normally, so it never
    /// double-fires OnMutantIntruderComplete.
    /// </summary>
    private void SubscribeLineupDeathSafetyNet()
    {
        if (_mutantEnemy == null) return;
        _mutantEnemy.OnRemovedFromPlay -= HandleRemovedFromPlayDuringLineup;
        _mutantEnemy.OnRemovedFromPlay += HandleRemovedFromPlayDuringLineup;
    }

    private void UnsubscribeLineupDeathSafetyNet()
    {
        if (_mutantEnemy == null) return;
        _mutantEnemy.OnRemovedFromPlay -= HandleRemovedFromPlayDuringLineup;
    }

    private void HandleRemovedFromPlayDuringLineup()
    {
        // Already handled via a normal completion path (give-up/retreat/despawn) or via the
        // breakthrough-specific handler in ClimbThroughSequence — nothing to do.
        if (_isDone) return;

        _isDone = true;
        _activeTween?.Kill();
        StopAllCoroutines();

        _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
        OnSequenceComplete?.Invoke(false);
    }

    // ── Coroutines ─────────────────────────────────────────────────────────────

    private IEnumerator LineupSequence()
    {
        if (!IsServer || _isDone) yield break;

        // Disable the NavMeshAgent during the DOMove walk-in phase so it doesn't
        // steer or override position — mirrors exactly how SuspectController.InitiateSuspect works.
        _agent.enabled = false;

        // Walk to the booth stand position using DOTween (position only, rotation unchanged).
        SetWalkingClientRpc(true);

        bool walkDone = false;
        _activeTween = transform
            .DOMove(_standPos.position, _data.walkInDurationSeconds)
            .OnComplete(() => walkDone = true);

        yield return new WaitUntil(() => walkDone);

        if (_isDone) yield break;

        // Arrived — stop walking, then rotate to face the booth window.
        SetWalkingClientRpc(false);

        bool rotationDone = false;
        _activeTween = transform
            .DORotateQuaternion(_standPos.rotation, 0.5f)
            .OnComplete(() => rotationDone = true);

        yield return new WaitUntil(() => rotationDone);

        // Brief pause before acting.
        yield return new WaitForSeconds(_data.preAttackPauseSeconds);

        if (_isDone) yield break;

        if (canClimb && _shutterController != null && _shutterController.IsOpen)
        {
            // Glass is exposed (shutter up). If it's already smashed, climb straight through;
            // otherwise attack it until it breaks or we give up.
            var glass = BreakableGlassController.Instance;
            if (glass == null || glass.IsSmashed)
                yield return StartCoroutine(ClimbThroughSequence());
            else
                yield return StartCoroutine(GlassAttackSequence());
        }
        else
            yield return StartCoroutine(ShutterBangSequence());
    }

    /// <summary>Climbs through the open booth window into the player area, then enables MutantEnemy.</summary>
    private IEnumerator ClimbThroughSequence()
    {
        if (!IsServer || _isDone) yield break;

        SetClimbingClientRpc(true);
        PlayClimbThroughSoundClientRpc();

        // Disable procedural leg IK for the duration of the climb — the tweened climb-through
        // motion isn't grounded locomotion, so LegsAnimator would otherwise fight the pose.
        // (Already zero since spawn/BeginLineup — reasserted here in case this mutant entered via
        // BeginAtStandPos after a normal-suspect arrival that had already restored it to full.)
        SetLegsAnimatorsBlendClientRpc(0f);

        // Disable agent so DOTween can move freely across the counter (off-mesh).
        _agent.enabled = false;

        bool moveDone = false;
        _activeTween = transform
            .DOMove(_climbThroughTargetPos.position, _data.climbDurationSeconds)
            .OnComplete(() => moveDone = true);

        // Fire the climbing bool like a trigger — hold briefly then clear so the
        // animation plays exactly once without looping for the full climb duration.
        yield return new WaitForSeconds(_climbAnimHoldSeconds);
        SetClimbingClientRpc(false);

        // Wait for the actual movement tween to finish.
        yield return new WaitUntil(() => moveDone);
        PlayClimbLandSoundClientRpc();
        if (_playChaseMusic)
            StartChaseMusicClientRpc();

        // Climb-through is complete — re-enable leg IK before the mutant resumes normal
        // grounded locomotion under MutantEnemy's control.
        SetLegsAnimatorsBlendClientRpc(1f);

        if (_isDone) yield break;

        // Stop the walking animation — MutantEnemy drives its own Speed parameter from here on.
        SetWalkingClientRpc(false);

        // Re-enable the NavMeshAgent so ChaseLoop can place the agent on the mesh.
        // One frame is required for the agent to link onto the NavMesh surface after
        // being re-enabled at the new position; without it isOnNavMesh returns false.
        _agent.enabled = true;
        yield return null;

        // Enable MutantEnemy on all clients (animation sync) and hand off server control.
        EnableMutantEnemyClientRpc();

        if (_mutantEnemy != null)
        {
            // Aggro on the booth itself once inside.
            if (_controller != null)
                _mutantEnemy.SetAggroTarget(_controller.transform);
            
            _mutantEnemy.SetForceAggro(true);
            _mutantEnemy.InitialiseServer();

            // Swap the lineup death safety net for the breakthrough-specific handler — from
            // here on the mutant is loose in the player area, so removal counts as brokeThrough.
            UnsubscribeLineupDeathSafetyNet();
            _mutantEnemy.OnRemovedFromPlay += () =>
            {
                _controller?.OnMutantIntruderComplete(this, brokeThrough: true);
                OnSequenceComplete?.Invoke(true);
            };
        }

        _isDone = true;

        // Fallback: if there is no MutantEnemy to listen to, notify immediately.
        if (_mutantEnemy == null)
        {
            _controller?.OnMutantIntruderComplete(this, brokeThrough: true);
            OnSequenceComplete?.Invoke(true);
        }
    }

    /// <summary>
    /// Attacks the closed shutter window.
    /// Climbing mutants bang a fixed number of times, climb through opportunistically if the shutter opens, then retreat.
    /// Non-climbing mutants attack for a fixed duration, then lose interest and retreat.
    /// </summary>
    private IEnumerator ShutterBangSequence()
    {
        if (!IsServer || _isDone) yield break;

        if (!canClimb)
        {
            // Release the lineup slot right away — this mutant stays at the window as a persistent threat
            // while the rest of the shift continues normally.
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false, staysAtWindow: true);

            // The slot is released for good at this point — unsubscribe the lineup death safety
            // net so a later kill/despawn doesn't call OnMutantIntruderComplete a second time
            // (which would try to despawn this mutant and re-arm the bell again).
            UnsubscribeLineupDeathSafetyNet();

            // Attack the window until the loses-interest timer expires.
            float endTime = Time.time + _data.losesInterestAfterSeconds;
            while (!_isDone && Time.time < endTime)
            {
                if (HasBarrierToBangOn()) SetAttackClientRpc(true);
                HitShutterClientRpc();
                yield return new WaitForSeconds(_data.attackAnimDurationSeconds);
                SetAttackClientRpc(false);
                yield return new WaitForSeconds(Mathf.Max(0f, _data.bangIntervalSeconds - _data.attackAnimDurationSeconds));
            }

            if (_isDone) yield break;

            // Lost interest — either despawn or retreat.

            if (DespawnInsteadOfRetreat)
            {
                _isDone = true;
                OnSequenceComplete?.Invoke(false);
                if (NetworkObject.IsSpawned) NetworkObject.Despawn();
                yield break;
            }

            yield return StartCoroutine(RetreatingSequence(notifyController: false));
            yield break;
        }

        // Climbing mutant: bang a fixed number of times, checking each cycle for an opened shutter.
        for (int i = 0; i < _data.shutterBangCount; i++)
        {
            if (_isDone) yield break;

            // Shutter opened mid-bang — break through if the glass is already gone/smashed,
            // otherwise the glass is still intact behind it so switch to attacking that instead
            // of climbing straight through it.
            if (_shutterController != null && _shutterController.IsOpen)
            {
                var glass = BreakableGlassController.Instance;
                if (glass == null || glass.IsSmashed)
                    yield return StartCoroutine(ClimbThroughSequence());
                else
                    yield return StartCoroutine(GlassAttackSequence());
                yield break;
            }

            if (HasBarrierToBangOn()) SetAttackClientRpc(true);
            HitShutterClientRpc();
            yield return new WaitForSeconds(_data.attackAnimDurationSeconds);
            SetAttackClientRpc(false);
            yield return new WaitForSeconds(Mathf.Max(0f, _data.bangIntervalSeconds - _data.attackAnimDurationSeconds));
        }

        if (_isDone) yield break;

        // Give up: brief pause, then either despawn or retreat.
        yield return new WaitForSeconds(GiveUpPauseDuration);

        if (_isDone) yield break;

        if (DespawnInsteadOfRetreat)
        {
            _isDone = true;
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
            OnSequenceComplete?.Invoke(false);
            if (NetworkObject.IsSpawned) NetworkObject.Despawn();
            yield break;
        }

        yield return StartCoroutine(RetreatingSequence(notifyController: true));
    }

    /// <summary>
    /// Attacks the exposed glass when the shutter is open.
    /// Each bang deals one hit to <see cref="BreakableGlassController"/>. If the glass shatters,
    /// the mutant immediately transitions to <see cref="ClimbThroughSequence"/>. If the glass
    /// survives all bangs or the shutter closes during the attack, the mutant retreats.
    /// Damage accumulates across visits — the glass health persists until
    /// <see cref="BreakableGlassController.ResetGlass"/> is called.
    /// </summary>
    private IEnumerator GlassAttackSequence()
    {
        if (!IsServer || _isDone) yield break;

        var glass = BreakableGlassController.Instance;

        for (int i = 0; i < _data.shutterBangCount; i++)
        {
            if (_isDone) yield break;

            // Player closed the shutter mid-attack — glass is now protected.
            if (_shutterController != null && !_shutterController.IsOpen)
                break;

            SetAttackClientRpc(true);

            // Wait for the animation to reach the impact point, then register the hit.
            yield return new WaitForSeconds(0.5f);

            if (glass != null)
            {
                int newHits = glass.RegisterHit();

                if (glass.IsSmashed)
                {
                    // Final blow — transition to broken glass on all clients, then climb through.
                    SmashGlassClientRpc();
                    yield return new WaitForSeconds(Mathf.Max(0f, _data.attackAnimDurationSeconds - 0.5f));
                    SetAttackClientRpc(false);
                    yield return StartCoroutine(ClimbThroughSequence());
                    yield break;
                }
                else
                {
                    // Intermediate hit — update crack visual on all clients.
                    UpdateGlassClientRpc(newHits);
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0f, _data.attackAnimDurationSeconds - 0.5f));
            SetAttackClientRpc(false);
            yield return new WaitForSeconds(Mathf.Max(0f, _data.bangIntervalSeconds - _data.attackAnimDurationSeconds));
        }

        if (_isDone) yield break;

        // Exhausted attack budget without breaking the glass — give up and retreat.
        yield return new WaitForSeconds(GiveUpPauseDuration);

        if (_isDone) yield break;

        if (DespawnInsteadOfRetreat)
        {
            _isDone = true;
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
            OnSequenceComplete?.Invoke(false);
            if (NetworkObject.IsSpawned) NetworkObject.Despawn();
            yield break;
        }

        yield return StartCoroutine(RetreatingSequence(notifyController: true));
    }

    /// <summary>
    /// Snaps to face the despawn point, sprints back to it, then either calls
    /// <see cref="SuspectController.OnMutantIntruderComplete"/> (climbing mutants that gave up)
    /// or despawns directly (non-climbing mutants whose lineup slot was already released).
    /// A hard <see cref="MutantIntruderData.retreatDespawnTimeout"/> deadline ensures the
    /// mutant is force-despawned within that window even if it never reaches the despawn point.
    /// </summary>
    private IEnumerator RetreatingSequence(bool notifyController)
    {
        if (_isDone) yield break;

        float retreatDeadline = Time.time + _data.retreatDespawnTimeout;

        // Quick snap-turn toward the despawn direction.
        Vector3 toSpawn = _despawnPos.position - transform.position;
        toSpawn.y = 0f;
        if (toSpawn.sqrMagnitude > 0.001f)
        {
            bool rotDone = false;
            _activeTween = transform
                .DORotateQuaternion(Quaternion.LookRotation(toSpawn.normalized), 0.2f)
                .OnComplete(() => rotDone = true);

            // Honour the hard deadline even during the turn.
            while (!rotDone && Time.time < retreatDeadline)
                yield return null;
        }

        if (_isDone) yield break;

        SetWalkingClientRpc(true);

        // About to hand off to real NavMeshAgent-driven movement (not a DOTween) — restore
        // procedural leg IK now so the retreat actually plants its feet correctly, mirroring
        // ClimbThroughSequence's restore on the breakthrough path.
        SetLegsAnimatorsBlendClientRpc(1f);

        // Re-enable the NavMeshAgent for retreat pathfinding (was disabled during walk-in).
        _agent.enabled = true;
        yield return null; // One frame for the agent to link onto the NavMesh.

        if (_agent.isOnNavMesh)
        {
            _agent.speed = _data.retreatSpeed;
            _agent.SetDestination(_despawnPos.position);

            // Sprint until arrival OR the hard deadline fires — whichever comes first.
            while (!_isDone && Time.time < retreatDeadline)
            {
                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + ArrivalTolerance)
                    break;
                yield return new WaitForSeconds(ArrivalPollInterval);
            }

            _agent.ResetPath();
        }

        SetWalkingClientRpc(false);
        _isDone = true;

        if (notifyController)
        {
            // Climbing mutant gave up — let the controller clean up and queue the next suspect.
            _controller?.OnMutantIntruderComplete(this, brokeThrough: false);
            OnSequenceComplete?.Invoke(false);
        }
        else
        {
            // Non-climbing mutant — controller was already notified; just despawn.
            if (NetworkObject.IsSpawned)
                NetworkObject.Despawn();
            OnSequenceComplete?.Invoke(false);
        }
    }

    // ── Scripted Entrance ──────────────────────────────────────────────────────

    /// <summary>
    /// Begins the window interaction sequence from the stand position, skipping the walk-in phase.
    /// Use this when the mutant has already been placed at the booth window via a scripted entrance
    /// (e.g. falling from above). Must be called on the server only, after the NetworkObject is spawned.
    /// </summary>
    public void BeginAtStandPos(
        MutantIntruderData data,
        Transform standPos,
        Transform despawnPos,
        Transform climbThroughTargetPos,
        ShutterController shutterController,
        SuspectController controller)
    {
        if (!IsServer) return;

        _data = data;
        _standPos = standPos;
        _despawnPos = despawnPos;
        _climbThroughTargetPos = climbThroughTargetPos;
        _shutterController = shutterController;
        _controller = controller;

        SubscribeLineupDeathSafetyNet();

        _mutantEnemy?.SuspendForLineup();

        // See BeginLineup for why this is required — SuspendForLineup() stops MutantEnemy's
        // coroutines but leaves it enabled, and its Update() (the only thing that keeps the
        // networked "Grounded" bool in sync) no-ops while _isActive is false, so it would
        // otherwise be stuck at its default false for this entire scripted-entrance sequence too.
        SetAnimBool(GroundedAnimBool, true);

        // Zero out procedural leg IK for the standing/climb sequence — see BeginLineup for why.
        SetLegsAnimatorsBlendClientRpc(0f);

        StartCoroutine(AtStandPosSequence());
    }

    private IEnumerator AtStandPosSequence()
    {
        if (!IsServer || _isDone) yield break;

        _agent.enabled = false;

        // Rotate to face the booth window.
        bool rotationDone = false;
        _activeTween = transform
            .DORotateQuaternion(_standPos.rotation, 0.5f)
            .OnComplete(() => rotationDone = true);

        yield return new WaitUntil(() => rotationDone);

        yield return new WaitForSeconds(_data.preAttackPauseSeconds);

        if (_isDone) yield break;

        if (canClimb && _shutterController != null && _shutterController.IsOpen)
        {
            var glass = BreakableGlassController.Instance;
            if (glass == null || glass.IsSmashed)
                yield return StartCoroutine(ClimbThroughSequence());
            else
                yield return StartCoroutine(GlassAttackSequence());
        }
        else
            yield return StartCoroutine(ShutterBangSequence());
    }

    /// <summary>
    /// Assigns the Animator reference used for all animation RPCs.
    /// Call this after swapping the mesh on a full-mutant SuspectCharacter prefab so that
    /// walking, climbing, and attack bools are driven on the correct skeleton.
    /// </summary>
    public void SetAnimator(Animator a) => _animator = a;

    /// <summary>Server-side: sets an Animator bool on all clients.</summary>
    public void SetAnimBool(string paramName, bool value) => SetAnimBoolClientRpc(paramName, value);

    /// <summary>Server-side: fires an Animator trigger on all clients.</summary>
    public void TriggerAnim(string paramName) => TriggerAnimClientRpc(paramName);

    // ── ClientRpcs ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the Walking animator bool on all clients. Also drives the Speed float directly
    /// (1 while walking, 0 when stopped) since MutantEnemy's own Speed sync is suspended for
    /// the whole lineup/retreat sequence (SuspendForLineup / DOTween-driven walk-in) — without
    /// this the locomotion blend tree has no Speed input and the walk-in plays as an idle pose.
    /// </summary>
    [ClientRpc]
    private void SetWalkingClientRpc(bool walking)
    {
        if (_animator != null)
        {
            _animator.SetBool("Walking", walking);
            _animator.SetFloat("Speed", walking ? 1f : 0f);
        }
    }

    /// <summary>Sets the climbing animator bool on all clients for the breakthrough sequence.</summary>
    [ClientRpc]
    private void SetClimbingClientRpc(bool climbing)
    {
        if (_animator != null)
            _animator.SetBool(ClimbingAnimBool, climbing);
    }

    /// <summary>
    /// Enables or disables every <see cref="LegsAnimator"/> on this mutant (and its children)
    /// on all clients by setting its Blend to 0 or 1 (rather than toggling 'enabled', which would
    /// permanently skip LegsAnimator's own Start()/Initialize() if disabled before it first runs).
    /// Used to suspend procedural leg IK during the DOTween-driven walk-in and window
    /// climb-through, then restore it once the mutant resumes genuine NavMeshAgent-driven
    /// grounded locomotion (post-breakthrough chase, or retreat).
    /// </summary>
    [ClientRpc]
    private void SetLegsAnimatorsBlendClientRpc(float blend)
    {
        foreach (LegsAnimator legsAnimator in GetComponentsInChildren<LegsAnimator>(true))
        {
            legsAnimator.LegsAnimatorBlend = blend;
        }
    }

    /// <summary>Sets the BangOnShutters animator bool on all clients. Used to drive the shutter-attack animation.</summary>
    [ClientRpc]
    private void SetAttackClientRpc(bool attacking)
    {
        if (_animator != null)
            _animator.SetBool(BangOnShuttersAnimBool, attacking);
    }

    /// <summary>Fires an animator trigger on all clients.</summary>
    [ClientRpc]
    private void TriggerAnimClientRpc(string trigger)
    {
        if (_animator != null && !string.IsNullOrEmpty(trigger))
            _animator.SetTrigger(trigger);
    }

    /// <summary>Sets a named Animator bool on all clients. Used for scripted-entrance animations (e.g. falling, landing).</summary>
    [ClientRpc]
    private void SetAnimBoolClientRpc(string paramName, bool value)
    {
        if (_animator != null && !string.IsNullOrEmpty(paramName))
            _animator.SetBool(paramName, value);
    }

    /// <summary>No-op now that MutantEnemy's 'enabled' flag is never toggled — its
    /// InitialiseServer() call already set its internal _isActive NetworkVariable, which
    /// replicates on its own. Kept as a harmless RPC stub in case other callers still expect
    /// this hook to exist.</summary>
    [ClientRpc]
    private void EnableMutantEnemyClientRpc()
    {
    }

    /// <summary>Triggers hit feedback (sound + shake) on the shutter for all clients.</summary>
    [ClientRpc]
    private void HitShutterClientRpc()
    {
        ShutterController.Instance?.OnHitByMutant();
    }

    /// <summary>
    /// Updates the glass crack overlay to the given intermediate hit count on all clients.
    /// Called after every non-smashing hit in <see cref="GlassAttackSequence"/>.
    /// </summary>
    [ClientRpc]
    private void UpdateGlassClientRpc(int hitCount)
    {
        BreakableGlassController.Instance?.OnHitByMutant(hitCount);
    }

    /// <summary>
    /// Transitions the glass to the fully smashed state on all clients (hides normal glass,
    /// activates broken shards). Called on the final blow in <see cref="GlassAttackSequence"/>.
    /// </summary>
    [ClientRpc]
    private void SmashGlassClientRpc()
    {
        BreakableGlassController.Instance?.ApplySmash();
    }

    /// <summary>Plays the climb-through sound at full volume on all clients.</summary>
    [ClientRpc]
    private void PlayClimbThroughSoundClientRpc()
    {
        if (_climbThroughSound != null)
            SFXController.Instance?.Play(_climbThroughSound);
    }

    /// <summary>Plays the climb-land stinger on all clients the moment the mutant finishes climbing through.</summary>
    [ClientRpc]
    private void PlayClimbLandSoundClientRpc()
    {
        if (_climbLandSound != null)
            SFXController.Instance?.Play(_climbLandSound);
    }

    /// <summary>Starts the looping chase music on all clients with a fade-in.</summary>
    [ClientRpc]
    private void StartChaseMusicClientRpc()
    {
        if (_chaseMusic != null)
            MusicManager.Instance?.Play(_chaseMusic, loop: true, fadeInDuration: _chaseMusicFadeInSeconds);
    }
}
