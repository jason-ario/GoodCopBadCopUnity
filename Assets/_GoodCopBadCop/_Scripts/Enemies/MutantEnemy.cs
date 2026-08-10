using System;
using System.Collections;
using System.Collections.Generic;
using FIMSpace.FProceduralAnimation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum DeathBehaviour { Destroy, PlayAnimation }

/// <summary>
/// Server-authoritative mutant NPC.
/// Chases the nearest living player and attacks them when within range.
/// Requires: NetworkObject, NavMeshAgent, Animator (optional).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MutantEnemy : NetworkBehaviour
{
    /// <summary>
    /// Fired on the server just before this mutant is removed from play — either by fleeing
    /// and despawning, or by dying. Subscribe to this to defer any post-breakthrough logic
    /// (e.g. queuing the next suspect) until the mutant is truly out of the scene.
    /// </summary>
    public event Action OnRemovedFromPlay;

    /// <summary>
    /// Fired on the server the instant a <see cref="fleeInsteadOfDie"/> mutant begins its
    /// flee-and-despawn sequence — i.e. as soon as it starts running, unlike
    /// <see cref="OnRemovedFromPlay"/> which only fires once <see cref="fleeDespawnTimeout"/>
    /// has elapsed and the mutant is actually gone. Subscribe here to react immediately to the
    /// flee itself (e.g. a scripted finale mutant fleeing should end the encounter right away).
    /// </summary>
    public event Action OnFleeStarted;

    // ── Static events ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the server when any <see cref="MutantEnemy"/> instance dies.
    /// <see cref="KillMutantTask"/> and other systems subscribe to this to detect scripted
    /// mutant kills without needing a direct reference to the instance.
    /// </summary>
    public static event Action OnAnyMutantKilled;

    /// <summary>
    /// Fired on the server the first time any <see cref="MutantEnemy"/> instance acquires a
    /// player target (i.e. transitions from idle/patrol to actively chasing).
    /// <see cref="FollowTrailThreat"/> subscribes while the follow-trail task is active so that
    /// encountering a pack mutant early skips straight to the kill-mutants task.
    /// </summary>
    /// <summary>
    /// Fired the first time ANY mutant anywhere in the world spots a player, passing the mutant
    /// instance that did the spotting. Subscribers that care about a specific pack (e.g.
    /// <see cref="FollowTrailThreat"/>) must check whether the passed mutant is one of theirs —
    /// this fires for the ambient world-population spawner's mutants too, not just packs.
    /// </summary>
    public static event Action<MutantEnemy> OnAnyMutantSpottedPlayer;

    // ── Configuration ─────────────────────────────────────────────────────────

    [SerializeField] private MutantEnemyData data;

    [Header("Animation (optional)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedParameterName = "Speed";
    [SerializeField] private string groundedParameterName = "Grounded";
    [SerializeField] private string attackBoolName = "Attack";
    [SerializeField] private string deathBoolName = "Death";
    [Tooltip("Bool parameter set to true whenever the agent is moving. Derived from the synced Speed value — no extra NetworkVariable required.")]
    [SerializeField] private string runningBoolName = "Running";

    [Header("Ragdoll (optional)")]
    [Tooltip("Ragdoll controller for this mutant's rig. Enabled (which also disables the Animator above) on a permanent death, so the corpse falls physically instead of playing a death animation.")]
    [SerializeField] private RagdollController ragdollController;

    [Header("Look At (FLook Animator)")]
    [Tooltip("FIMSpace.FLook.FLookAnimator used to turn the head/spine toward the chased player. " +
             "If left empty, auto-assigned from a FLookAnimator found on this GameObject or its children.")]
    [SerializeField] private FIMSpace.FLook.FLookAnimator lookAnimator;

    [Tooltip("Maximum distance at which the mutant will aim the look animator at the player it is chasing. " +
             "Independent from attackRange/detectionRadius so head-tracking can be tuned separately.")]
    [SerializeField] private float lookAtRange = 12f;

    [Header("Attack Hitbox")]
    [Tooltip("Hitbox component used to sphere-cast at the melee hit frame.")]
    [SerializeField] private MutantAttackHitbox attackHitbox;

    [Tooltip("Delay in seconds from the start of the attack animation to the melee impact frame.")]
    [SerializeField] private float attackHitDelay = 0.4f;

    [Header("Second Attack Animation (optional)")]
    [Tooltip("When enabled, the mutant randomly picks between the primary attack state (\"Mutant Attack\") " +
             "and the second attack state below each time it attacks.")]
    [SerializeField] private bool useSecondAttackAnimation = false;

    [Tooltip("Animator state name to CrossFade into for the second attack animation. Only used when " +
             "useSecondAttackAnimation is enabled.")]
    [SerializeField] private string secondAttackStateName = "Mutant Attack 2";

    [Header("Hit / Knockback Animation (optional)")]
    [Tooltip("When enabled, fires hitTriggerName on the Animator whenever this mutant survives a " +
             "knockback-carrying hit. Leave disabled for mutants whose rig/controller has no hit-reaction " +
             "state set up.")]
    [SerializeField] private bool enableHitAnimation = false;

    [Tooltip("Animator trigger parameter name fired for the hit/knockback reaction. Using a trigger (rather " +
             "than CrossFade) lets the Animator Controller's own transitions decide how/when to blend into " +
             "it, so it doesn't stomp whatever state (attack, locomotion, etc.) is currently playing. Only " +
             "used when enableHitAnimation is enabled.")]
    [SerializeField] private string hitTriggerName = "Hit";

    [Header("Door Interaction")]
    [Tooltip("Radius within which the mutant detects and forces open doors or gates blocking its NavMesh path.")]
    [SerializeField] private float doorDetectionRadius = 3f;

    [Tooltip("Minimum time in seconds between consecutive door-open attempts.")]
    [SerializeField] private float doorOpenCooldownDuration = 3f;

    [Header("Hit Feedback")]
    [Tooltip("Particle prefab instantiated on all clients at the point of impact when this enemy is hit.")]
    [SerializeField] private GameObject hitParticlePrefab;

    [Header("Gore")]
    [Tooltip("Collider used to compute random surface spawn points for gore pieces. Defaults to a CapsuleCollider or, failing that, any Collider found on this GameObject.")]
    [SerializeField] private Collider goreCollider;

    [Tooltip("Random gore chunk/giblet prefabs that burst out of this mutant on a permanent death. Leave empty to disable gore entirely.")]
    [SerializeField] private GameObject[] goreDropPrefabs;

    [Tooltip("Minimum and maximum number of gore pieces spawned in the burst when this mutant dies.")]
    [SerializeField] private Vector2Int deathGoreBurstCountRange = new Vector2Int(4, 8);

    [Tooltip("Minimum and maximum random pop speed (units/sec) applied to each gore piece spawned in the death burst.")]
    [SerializeField] private Vector2 deathGoreBurstSpeedRange = new Vector2(3f, 7f);

    [Tooltip("Seconds before a spawned gore piece is automatically destroyed.")]
    [Min(0f)]
    [SerializeField] private float goreLifetime = 10f;

    [Tooltip("If a gore piece's Y position ever drops this many units below the point it spawned " +
             "at (e.g. it clipped through the floor and is falling forever, out of reach), it is " +
             "immediately despawned/destroyed instead of being left to fall indefinitely.")]
    [Min(0.1f)]
    [SerializeField] private float goreMaxFallDistance = 15f;

    [Tooltip("Seconds after a cosmetic gore piece becomes active before its Rigidbody is switched " +
             "to kinematic (perf optimization — once it's popped/fallen/settled there's no gameplay " +
             "reason left to keep simulating it). Networked JunkItem gore is unaffected; its " +
             "kinematic state is already managed by Netcode's NetworkRigidbody.")]
    [Min(0f)]
    [SerializeField] private float goreKinematicDelay = 2f;

    [Tooltip("Blood decal prefabs spawned on the ground where a cosmetic gore piece lands (i.e. one that landed outside the Trash Task's yard). Purely cosmetic/local — leave empty to disable landing decals.")]
    [SerializeField] private GameObject[] bloodDecalPrefabs;

    [Tooltip("Layer(s) considered 'ground' for the purpose of spawning a landing blood decal under a gore piece.")]
    [SerializeField] private LayerMask goreGroundLayer;

    [Tooltip("Seconds before a landing blood decal is automatically destroyed. 0 = never.")]
    [Min(0f)]
    [SerializeField] private float bloodDecalLifetime = 20f;

    [Tooltip("Networked blood-decal prefabs (must have a NetworkObject + GraffitiInteractable) spawned " +
             "under EVERY gore piece dropped in a death burst, whether or not it lands inside the Trash " +
             "Task's yard. Registered with CleanBloodTask (see CleanBloodTask.RegisterBloodSplatter) so " +
             "each splatter counts toward the post-breach clean-up objective and blocks clock-out until " +
             "scrubbed — same as the gore piece itself, which IS registered as junk when it lands in the " +
             "yard. All must be registered as Network Prefabs in the NetworkManager. Leave empty to " +
             "disable gore blood splatters entirely.")]
    [SerializeField] private GameObject[] yardBloodDecalPrefabs;

    [Tooltip("Small cosmetic blood-spray particle spawned alongside every gore blood-splatter decal " +
             "(both the yard splatter and any cosmetic landing decal), aligned with the same ground " +
             "normal and in world space. Purely cosmetic/local — not a NetworkObject, but broadcast to " +
             "every client via RPC for the yard splatter so it's visible everywhere. Leave unassigned " +
             "to disable.")]
    [SerializeField] private GameObject bloodParticlePrefab;

    [Tooltip("Seconds before a spawned blood particle effect is automatically destroyed.")]
    [Min(0f)]
    [SerializeField] private float bloodParticleLifetime = 3f;

    [Tooltip("'Splat' sound played (spatialized at the contact point, via SFXController) the instant a " +
             "cosmetic gore piece from the death burst first lands on the ground. Leave unassigned to disable.")]
    [SerializeField] private AudioClip goreLandingSound;

    [Header("Corpse Junk")]
    [Tooltip("Optional JunkItem component pre-attached (and disabled) to this corpse, matching the same " +
             "pre-attached-but-disabled pattern documented on JunkItem for SuspectCharacter bodies. When " +
             "assigned and Death Behaviour is PlayAnimation (the corpse persists instead of despawning " +
             "immediately), this is enabled on death so the corpse becomes collectible junk and is " +
             "registered with TakeOutTrashTask. Leave unassigned to skip (e.g. mutants using the Destroy " +
             "death behaviour, which despawn immediately and leave nothing to collect).")]
    [SerializeField] private JunkItem corpseJunkItem;

    [Tooltip("Dedicated interaction collider for corpseJunkItem. Kept disabled until death and excluded " +
             "from DisableColliders() so junk pickup still works after the corpse's other colliders are " +
             "disabled. Required (on the Interactable layer) when corpseJunkItem is assigned.")]
    [SerializeField] private Collider corpseJunkInteractionCollider;


    [Header("Aggro Target")]
    [Tooltip("If false, this mutant will never head for the booth on its own, regardless of the aggro chance roll.")]
    [SerializeField] private bool canAggro = true;

    [Tooltip("Transform the mutant will move toward when aggroed (e.g. the booth). Can also be assigned at runtime via SetAggroTarget().")]
    [SerializeField] private Transform aggroTarget;

    [Header("Death")]
    [Tooltip("Destroy: despawns immediately on death. PlayAnimation: triggers the death animation then despawns after a delay.")]
    [SerializeField] private DeathBehaviour deathBehaviour = DeathBehaviour.Destroy;

    [Tooltip("Seconds to wait after triggering the death animation before despawning. Only used when Death Behaviour is PlayAnimation.")]
    [Min(0f)]
    [SerializeField] private float deathAnimationDuration = 2f;

    [Tooltip("Sound played on all clients when this enemy dies.")]
    [SerializeField] private AudioClip deathSound;

    [Header("Sounds")]
    [Tooltip("Clips played spatially at random when this mutant takes a hit and survives.")]
    [SerializeField] private AudioClip[] _hurtSounds;

    [Tooltip("Clips played spatially at random while this mutant is actively chasing a player. " +
             "Acts as an audible \"grunt\" that can alert nearby players to the mutant's presence.")]
    [SerializeField] private AudioClip[] _chaseSounds;

    [Tooltip("Minimum seconds between random chase screams/grunts.")]
    [Min(0.5f)]
    [SerializeField] private float _chaseScreamIntervalMin = 3f;

    [Tooltip("Maximum seconds between random chase screams/grunts.")]
    [Min(0.5f)]
    [SerializeField] private float _chaseScreamIntervalMax = 6f;

    [Tooltip("Seconds to fade out chase music when this mutant dies or flees.")]
    [Min(0f)]
    [SerializeField] private float _chaseMusicFadeOutSeconds = 2f;

    [Header("Footsteps")]
    [Tooltip("Clips played spatially at random for footsteps while this mutant is outside.")]
    [SerializeField] private AudioClip[] _outsideFootstepClips;

    [Tooltip("Clips played spatially at random for footsteps while this mutant is inside.")]
    [SerializeField] private AudioClip[] _insideFootstepClips;

    [Tooltip("Seconds between footstep sounds while moving. Mirrors the interval used by SuspectFootstepsAudio.")]
    [Min(0.05f)]
    [SerializeField] private float _footstepInterval = 0.5f;

    [Tooltip("Minimum NavMeshAgent speed (m/s) required to trigger footsteps.")]
    [SerializeField] private float _footstepMovementThreshold = 0.1f;

    [Range(0f, 0.5f)]
    [Tooltip("Random pitch variance applied to each footstep clip.")]
    [SerializeField] private float _footstepPitchRandomness = 0.1f;

    [Tooltip("Set to true when this mutant is outdoors, false when indoors. Controls which footstep clip set is used.")]
    [SerializeField] private bool _isOutside = true;

    [Header("Flee Behaviour")]
    [Tooltip("When enabled, reaching zero health triggers a rapid flee-and-despawn instead of a normal death. " +
             "Intended for fully-mutated civilian variants that cannot be permanently killed in the world.")]
    [SerializeField] private bool fleeInsteadOfDie = false;

    [Tooltip("NavMesh movement speed during the flee phase. Should be noticeably faster than normal move speed.")]
    [Min(1f)]
    [SerializeField] private float fleeSpeed = 12f;

    [Tooltip("Seconds after beginning the flee before the mutant force-despawns regardless of distance.")]
    [Min(1f)]
    [SerializeField] private float fleeDespawnTimeout = 8f;

    [Header("Friendly Fire")]
    [Tooltip("When true, TakeDamage() is a no-op for this mutant — pistol shots, melee hits (shovel, " +
             "hammer), shotgun pellets, flamethrower ticks, etc. all do nothing, regardless of whether " +
             "this mutant is currently active/hostile. Use this on suspects that should never be harmed " +
             "by player weapons — e.g. guard soldiers or story-critical suspects like Vlad.")]
    [SerializeField] private bool _ignoreFriendlyFireDamage = false;

    [Header("Deferred Initialisation")]
    [Tooltip("When false, InitialiseServer() is NOT called automatically on OnNetworkSpawn. " +
             "Use this when the enemy lives on a SuspectCharacter prefab and should only activate " +
             "after its booth cutscene completes. Call InitialiseServer() manually via SuspectCharacter.BeginMutantBehavior().")]
    [SerializeField] private bool _autoInitialiseOnSpawn = true;

    // ── State ──────────────────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private Transform _currentTarget;
    private float _health;
    private float _attackCooldownTimer;
    private float _doorOpenCooldownTimer;
    private float _chaseScreamTimer;
    private float _footstepTimer;
    private bool _isDead;
    private Coroutine _knockbackCoroutine;

    // Patrol & aggro state (server only)
    private Vector3 _spawnPosition;
    private bool _isAggroed;
    private bool _forceAggro;
    private bool _patrolWaiting;
    private float _patrolWaitTimer;
    private PerimiterFence _fenceTarget;
    private DoorController _doorTarget;

    /// <summary>
    /// When true, the Chase branch of <see cref="ChaseLoop"/> re-targets whichever living
    /// player is currently nearest with no <see cref="MutantEnemyData.detectionRadius"/> cap,
    /// and smashes through any blocking <see cref="PerimiterFence"/> along the way — same as
    /// the aggro-to-<see cref="aggroTarget"/> path, but toward a dynamic player target instead
    /// of a fixed structure. Set via <see cref="SetBreachChargeMode"/>, used by
    /// <see cref="MutantBreachManager"/> so breach mutants relentlessly charge players instead
    /// of patrolling or waiting for one to wander into detection range.
    /// </summary>
    private bool _breachChargeMode;

    /// <summary>
    /// When true, this mutant is frozen in place — it does not target, chase, patrol, or attack,
    /// regardless of any player's proximity. Used by pack spawns (e.g. <see cref="FollowTrailThreat"/>)
    /// to keep freshly-spawned mutants exactly where they landed until a player actually approaches
    /// the encounter area, instead of letting them immediately wander/patrol away from it. Set via
    /// <see cref="SetHeld"/>; takes effect on the very next <see cref="ChaseLoop"/> tick.
    /// </summary>
    private bool _isHeld;

    /// <summary>
    /// Earliest time (Time.time) at which the breach-charge Chase branch is allowed to hunt for
    /// a new blocking fence again after the previous one broke. Gives the mutant a couple of
    /// seconds to actually walk through the gap it just opened before the straight-line sweep in
    /// <see cref="FindBlockingFenceTowardTarget"/> is allowed to flag a neighbouring, merely
    /// nearby fence segment as "blocking" — without this, a mutant would immediately re-detect
    /// an adjacent intact panel it never actually needed to break and go smash that too instead
    /// of walking through the opening straight ahead of it.
    /// </summary>
    private float _fenceRecheckAllowedTime;

    // Destination deduplication — prevents redundant SetDestination calls for stationary
    // targets, which cause a 1-frame path-recalculation stutter every retarget tick.
    private Vector3 _lastSetDestination = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    private object _lastDestinationTarget;
    private const float DestinationChangeSqrThreshold = 0.25f; // skip re-set if target moved < 0.5 m

    /// <summary>Grace period after breaking a fence before hunting for another blocking one — see <see cref="_fenceRecheckAllowedTime"/>.</summary>
    private const float FenceRecheckGraceSeconds = 2f;

    /// <summary>
    /// True once this enemy has died, regardless of whether it has been despawned yet.
    /// </summary>
    public bool IsDead => _isDead;

    /// <summary>
    /// Valid only once <see cref="OnRemovedFromPlay"/> has fired. True when this enemy's removal
    /// was a genuine permanent death (normal kill, or a fire kill on a flee-instead-of-die unit).
    /// False when it was a flee-and-despawn (fleeInsteadOfDie unit killed by non-fire damage).
    /// <see cref="SuspectCharacter"/> reads this inside its OnRemovedFromPlay handler to decide
    /// whether to register or clear the legacy-mutant record for this suspect.
    /// </summary>
    public bool DiedPermanently { get; private set; }

    /// <summary>
    /// True once this mutant has been activated (<see cref="InitialiseServer"/> has run and
    /// <see cref="_isActive"/> is set), false while dormant. A <see cref="SuspectCharacter"/>'s
    /// own <see cref="MutantEnemy"/> component stays false until it actually transforms — read
    /// this to distinguish a genuine roaming/hostile mutant from a suspect that hasn't turned
    /// yet. Server value, replicated to every client via NetworkVariable.
    /// </summary>
    public bool IsActive => _isActive.Value;

    /// <summary>
    /// True when this mutant is outdoors, false when indoors. Controls which footstep clip set
    /// (<see cref="_outsideFootstepClips"/> vs <see cref="_insideFootstepClips"/>) is used.
    /// Mirrors <see cref="SuspectFootstepsAudio.IsOutside"/> — set externally if a mutant needs
    /// to track indoor/outdoor transitions at runtime. Defaults to the inspector-configured value.
    /// </summary>
    public bool IsOutside
    {
        get => _isOutside;
        set => _isOutside = value;
    }

    // Synced animator speed so non-owners see movement blend correctly
    private readonly NetworkVariable<float> _networkSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Synced animator grounded state so non-owners see the correct grounded blend
    private readonly NetworkVariable<bool> _networkGrounded = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Synced NetworkObjectId of the player the look animator should aim at (0 = none).
    // Resolved locally on every client into a Transform so FLookAnimator can be driven
    // without requiring the target reference itself to be replicated.
    private readonly NetworkVariable<ulong> _networkLookTargetId = new NetworkVariable<ulong>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Server-authoritative gameplay-active flag. Replaces toggling <c>enabled</c> on this
    /// component: this NetworkBehaviour's own Unity-level enabled state must never change again
    /// (see <see cref="Awake"/>) because Netcode decides which NetworkBehaviours to include in a
    /// scene-object's synchronization stream based on each machine's own local
    /// isActiveAndEnabled at that moment — if the server had this component enabled (active
    /// mutant) while a newly-joining client's freshly-loaded copy still had it disabled (its own
    /// Awake-time default), the two machines built different-length synchronization payloads for
    /// the same object, corrupting the byte stream and native-crashing the client
    /// (NetworkObject.SynchronizeNetworkBehaviours). Being a NetworkVariable, this flag's current
    /// value is delivered correctly to every client — including ones that join long after the
    /// mutant activated — via ordinary variable replication instead of a ClientRpc that only
    /// reaches whoever happens to be connected at the moment it fires.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        if (goreCollider == null)
            goreCollider = GetComponent<CapsuleCollider>() as Collider ?? GetComponent<Collider>();

        if (lookAnimator == null)
            lookAnimator = GetComponentInChildren<FIMSpace.FLook.FLookAnimator>(true);

        // Always stay enabled — see the comment on _isActive above. Dormancy is now driven
        // entirely by _isActive (defaults to false), never by this component's own Unity
        // enabled flag, so every client's synchronization payload for this NetworkBehaviour
        // stays consistent regardless of when it joins.
        enabled = true;
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _autoInitialiseOnSpawn)
        {
            InitialiseServer();
        }

        // All clients track the synced speed and grounded state for animation
        _networkSpeed.OnValueChanged += OnNetworkSpeedChanged;
        ApplyAnimatorSpeed(_networkSpeed.Value);

        _networkGrounded.OnValueChanged += OnNetworkGroundedChanged;
        ApplyAnimatorGrounded(_networkGrounded.Value);

        // All clients resolve the synced look target id into a local Transform for FLookAnimator.
        _networkLookTargetId.OnValueChanged += OnNetworkLookTargetChanged;
        ApplyLookTarget(_networkLookTargetId.Value);

        // Server-only: any corpse still lingering (deathBehaviour == PlayAnimation, see Die())
        // gets swept away the next time a day starts, regardless of where it died.
        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += DespawnCorpseOnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkSpeed.OnValueChanged -= OnNetworkSpeedChanged;
        _networkGrounded.OnValueChanged -= OnNetworkGroundedChanged;
        _networkLookTargetId.OnValueChanged -= OnNetworkLookTargetChanged;

        if (IsServer && ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= DespawnCorpseOnDayStart;
    }

    /// <summary>
    /// Server-side <see cref="ShiftManager.OnDayStart"/> handler. Corpses left behind by a
    /// permanent kill with <see cref="deathBehaviour"/> set to <see cref="DeathBehaviour.PlayAnimation"/>
    /// are never despawned by <see cref="Die"/> itself (they persist so they can be collected as
    /// junk — see <see cref="EnableCorpseJunkPickup"/>), so any that are still around once the
    /// next day starts are cleaned up here instead of lingering forever.
    /// </summary>
    private void DespawnCorpseOnDayStart()
    {
        // DiedPermanently (as opposed to _isDead alone) excludes mutants mid-flee — those
        // despawn on their own via FleeAndDespawn and should survive to be re-encountered later,
        // not be swept up as a corpse.
        if (!DiedPermanently || !IsSpawned)
            return;

        NetworkObject.Despawn();
    }

    /// <summary>
    /// Sets up health, NavMeshAgent settings, and starts the chase loop.
    /// Called automatically from OnNetworkSpawn on the server, unless a MutantSuspectBehaviour
    /// component is present — in that case it is called manually after lineup breakthrough.
    /// </summary>
    public void InitialiseServer()
    {
        if (data == null)
        {
            Debug.LogError($"[MutantEnemy] No MutantEnemyData assigned on {gameObject.name}. Despawning.", this);
            NetworkObject.Despawn();
            return;
        }

        _health = data.maxHealth;

        // Marks this mutant as gameplay-active. Does NOT touch this component's Unity
        // enabled flag — that stays permanently true from Awake() onward (see _isActive).
        if (IsServer)
            _isActive.Value = true;

        _agent.speed = data.moveSpeed;
        _agent.angularSpeed = data.angularSpeed;
        _agent.acceleration = data.acceleration;
        _agent.stoppingDistance = data.stoppingDistance;
        _agent.updateRotation = true;
        _agent.isStopped = false;

        _spawnPosition = transform.position;
        _isAggroed = canAggro && aggroTarget != null && (_forceAggro || UnityEngine.Random.value < data.aggroChance);

        _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);

        StartCoroutine(ChaseLoop());
    }

    /// <summary>
    /// Assigns the Animator reference used for speed and grounded blending.
    /// Call this before <see cref="InitialiseServer"/> when the enemy's Animator lives on a
    /// child that is only activated at runtime (e.g. the Mutated Version mesh on a SuspectCharacter prefab).
    /// </summary>
    public void SetAnimator(Animator a) => animator = a;

    /// <summary>
    /// Prevents <see cref="InitialiseServer"/> from firing automatically during
    /// <see cref="OnNetworkSpawn"/>. Must be called before <see cref="NetworkObject.Spawn"/>
    /// on the server when this enemy lives on a <see cref="SuspectCharacter"/> prefab and must
    /// stay dormant until the booth cutscene ends and
    /// <see cref="SuspectCharacter.BeginMutantBehavior"/> fires.
    /// </summary>
    public void DisableAutoInit() => _autoInitialiseOnSpawn = false;

    // ── Server Loops ───────────────────────────────────────────────────────────

    /// <summary>
    /// Periodically re-evaluates the nearest player target and issues NavMesh destinations.
    /// Priority order per tick: Chase > Aggro toward target > Patrol > Idle.
    /// Runs only on the server.
    /// </summary>
    private IEnumerator ChaseLoop()
    {
        const float retargetInterval = 0.5f;

        // Wait one frame for the NavMeshAgent to place itself on the NavMesh surface
        // after being instantiated at runtime. Without this, SetDestination silently
        // fails on freshly spawned agents that haven't yet been linked to the mesh.
        yield return null;

        while (!_isDead)
        {
            if (!_agent.isOnNavMesh)
            {
                yield return new WaitForSeconds(retargetInterval);
                continue;
            }

            if (_isHeld)
            {
                // Frozen — no targeting, no movement, no patrol. Just sit tight until released.
                _agent.ResetPath();
                yield return new WaitForSeconds(retargetInterval);
                continue;
            }

            bool wasChasing = _currentTarget != null;
            _currentTarget = FindNearestTarget(ignoreDetectionRadius: _breachChargeMode);

            if (!wasChasing && _currentTarget != null)
                OnAnyMutantSpottedPlayer?.Invoke(this);

            if (_currentTarget != null)
            {
                // ── Chase ──────────────────────────────────────────────────────

                // Breach charge mode: re-check for a blocking fence every tick, same as the
                // aggro-to-booth path, so a relentless breach mutant smashes through fences in
                // its way toward the nearest player instead of getting stuck avoiding a
                // non-carving obstacle it can never actually route around.
                if (_breachChargeMode)
                {
                    if (_fenceTarget == null && Time.time >= _fenceRecheckAllowedTime)
                        _fenceTarget = FindBlockingFenceTowardTarget(_currentTarget.position);

                    if (_fenceTarget != null && _fenceTarget.IsSpawned && !_fenceTarget.IsPassableByMutant)
                    {
                        if (_knockbackCoroutine == null)
                            _agent.isStopped = false;
                        _agent.stoppingDistance = data.fenceStopDistance;
                        SetAgentDestination(GetClosestFencePoint(_fenceTarget), _fenceTarget);

                        if (IsFenceTargetInRange())
                            TryAttackFence();

                        yield return new WaitForSeconds(retargetInterval);
                        continue;
                    }

                    // No blocking fence (or it just broke) — drop it, give ourselves a couple of
                    // seconds to actually walk through the opening before hunting for another
                    // one, and fall through to a direct charge at the player below.
                    if (_fenceTarget != null)
                        _fenceRecheckAllowedTime = Time.time + FenceRecheckGraceSeconds;
                    _fenceTarget = null;
                    _agent.stoppingDistance = data.stoppingDistance;
                }

                SetAgentDestination(_currentTarget.position, _currentTarget);

                float distanceToTarget = Vector3.Distance(transform.position, _currentTarget.position);

                if (distanceToTarget <= data.attackRange)
                {
                    TryAttack();
                }
                else if (!_agent.pathPending
                         && _agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    TryBangBlockingDoorTowardTarget(_currentTarget);
                }
            }
            else
            {
                // Lost or never had a player target; reset patrol state so we
                // pick a new waypoint immediately rather than waiting out a stale timer.
                if (wasChasing)
                    _patrolWaiting = false;

                if (_isAggroed && aggroTarget != null)
                {
                    // ── Aggro ──────────────────────────────────────────────────

                    // Drop stale door target.
                    if (_doorTarget != null && (!_doorTarget.IsSpawned || !_doorTarget.IsDoorClosed))
                    {
                        _doorTarget = null;
                        InvalidateDestination();
                    }

                    // Re-check for a fence directly between us and the aggro target before
                    // committing to a NavMesh route — this must happen every tick (not just when
                    // _fenceTarget is null in Update()) so a freshly-detected fence redirects the
                    // agent immediately instead of first routing the long way around via NavMesh.
                    if (_fenceTarget == null)
                        _fenceTarget = FindBlockingFenceTowardTarget(aggroTarget.position);

                    if (_fenceTarget != null)
                    {
                        // Navigate straight to the point on the fence blocking our path — not its
                        // GameObject pivot, which may sit on the unwalkable carved-out point or far
                        // along a long fence run. Uses fenceStopDistance (tuned to the fence's
                        // surface) rather than the general-purpose stoppingDistance, so the mutant
                        // closes in all the way to melee range instead of stopping at the same
                        // distance it would use for chasing a player or bashing a door.
                        if (_knockbackCoroutine == null)
                            _agent.isStopped = false;
                        _agent.stoppingDistance = data.fenceStopDistance;
                        SetAgentDestination(GetClosestFencePoint(_fenceTarget), _fenceTarget);

                        if (IsFenceTargetInRange())
                            TryAttackFence();
                    }
                    else if (_doorTarget != null)
                    {
                        if (_knockbackCoroutine == null)
                            _agent.isStopped = false;
                        _agent.stoppingDistance = data.stoppingDistance;
                        SetAgentDestination(_doorTarget.transform.position, _doorTarget);

                        float distToDoor = Vector3.Distance(transform.position, _doorTarget.transform.position);
                        if (distToDoor <= data.attackRange)
                            TryBangDoor(_doorTarget);
                    }
                    else
                    {
                        if (_knockbackCoroutine == null)
                            _agent.isStopped = false;
                        _agent.stoppingDistance = data.stoppingDistance;
                        SetAgentDestination(aggroTarget.position, aggroTarget);

                        // Once the agent reaches the booth wall (partial path), find the door.
                        bool agentSettled = _agent.hasPath
                                         && !_agent.pathPending
                                         && _agent.pathStatus != NavMeshPathStatus.PathComplete
                                         && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;

                        if (agentSettled)
                            _doorTarget = FindNearestBlockingDoor();
                    }
                }
                else if (data.enablePatrol)
                {
                    // ── Patrol ─────────────────────────────────────────────────
                    UpdatePatrol();
                }
                else
                {
                    // ── Idle ───────────────────────────────────────────────────
                    _agent.ResetPath();
                }
            }

            yield return new WaitForSeconds(retargetInterval);
        }
    }

    // ── Patrol ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages waypoint selection for the patrol state.
    /// Called every <see cref="ChaseLoop"/> tick when no player is in range and the enemy is not aggroed.
    /// </summary>
    private void UpdatePatrol()
    {
        // Keep moving toward the current waypoint if we haven't arrived yet.
        if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance)
            return;

        // Arrived (or no path). Start or continue the idle wait before the next waypoint.
        if (!_patrolWaiting)
        {
            _patrolWaiting = true;
            _patrolWaitTimer = Time.time + UnityEngine.Random.Range(data.patrolWaitMin, data.patrolWaitMax);
            _agent.ResetPath();
        }

        if (Time.time >= _patrolWaitTimer)
        {
            _patrolWaiting = false;
            TrySetPatrolDestination();
        }
    }

    /// <summary>
    /// Samples a random reachable NavMesh point within <see cref="MutantEnemyData.patrolRadius"/>
    /// of the spawn position and sets it as the agent destination.
    /// </summary>
    private void TrySetPatrolDestination()
    {
        Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * data.patrolRadius;
        randomOffset.y = 0f;
        Vector3 candidate = _spawnPosition + randomOffset;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, data.patrolRadius, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    // ── Fence Assault ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the NavMesh agent's destination, skipping the call entirely when the target
    /// identity and position have not meaningfully changed since the last call.
    /// Switching to a different target always forces a new <see cref="NavMeshAgent.SetDestination"/> call.
    /// </summary>
    private void SetAgentDestination(Vector3 destination, object targetIdentity = null)
    {
        bool sameTarget = targetIdentity != null && targetIdentity == _lastDestinationTarget;
        if (sameTarget && (destination - _lastSetDestination).sqrMagnitude <= DestinationChangeSqrThreshold)
            return;

        _lastDestinationTarget = targetIdentity;
        _lastSetDestination = destination;
        _agent.SetDestination(destination);
    }

    /// <summary>
    /// Resets destination-deduplication state so the very next <see cref="SetAgentDestination"/>
    /// call always triggers a real <see cref="NavMeshAgent.SetDestination"/>, forcing the agent
    /// to replan its path. Call this whenever a blocking obstacle is removed.
    /// </summary>
    private void InvalidateDestination()
    {
        _lastDestinationTarget = null;
        _lastSetDestination    = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    }

    /// <summary>
    /// Returns true when any collider belonging to <see cref="_fenceTarget"/> overlaps the
    /// attack-range sphere around the mutant. Uses collider overlap so it works correctly
    /// for long fence segments whose transform center may be several metres away.
    /// </summary>
    private bool IsFenceTargetInRange()
    {
        if (_fenceTarget == null) return false;

        Collider[] cols = Physics.OverlapSphere(transform.position, data.attackRange);
        foreach (Collider col in cols)
        {
            if (col.GetComponentInParent<PerimiterFence>() == _fenceTarget)
                return true;
        }
        return false;
    }

    /// <summary>
    /// SphereCasts in a straight line from the mutant directly toward <paramref name="targetPosition"/>
    /// and returns the nearest non-passable <see cref="PerimiterFence"/> found along that line, up to
    /// the full distance to the target.
    ///
    /// This is intentionally independent of the mutant's current facing/movement direction: while
    /// aggroed, the NavMeshAgent's own path (which routes around the fence's carved NavMeshObstacle)
    /// must not be trusted to reveal fences that stand directly between the mutant and its target — an
    /// aggroed mutant knows it can smash through fences, so it should beeline straight at the target and
    /// only detour to attack whichever fence segment is actually in the way of that straight line.
    /// </summary>
    private PerimiterFence FindBlockingFenceTowardTarget(Vector3 targetPosition)
    {
        // Cast from chest height toward the target, ignoring vertical offset.
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 toTarget = targetPosition - origin;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        if (distance < 0.01f)
            return null;

        Vector3 direction = toTarget / distance;

        // Kept tight (well under half a fence panel's width) so only a fence genuinely dead
        // ahead of the mutant registers as "blocking" — a wider sweep was grazing the edge of
        // an adjacent, easily-walkable-around panel right after breaking the one actually in
        // the way, causing the mutant to smash a second fence it never needed to.
        float radius = 0.35f;

        RaycastHit[] hits = Physics.SphereCastAll(origin, radius, direction, distance);
        if (hits.Length == 0)
            return null;

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            PerimiterFence fence = hit.collider.GetComponentInParent<PerimiterFence>();
            if (fence != null && fence.IsSpawned && !fence.IsPassableByMutant)
                return fence;
        }

        return null;
    }

    /// <summary>
    /// Returns the point on <paramref name="fence"/>'s collider closest to the mutant, used as the
    /// NavMesh destination so the mutant walks straight up to the fence segment that is actually
    /// blocking it rather than toward the fence GameObject's pivot (which may sit on an unwalkable
    /// carved-out point, or far along a long fence run).
    /// </summary>
    private Vector3 GetClosestFencePoint(PerimiterFence fence)
    {
        Collider fenceCollider = fence.GetComponentInChildren<Collider>();
        return fenceCollider != null ? fenceCollider.ClosestPoint(transform.position) : fence.transform.position;
    }

    /// <summary>
    /// Triggers an attack against the cached fence target if the cooldown has elapsed.
    /// Plays the standard attack animation and schedules the impact hit via <see cref="DelayedFenceHit"/>.
    /// </summary>
    private void TryAttackFence()
    {
        if (Time.time < _attackCooldownTimer || _fenceTarget == null)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        // Capture the target NOW so DelayedFenceHit doesn't accidentally hit whichever
        // fence happens to be cached when the coroutine executes.
        PerimiterFence target = _fenceTarget;

        TriggerAttackAnimationClientRpc();
        StartCoroutine(DelayedFenceHit(target));
    }

    /// <summary>
    /// Waits for the melee impact frame then fires the hitbox's fence scan — the same OverlapSphere
    /// that the player attack uses, so the hit only registers when the animation reaches the mutant's
    /// arm. Falls back to a centre-distance check when no hitbox is assigned.
    /// </summary>
    private IEnumerator DelayedFenceHit(PerimiterFence fence)
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_isDead || fence == null || !fence.IsSpawned)
            yield break;

        if (attackHitbox != null)
        {
            attackHitbox.PerformFenceHitScan(data.fenceDamagePerHit, fence);
        }
        else
        {
            // Fallback: centre-distance check when no hitbox component is configured.
            if (Vector3.Distance(transform.position, fence.transform.position) <= data.attackRange)
            {
                // Approximate hit position as the point on the fence closest to this mutant.
                Collider fenceCollider = fence.GetComponentInChildren<Collider>();
                Vector3 hitPosition = fenceCollider != null
                    ? fenceCollider.ClosestPoint(transform.position)
                    : fence.transform.position;
                fence.TakeMutantHitServer(data.fenceDamagePerHit, hitPosition);
            }
        }
    }

    private void Update()
    {
        if (!IsServer || !_isActive.Value || !_agent.isActiveAndEnabled) return;

        // Always sync locomotion state so clients see movement during flee even though _isDead is true.
        _networkSpeed.Value = _agent.velocity.magnitude;
        _networkGrounded.Value = _agent.isOnNavMesh;

        if (_isDead) return;

        // ── Chase Scream ───────────────────────────────────────────────────────
        if (_currentTarget != null)
        {
            _chaseScreamTimer -= Time.deltaTime;
            if (_chaseScreamTimer <= 0f && _chaseSounds != null && _chaseSounds.Length > 0)
            {
                int idx = UnityEngine.Random.Range(0, _chaseSounds.Length);
                PlayChaseSoundClientRpc(idx);
                _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);
            }
        }
        else
        {
            // Reset to a fresh interval so the first scream fires naturally after acquiring a target.
            _chaseScreamTimer = UnityEngine.Random.Range(_chaseScreamIntervalMin, _chaseScreamIntervalMax);
        }

        // ── Footsteps ──────────────────────────────────────────────────────────
        // Server-driven, same as the chase scream above — the agent's movement state only
        // reliably reflects reality on the server, so footstep timing is computed here and
        // broadcast to clients via RPC rather than relying on each client's own (unmoved)
        // NavMeshAgent velocity, matching the pattern used for chase/hurt sounds.
        bool isMoving = _agent.velocity.sqrMagnitude > _footstepMovementThreshold * _footstepMovementThreshold;
        if (isMoving)
        {
            _footstepTimer += Time.deltaTime;
            if (_footstepTimer >= _footstepInterval)
            {
                _footstepTimer = 0f;
                AudioClip[] clips = _isOutside ? _outsideFootstepClips : _insideFootstepClips;
                if (clips != null && clips.Length > 0)
                {
                    int idx = UnityEngine.Random.Range(0, clips.Length);
                    PlayFootstepClientRpc(_isOutside, idx);
                }
            }
        }
        else
        {
            _footstepTimer = 0f;
        }

        // ── Rotation Tracking ──────────────────────────────────────────────────
        // If we are in range to attack something, ensure we rotate to face it 
        // regardless of whether the NavMeshAgent is currently "moving".
        Transform faceTarget = null;
        if (_currentTarget != null && Vector3.Distance(transform.position, _currentTarget.position) <= data.attackRange)
        {
            faceTarget = _currentTarget;
        }
        else if (_fenceTarget != null && IsFenceTargetInRange())
        {
            faceTarget = _fenceTarget.transform;
        }
        else if (_doorTarget != null && Vector3.Distance(transform.position, _doorTarget.transform.position) <= data.attackRange * 1.5f)
        {
            faceTarget = _doorTarget.transform;
        }

        if (faceTarget != null)
        {
            Vector3 toTarget = faceTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, data.angularSpeed * Time.deltaTime);
            }
        }

        // ── Look At (FLook Animator) ────────────────────────────────────────────
        UpdateLookAtTarget();

        // ── Aggro / Fence Logic ────────────────────────────────────────────────
        // Skipped entirely in breach charge mode — ChaseLoop's Chase branch already handles
        // fence-smashing toward the dynamic nearest-player target every retarget tick, and this
        // legacy block would otherwise fight it by driving the agent toward the fixed aggroTarget.
        if (!_breachChargeMode && _isAggroed && aggroTarget != null)
        {
            if (_fenceTarget == null)
            {
                // Check the straight line to the aggro target every frame, independent of the
                // agent's current facing/movement — while aggroed the mutant beelines for the
                // target and only detours to smash through whichever fence is actually in the way.
                _fenceTarget = FindBlockingFenceTowardTarget(aggroTarget.position);
            }
            else if (!_fenceTarget.IsSpawned || _fenceTarget.IsPassableByMutant)
            {
                // Fence broken — resume navigation toward the aggro target immediately.
                _fenceTarget = null;
                if (_knockbackCoroutine == null)
                    _agent.isStopped = false;
                _agent.stoppingDistance = data.stoppingDistance;
                InvalidateDestination();
                _agent.SetDestination(aggroTarget.position);
            }
            else if (IsFenceTargetInRange())
            {
                // Do NOT force _agent.isStopped here — attackRange (used by IsFenceTargetInRange)
                // is intentionally larger than the fence-specific approach distance, so freezing
                // movement as soon as it's true stops the mutant well before it closes in to
                // fenceStopDistance. Let the NavMeshAgent's own stoppingDistance/autoBraking (set
                // to data.fenceStopDistance in ChaseLoop) bring it to a natural stop right at the
                // fence, and only gate the attack trigger on range here.
                TryAttackFence();
            }
        }
    }

    // ── Targeting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns the aggro target at runtime (e.g. called by the spawner before <see cref="NetworkObject.Spawn"/>).
    /// If <paramref name="target"/> is non-null and the aggro chance roll succeeds in
    /// <see cref="InitialiseServer"/>, this mutant will head toward the target instead of patrolling.
    /// </summary>
    public void SetAggroTarget(Transform target)
    {
        aggroTarget = target;
    }

    /// <summary>
    /// Forces this mutant into aggro mode on spawn, bypassing the <see cref="MutantEnemyData.aggroChance"/> roll.
    /// Must be called before <see cref="NetworkObject.Spawn"/> so <see cref="InitialiseServer"/> reads it.
    /// Requires a valid aggro target — pair with <see cref="SetAggroTarget"/>.
    /// </summary>
    public void SetForceAggro(bool forceAggro)
    {
        _forceAggro = forceAggro;
    }

    /// <summary>
    /// Enables/disables breach charge mode — see <see cref="_breachChargeMode"/>.
    /// Safe to call before or after <see cref="NetworkObject.Spawn"/>; takes effect on the very
    /// next <see cref="ChaseLoop"/> tick.
    /// </summary>
    public void SetBreachChargeMode(bool chargeMode)
    {
        _breachChargeMode = chargeMode;
    }

    /// <summary>
    /// Freezes/unfreezes this mutant — see <see cref="_isHeld"/>. Safe to call before or after
    /// <see cref="NetworkObject.Spawn"/>; takes effect on the very next <see cref="ChaseLoop"/> tick.
    /// </summary>
    public void SetHeld(bool held)
    {
        _isHeld = held;
    }

    /// <summary>
    /// Stops all running coroutines (e.g. an auto-started ChaseLoop) so that
    /// <see cref="MutantSuspectBehaviour"/> can take exclusive control during a lineup sequence.
    /// Called by <see cref="MutantSuspectBehaviour.BeginLineup"/> before the lineup coroutine starts.
    /// Does NOT disable this component — see the comment on <see cref="Awake"/> for why this
    /// component's Unity-level enabled flag must never be toggled. <see cref="Update"/> already
    /// no-ops while <see cref="_isActive"/> is false and while the NavMeshAgent is disabled
    /// (both true for the duration of the lineup sequence), so leaving the component enabled is
    /// safe and is required for its Grounded/Speed animator sync to resume automatically once
    /// the mutant breaks through and <see cref="InitialiseServer"/> re-activates it.
    /// </summary>
    public void SuspendForLineup()
    {
        StopAllCoroutines();
    }

    /// <summary>
    /// Finds the nearest valid target overall — a living player or a living guard soldier
    /// (<see cref="SoldierMutantResponder"/>), whichever is closer. Guard soldiers are treated
    /// exactly like players as attack targets: if a soldier is standing closer than every
    /// player, the mutant chases and attacks the soldier instead.
    /// </summary>
    /// <param name="ignoreDetectionRadius">See <see cref="FindNearestLivingPlayer"/>.</param>
    private Transform FindNearestTarget(bool ignoreDetectionRadius = false)
    {
        Transform nearestPlayer = FindNearestLivingPlayer(ignoreDetectionRadius);
        Transform nearestSoldier = FindNearestLivingSoldier(ignoreDetectionRadius);

        if (nearestPlayer == null)
            return nearestSoldier;
        if (nearestSoldier == null)
            return nearestPlayer;

        float sqrDistToPlayer = (nearestPlayer.position - transform.position).sqrMagnitude;
        float sqrDistToSoldier = (nearestSoldier.position - transform.position).sqrMagnitude;

        return sqrDistToSoldier < sqrDistToPlayer ? nearestSoldier : nearestPlayer;
    }

    /// <summary>
    /// Finds the nearest living guard soldier (a <see cref="SoldierMutantResponder"/> that is
    /// still alive) within detection radius. Mirrors <see cref="FindNearestLivingPlayer"/>.
    /// </summary>
    /// <param name="ignoreDetectionRadius">
    /// When true, finds the globally nearest living soldier with no distance cap at all.
    /// </param>
    private Transform FindNearestLivingSoldier(bool ignoreDetectionRadius = false)
    {
        Transform nearest = null;
        float nearestSqrDist = ignoreDetectionRadius
            ? float.MaxValue
            : data.detectionRadius * data.detectionRadius;

        SoldierMutantResponder[] soldiers = FindObjectsByType<SoldierMutantResponder>(FindObjectsSortMode.None);
        foreach (SoldierMutantResponder soldier in soldiers)
        {
            if (soldier == null || !soldier.IsAlive)
                continue;

            float sqrDist = (soldier.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = soldier.transform;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Finds the nearest player that is alive (PlayerHealth not dead) within detection radius.
    /// Iterates all connected NetworkClients so it works in multiplayer.
    /// Players who are inside a scripted dialogue cutscene are excluded — they cannot be
    /// aggroed while the cutscene holds their controls.
    /// </summary>
    /// <param name="ignoreDetectionRadius">
    /// When true, finds the globally nearest living player with no distance cap at all —
    /// used by <see cref="_breachChargeMode"/> so breach mutants are always aware of the
    /// closest player regardless of <see cref="MutantEnemyData.detectionRadius"/>.
    /// </param>
    private Transform FindNearestLivingPlayer(bool ignoreDetectionRadius = false)
    {
        Transform nearest = null;
        float nearestSqrDist = ignoreDetectionRadius
            ? float.MaxValue
            : data.detectionRadius * data.detectionRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            PlayerHealth health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health == null || health.IsDead)
                continue;

            // Do not target players who are locked inside a scripted dialogue cutscene.
            // IsInCutscene is set by the owning client via DialogueChoiceSystem and replicated
            // to the server through a NetworkVariable, so this check is server-authoritative.
            PlayerInstance pi = client.PlayerObject.GetComponent<PlayerInstance>();
            if (pi != null && pi.IsInCutscene)
                continue;

            float sqrDist = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = client.PlayerObject.transform;
            }
        }

        return nearest;
    }

    // ── Door Interaction ───────────────────────────────────────────────────────

    /// <summary>
    /// Searches for an <see cref="IMutantPassable"/> obstacle within <see cref="doorDetectionRadius"/>
    /// that lies roughly between the mutant and its current target, and forces it open.
    /// Returns true when an unlocked door was found and opened; false otherwise.
    /// Applies a cooldown to prevent spam.
    /// </summary>
    private bool TryOpenBlockingDoor()
    {
        if (_currentTarget == null) return false;

        Vector3 toTarget = (_currentTarget.position - transform.position).normalized;
        Collider[] nearby = Physics.OverlapSphere(transform.position, doorDetectionRadius);

        foreach (Collider col in nearby)
        {
            // Walk up the hierarchy in case the collider is on a child of the door/gate.
            IMutantPassable passable = col.GetComponentInParent<IMutantPassable>();
            if (passable == null || !passable.IsBlockingMutant)
                continue;

            // Only open obstacles that are in the general direction of the target.
            Vector3 toObstacle = (col.transform.position - transform.position).normalized;
            if (Vector3.Dot(toTarget, toObstacle) <= 0f)
                continue;

            passable.OpenForMutant();
            _doorOpenCooldownTimer = Time.time + doorOpenCooldownDuration;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Searches within <see cref="doorDetectionRadius"/> for any physically-closed
    /// <see cref="DoorController"/> (locked or not) in the direction of <paramref name="target"/>,
    /// and bangs on it at the standard attack rate. Used when the chase path is blocked by a
    /// door that cannot be forced open.
    /// </summary>
    private void TryBangBlockingDoorTowardTarget(Transform target)
    {
        if (target == null) return;

        Vector3 toTarget = (target.position - transform.position).normalized;
        Collider[] nearby = Physics.OverlapSphere(transform.position, doorDetectionRadius);

        foreach (Collider col in nearby)
        {
            DoorController door = col.GetComponentInParent<DoorController>();
            if (door == null || !door.IsSpawned || !door.IsDoorClosed)
                continue;

            Vector3 toObstacle = (col.transform.position - transform.position).normalized;
            if (Vector3.Dot(toTarget, toObstacle) <= 0f)
                continue;

            TryBangDoor(door);
            return;
        }
    }

    /// <summary>
    /// Returns the nearest spawned <see cref="DoorController"/> that is physically closed,
    /// regardless of lock state. Used as a fallback when the aggro path is blocked and no
    /// non-passable perimeter fence remains.
    /// </summary>
    private DoorController FindNearestBlockingDoor()
    {
        DoorController[] allDoors = UnityEngine.Object.FindObjectsByType<DoorController>(FindObjectsSortMode.None);

        DoorController nearest = null;
        float nearestSqrDist = float.MaxValue;

        foreach (DoorController door in allDoors)
        {
            if (door == null || !door.IsSpawned || !door.IsDoorClosed)
                continue;

            float sqrDist = (door.transform.position - transform.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = door;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Triggers an attack animation against <paramref name="door"/> and schedules the bang
    /// sound at the melee impact frame. Uses the same attack-cooldown timer as player attacks
    /// so the mutant cannot simultaneously pummel both.
    /// </summary>
    private void TryBangDoor(DoorController door)
    {
        if (Time.time < _attackCooldownTimer || door == null)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        TriggerAttackAnimationClientRpc();
        StartCoroutine(DelayedDoorBang(door));
    }

    /// <summary>
    /// Waits for the melee impact frame, then plays the door-bang sound on all clients.
    /// Validates proximity at impact time so a stale coroutine does not trigger remotely.
    /// </summary>
    private IEnumerator DelayedDoorBang(DoorController door)
    {
        yield return new WaitForSeconds(attackHitDelay);

        if (_isDead || door == null || !door.IsSpawned)
            yield break;

        float distToDoor = Vector3.Distance(transform.position, door.transform.position);
        if (distToDoor <= data.attackRange * 1.5f)
            door.PlayMutantBangClientRpc();
    }

    // ── Attack ─────────────────────────────────────────────────────────────────

    private void TryAttack()
    {
        if (Time.time < _attackCooldownTimer)
            return;

        _attackCooldownTimer = Time.time + data.attackCooldown;

        if (_currentTarget == null)
            return;

        PlayerHealth targetHealth = _currentTarget.GetComponent<PlayerHealth>();
        if (targetHealth != null)
        {
            if (targetHealth.IsDead)
                return;

            // Do not attack a player who has entered a cutscene since the last ChaseLoop tick
            // (guards the window between FindNearestTarget clearing the target and the
            // next retarget interval, since _currentTarget can briefly outlive the exclusion).
            PlayerInstance targetPlayer = _currentTarget.GetComponent<PlayerInstance>();
            if (targetPlayer != null && targetPlayer.IsInCutscene)
                return;
        }
        else
        {
            // Not a player — must be a guard soldier target (see FindNearestLivingSoldier).
            SoldierMutantResponder targetSoldier = _currentTarget.GetComponent<SoldierMutantResponder>();
            if (targetSoldier == null || !targetSoldier.IsAlive)
                return;
        }

        TriggerAttackAnimationClientRpc();

        // Freeze movement for the swing's windup so the mutant plants its feet and actually
        // swings instead of continuing to slide toward the player mid-animation. DelayedHitScan
        // releases the lock once the swing connects. Skip this while a knockback shove is in
        // progress — KnockbackCoroutine already owns isStopped for its duration, and letting the
        // attack windup and the knockback fight over it is what let attacks silently cancel
        // knockback (isStopped flips back to false mid-shove, letting the agent's autopilot
        // immediately override the manual Move() displacement).
        if (_knockbackCoroutine == null)
            _agent.isStopped = true;

        // Schedule the sphere-cast to fire at the melee impact frame on the server.
        StartCoroutine(DelayedHitScan(data.damagePerHit));
    }

    /// <summary>
    /// Waits for the attack animation to finish (approximated by the cooldown duration),
    /// then clears the Attack bool on all clients so the animator transitions back to locomotion.
    /// </summary>
    private IEnumerator ResetAttackBoolAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ResetAttackAnimationClientRpc();
    }

    [ClientRpc]
    private void ResetAttackAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, false);
    }

    /// <summary>
    /// Waits for the animation's melee point, then runs the hitbox sphere-cast on the server.
    /// Also releases the movement lock set in <see cref="TryAttack"/> so the mutant resumes
    /// chasing right after the swing connects rather than staying frozen for the rest of the
    /// attack cooldown.
    /// </summary>
    private IEnumerator DelayedHitScan(float damage)
    {
        yield return new WaitForSeconds(attackHitDelay);

        // Don't resume movement if a knockback shove started mid-windup (or is still running) —
        // KnockbackCoroutine is responsible for clearing isStopped itself once the shove finishes.
        if (!_isDead && _knockbackCoroutine == null)
            _agent.isStopped = false;

        if (_isDead || attackHitbox == null)
            yield break;

        attackHitbox.PerformHitScan(damage);
    }

    [ClientRpc]
    private void TriggerAttackAnimationClientRpc()
    {
        if (animator != null)
        {
            // Use CrossFade to authoritatively force the animator into the attack state.
            // This is more robust for networked one-shots than boolean parameters.
            string attackState = "Mutant Attack";
            if (useSecondAttackAnimation && !string.IsNullOrEmpty(secondAttackStateName) && UnityEngine.Random.value < 0.5f)
                attackState = secondAttackStateName;

            animator.CrossFade(attackState, 0.2f, 0, 0f);
        }
    }

    [ClientRpc]
    private void TriggerHitAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(hitTriggerName))
        {
            // A trigger (rather than CrossFade) so the Animator Controller's own transitions decide
            // how/when to blend into the reaction, instead of forcibly overriding whatever state
            // (attack windup, locomotion, etc.) is currently playing.
            animator.SetTrigger(hitTriggerName);
        }
    }

    // ── Immobilization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Stops this enemy's NavMeshAgent for <paramref name="duration"/> seconds, then resumes
    /// movement. Server-only; has no effect if the enemy is already dead.
    /// </summary>
    /// <param name="duration">Seconds to keep the agent stopped.</param>
    public void Immobilize(float duration)
    {
        if (!IsServer || _isDead) return;
        StartCoroutine(ImmobilizeCoroutine(duration));
    }

    private IEnumerator ImmobilizeCoroutine(float duration)
    {
        _agent.isStopped = true;
        yield return new WaitForSeconds(duration);
        if (!_isDead)
            _agent.isStopped = false;
    }

    /// <summary>
    /// Shoves the mutant along <paramref name="direction"/> (horizontal plane only) over
    /// <see cref="MutantEnemyData.knockbackDuration"/> seconds, using an ease-out
    /// <see cref="NavMeshAgent.Move"/> so the shove stays clamped to the NavMesh. Interrupts
    /// pathing for the duration, then hands control straight back to <see cref="ChaseLoop"/>,
    /// which re-issues a destination on its next tick — mirroring the resume behaviour of
    /// <see cref="Immobilize"/>.
    /// </summary>
    private void ApplyKnockback(Vector3 direction)
    {
        if (!IsServer || _isDead || _agent == null || !_agent.isOnNavMesh)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(KnockbackCoroutine(direction));
    }

    private IEnumerator KnockbackCoroutine(Vector3 direction)
    {
        float distance = data.knockbackDistance;
        float duration = data.knockbackDuration;

        if (distance <= 0f || duration <= 0f)
            yield break;

        _agent.isStopped = true;

        // Ease-out shove: initial velocity chosen so total displacement over `duration`
        // equals `distance`, decaying linearly to zero.
        float initialSpeed = 2f * distance / duration;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float speed = initialSpeed * (1f - t);
            _agent.Move(direction * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!_isDead)
            _agent.isStopped = false;

        _knockbackCoroutine = null;
    }

    // ── Damage / Death ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this enemy. Call from the server (e.g. from a weapon script).
    /// No-op entirely when <see cref="_ignoreFriendlyFireDamage"/> is set on this instance, or
    /// while this mutant is still dormant (<see cref="_isActive"/> false) — suspects carry a
    /// MutantEnemy component from the moment they spawn, kept dormant until their booth
    /// transition calls <see cref="InitialiseServer"/>, but this component's own Unity
    /// 'enabled' flag stays true the whole time (see the comment on <see cref="_isActive"/>).
    /// Weapon scripts find this component via GetComponentInParent regardless of dormancy, so
    /// without this guard, hitting a suspect that hasn't mutated yet would silently
    /// damage/kill/flee the dormant MutantEnemy underneath it.
    /// </summary>
    /// <param name="amount">Damage to apply.</param>
    /// <param name="hitPoint">World-space point of impact used to position the hit particle.</param>
    /// <param name="isFireDamage">
    /// True when this damage tick came from <see cref="SetOnFire"/>. Fire damage always kills
    /// permanently, even on units with <see cref="fleeInsteadOfDie"/> enabled — it's the only
    /// way to finish off a fully-mutated resident for good.
    /// </param>
    /// <param name="knockbackDirection">
    /// When provided (non-null, non-zero), shoves the mutant this direction on a survived hit.
    /// Pass this only for a real physical impact — a melee swing or a gunshot — not for damage
    /// ticks like fire or radiation, which should hurt without physically knocking the mutant
    /// around. Ignored if the hit is lethal.
    /// </param>
    public void TakeDamage(float amount, Vector3 hitPoint, bool isFireDamage = false, Vector3? knockbackDirection = null)
    {
        if (!IsServer || _isDead || _ignoreFriendlyFireDamage || !enabled || !_isActive.Value)
            return;

        _health -= amount;

        SpawnHitParticleClientRpc(hitPoint);

        if (_health <= 0f)
        {
            Die(isFireDamage);
            return;
        }

        // Gore no longer pops out on a survived hit — it only bursts out on death
        // (see SpawnDeathGoreBurst), so shooting an enemy that doesn't kill it leaves no
        // gore/junk/blood behind.

        if (_hurtSounds != null && _hurtSounds.Length > 0)
        {
            int idx = UnityEngine.Random.Range(0, _hurtSounds.Length);
            PlayHurtSoundClientRpc(idx);
        }

        if (knockbackDirection.HasValue)
        {
            if (enableHitAnimation)
                TriggerHitAnimationClientRpc();

            ApplyKnockback(knockbackDirection.Value);
        }
    }

    /// <summary>
    /// Spawns a burst of <see cref="deathGoreBurstCountRange"/> gore pieces popping outward from
    /// random points on <see cref="goreCollider"/>'s surface. Called once from the server on a
    /// permanent death.
    /// </summary>
    private void SpawnDeathGoreBurst()
    {
        if (goreDropPrefabs == null || goreDropPrefabs.Length == 0)
            return;

        SpawnGoreBurst(deathGoreBurstCountRange, deathGoreBurstSpeedRange);
    }

    /// <summary>
    /// Rolls a random piece count within <paramref name="countRange"/> and builds randomized
    /// spawn data for each piece. Pieces that land inside the Trash Task's yard area are
    /// spawned server-side as real <see cref="JunkItem"/> NetworkObjects that count toward the
    /// task; all other pieces are purely cosmetic and broadcast in a single RPC so every client
    /// spawns the same non-networked result. Every piece — yard or not (e.g. gore from a
    /// checkpoint breach fight) — also gets a server-authoritative, networked blood-splatter
    /// decal via <see cref="SpawnGoreBloodDecal"/>, which applies the same yard-bounds rule to
    /// the splatter: in-yard blood counts toward <see cref="CleanBloodTask"/> and blocks
    /// clock-out, out-of-yard blood is cosmetic-only and despawns automatically the next day.
    /// </summary>
    private void SpawnGoreBurst(Vector2Int countRange, Vector2 speedRange)
    {
        int count = UnityEngine.Random.Range(countRange.x, countRange.y + 1);
        if (count <= 0)
            return;

        List<Vector3> cosmeticPositions = new List<Vector3>();
        List<int> cosmeticPrefabIndices = new List<int>();
        List<Vector3> cosmeticVelocities = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            Vector3 position = GetRandomGoreSpawnPosition();
            int prefabIndex = UnityEngine.Random.Range(0, goreDropPrefabs.Length);
            float speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);
            Vector3 velocity = GetRandomPopVelocity(position, speed);

            SpawnGoreBloodDecal(position);

            if (TakeOutTrashTask.Instance != null && TakeOutTrashTask.Instance.IsPositionInYard(position)
                && SpawnGoreJunkItem(position, prefabIndex, velocity))
            {
                continue;
            }

            cosmeticPositions.Add(position);
            cosmeticPrefabIndices.Add(prefabIndex);
            cosmeticVelocities.Add(velocity);
        }

        if (cosmeticPositions.Count > 0)
            SpawnGoreBurstClientRpc(cosmeticPositions.ToArray(), cosmeticPrefabIndices.ToArray(), cosmeticVelocities.ToArray());
    }

    /// <summary>
    /// Server-side spawn for a gore piece that landed inside the Trash Task's yard area.
    /// Instantiates the prefab, spawns it as a real NetworkObject (so it replicates to every
    /// client like any other <see cref="JunkItem"/>), enables its (pre-attached, disabled)
    /// <see cref="JunkItem"/> component, and registers it with <see cref="TakeOutTrashTask"/>.
    /// Rigidbody stays dynamic for as long as it counts toward the yard task — see
    /// <see cref="MonitorGoreJunkItem"/>, which only switches it to kinematic once it settles
    /// outside every yard SpawnZone (at which point it no longer counts and there's no reason
    /// to keep simulating it).
    ///
    /// Requires the gore prefab to already have a NetworkObject (registered as a Network Prefab
    /// in the NetworkManager) and a disabled <see cref="JunkItem"/> component, matching the same
    /// pre-attached-but-disabled pattern documented on <see cref="JunkItem"/> for SuspectCharacter
    /// bodies. Returns false (and destroys the instantiated piece) if either is missing, so the
    /// caller can fall back to spawning it as ordinary cosmetic debris instead.
    /// </summary>
    private bool SpawnGoreJunkItem(Vector3 position, int prefabIndex, Vector3 velocity)
    {
        if (!IsServer)
            return false;

        if (goreDropPrefabs == null || prefabIndex < 0 || prefabIndex >= goreDropPrefabs.Length)
            return false;

        GameObject prefab = goreDropPrefabs[prefabIndex];
        if (prefab == null)
            return false;

        GameObject piece = Instantiate(prefab, position, UnityEngine.Random.rotation);
        NetworkObject netObj = piece.GetComponent<NetworkObject>();
        JunkItem junk = piece.GetComponent<JunkItem>();

        if (netObj == null || junk == null)
        {
            Debug.LogWarning("[MutantEnemy] Gore prefab landed in the yard but is missing a NetworkObject " +
                              "and/or a disabled JunkItem component — it must have both to count toward the " +
                              "Trash Task. Falling back to cosmetic debris instead.");
            Destroy(piece);
            return false;
        }

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null)
            rb = piece.AddComponent<Rigidbody>();

        rb.linearVelocity = velocity;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * UnityEngine.Random.Range(2f, 6f);

        // The blood splatter for this piece is already spawned by the caller (see
        // SpawnGoreBurst's unconditional SpawnGoreBloodDecal call) — it despawns on its own the
        // next day starts, independent of whether this gore piece gets bagged up as junk.
        junk.enabled = true;
        netObj.Spawn(destroyWithScene: true);

        TakeOutTrashTask.Instance?.RegisterExternalJunkItem(netObj);

        // This piece was registered based on the position it was launched from, before physics
        // has had a chance to settle it — the pop velocity/gravity can carry it past the yard
        // boundary by the time it comes to rest. Once it settles, re-check its final position
        // and drop it from the task if it ended up outside every yard SpawnZone. Also guards
        // against it clipping through the floor and falling forever (e.g. spawned underground) —
        // if it ever drops too far below its launch height it is despawned immediately so it
        // can never soft-lock the player by being both required and unreachable.
        StartCoroutine(MonitorGoreJunkItem(netObj, rb, position.y - goreMaxFallDistance));

        return true;
    }

    /// <summary>
    /// Server-only watchdog for a networked gore <see cref="JunkItem"/> spawned by
    /// <see cref="SpawnGoreJunkItem"/>. Every frame, despawns it immediately if it has fallen
    /// below <paramref name="minY"/> (e.g. it clipped through the floor and is falling forever,
    /// out of reach — this piece is unregistered from the task first so it never soft-locks the
    /// player by staying required-but-unreachable). Otherwise, once its Rigidbody settles (or a
    /// timeout elapses), it is unregistered — but NOT despawned, remaining physically collectible
    /// as a bonus — if its final resting position ended up outside every configured yard
    /// SpawnZone; only in that case is its Rigidbody switched to kinematic (perf optimization —
    /// it's no longer required, so there's no reason to keep simulating it). A piece that
    /// settles inside the yard stays dynamic for as long as it's tracked by the task. No-op once
    /// the item is collected (despawned) before either check fires.
    /// </summary>
    private IEnumerator MonitorGoreJunkItem(NetworkObject netObj, Rigidbody rb, float minY)
    {
        const float settleTimeout = 5f;
        float elapsed = 0f;
        bool settled = false;

        while (true)
        {
            if (netObj == null || !netObj.IsSpawned)
                yield break;

            if (netObj.transform.position.y < minY)
            {
                TakeOutTrashTask.Instance?.UnregisterExternalJunkItem(netObj);
                netObj.Despawn(destroy: true);
                yield break;
            }

            if (!settled)
            {
                if (rb == null || rb.IsSleeping() || elapsed >= settleTimeout)
                    settled = true;
                else
                    elapsed += Time.deltaTime;
            }

            if (settled)
                break;

            yield return null;
        }

        if (netObj == null || !netObj.IsSpawned)
            yield break;

        if (TakeOutTrashTask.Instance != null && !TakeOutTrashTask.Instance.IsPositionInYard(netObj.transform.position))
        {
            TakeOutTrashTask.Instance.UnregisterExternalJunkItem(netObj);

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }
    }

    /// <summary>
    /// Server-side spawn of a networked blood-decal splatter under a gore piece the instant it's
    /// dropped, raycast downward from just above <paramref name="originPosition"/> to find the
    /// ground. Mirrors <see cref="SpawnGoreJunkItem"/>'s yard-bounds rule for the gore piece
    /// itself: a splatter that lands inside the Trash Task's yard is registered with
    /// <see cref="CleanBloodTask"/> via <see cref="CleanBloodTask.RegisterBloodSplatter"/> so it
    /// counts toward the post-breach clean-up objective and blocks clock-out until scrubbed;
    /// a splatter outside the yard (e.g. from a checkpoint breach fight) is purely cosmetic and
    /// registered via <see cref="CleanBloodTask.RegisterTransientBloodSplatter"/> instead, so it
    /// never blocks clock-out and just despawns the next time a day starts. Called for every gore
    /// piece in a death burst (see <see cref="SpawnGoreBurst"/>). No-op when
    /// <see cref="yardBloodDecalPrefabs"/> is empty.
    /// </summary>
    private void SpawnGoreBloodDecal(Vector3 originPosition)
    {
        if (yardBloodDecalPrefabs == null || yardBloodDecalPrefabs.Length == 0)
            return;

        Vector3 groundPoint = originPosition;
        Vector3 groundNormal = Vector3.up;

        Vector3 castOrigin = originPosition + Vector3.up * 5f;
        if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 20f, goreGroundLayer, QueryTriggerInteraction.Ignore))
        {
            groundPoint = hit.point;
            groundNormal = hit.normal;
        }

        GameObject prefab = yardBloodDecalPrefabs[UnityEngine.Random.Range(0, yardBloodDecalPrefabs.Length)];
        if (prefab == null)
            return;

        // TODO: BloodDecalUtility.GetGroundDecalRotation(groundNormal) was producing incorrect
        // orientations on landing; forcing identity rotation for now until that's fixed.
        Quaternion rotation = Quaternion.identity;
        GameObject decalGo = Instantiate(prefab, groundPoint, rotation);
        NetworkObject decalNetObj = decalGo.GetComponent<NetworkObject>();

        if (decalNetObj == null)
        {
            Debug.LogWarning("[MutantEnemy] yardBloodDecalPrefabs entry is missing a NetworkObject component — skipping blood splatter registration.");
            Destroy(decalGo);
            return;
        }

        decalNetObj.Spawn(destroyWithScene: true);

        // Only count this splatter toward the mop task if it landed inside the yard — mirrors
        // SpawnGoreJunkItem's bounds check for the gore piece it belongs to. Splatters outside
        // the yard (e.g. a checkpoint breach fight) are cosmetic-only and just despawn on their
        // own the next day, same as out-of-yard gore never becoming a collectible JunkItem.
        if (TakeOutTrashTask.Instance != null && TakeOutTrashTask.Instance.IsPositionInYard(originPosition))
            CleanBloodTask.Instance?.RegisterBloodSplatter(decalNetObj);
        else
            CleanBloodTask.Instance?.RegisterTransientBloodSplatter(decalNetObj);

        SpawnBloodParticleClientRpc(groundPoint, rotation);
    }

    /// <summary>
    /// Spawns <see cref="bloodParticlePrefab"/> on every client at the same position/rotation as
    /// a just-spawned yard blood-splatter decal (see <see cref="SpawnGoreBloodDecal"/>), so the
    /// cosmetic spray effect appears everywhere the networked decal does. No-op when
    /// <see cref="bloodParticlePrefab"/> is unassigned.
    /// </summary>
    [ClientRpc]
    private void SpawnBloodParticleClientRpc(Vector3 position, Quaternion rotation)
    {
        BloodDecalUtility.SpawnAlignedParticle(bloodParticlePrefab, position, rotation, bloodParticleLifetime);
    }

    /// <summary>
    /// Picks a random point on the surface of <see cref="goreCollider"/>. Falls back to this
    /// mutant's position when no collider is assigned.
    /// </summary>
    private Vector3 GetRandomGoreSpawnPosition()
    {
        if (goreCollider == null)
            return transform.position;

        Bounds bounds = goreCollider.bounds;
        Vector3 outsidePoint = bounds.center + Vector3.Scale(UnityEngine.Random.onUnitSphere, bounds.extents) * 2f;
        return goreCollider.ClosestPoint(outsidePoint);
    }

    /// <summary>
    /// Builds a velocity vector pointing outward from this mutant's body (with a bit of upward
    /// bias and random jitter), scaled to the given speed — used to make gore pieces look like
    /// they're popping out of the mutant rather than just falling in place. The upward bias is
    /// re-clamped AFTER jitter is applied so a piece can never end up launched downward — that
    /// previously let some pieces punch straight into the floor and fall through it forever.
    /// </summary>
    private Vector3 GetRandomPopVelocity(Vector3 spawnPosition, float speed)
    {
        Vector3 direction = spawnPosition - transform.position;
        direction.y = Mathf.Max(direction.y, 0.3f);

        if (direction.sqrMagnitude < 0.0001f)
            direction = UnityEngine.Random.onUnitSphere;

        direction = direction.normalized + UnityEngine.Random.insideUnitSphere * 0.5f;

        // Random jitter above can drag the y component back down (even negative) despite the
        // upward bias already applied — re-clamp it so every gore piece always pops at least
        // partly upward, never straight down into the floor.
        direction.y = Mathf.Max(direction.y, 0.3f);

        return direction.normalized * speed;
    }

    /// <summary>
    /// Disables every Collider on this mutant (and its children) so the corpse stops blocking
    /// navigation, physics, and weapon hit detection after death.
    /// </summary>
    private void DisableColliders()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            // Excluded so a persisting corpse (PlayAnimation death behaviour) can still be
            // interacted with/collected as junk after death — see EnableCorpseJunkPickup.
            if (col == corpseJunkInteractionCollider)
                continue;

            col.enabled = false;
        }
    }

    [ClientRpc]
    private void DisableCollidersClientRpc()
    {
        DisableColliders();
    }

    /// <summary>
    /// Disables every <see cref="LegsAnimator"/> on this mutant (and its children) so procedural
    /// leg IK stops driving the rig once the corpse should go limp/play its death animation.
    /// </summary>
    private void DisableLegsAnimators()
    {
        foreach (LegsAnimator legsAnimator in GetComponentsInChildren<LegsAnimator>(true))
        {
            legsAnimator.enabled = false;
        }
    }

    [ClientRpc]
    private void DisableLegsAnimatorsClientRpc()
    {
        DisableLegsAnimators();
    }

    /// <summary>
    /// Disables this mutant's <see cref="lookAnimator"/> on death so it stops procedurally
    /// turning the head/spine toward the (now irrelevant) chase target on a corpse.
    /// </summary>
    private void DisableLookAnimator()
    {
        if (lookAnimator != null)
            lookAnimator.enabled = false;
    }

    [ClientRpc]
    private void DisableLookAnimatorClientRpc()
    {
        DisableLookAnimator();
    }

    /// <summary>
    /// Switches this mutant's ragdoll on (rigidbodies dynamic, ragdoll colliders enabled) and
    /// disables its Animator, via the assigned <see cref="ragdollController"/>. No-op if none
    /// is assigned. See <see cref="RagdollController.EnableRagdoll"/>.
    /// </summary>
    private void EnableRagdoll()
    {
        if (ragdollController != null)
            ragdollController.ActivateRagdoll();
    }

    [ClientRpc]
    private void EnableRagdollClientRpc()
    {
        EnableRagdoll();
    }

    [ClientRpc]
    private void SpawnGoreBurstClientRpc(Vector3[] positions, int[] prefabIndices, Vector3[] velocities)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            SpawnGorePiece(positions[i], prefabIndices[i], velocities[i]);
        }
    }

    private void SpawnGorePiece(Vector3 position, int prefabIndex, Vector3 velocity)
    {
        if (goreDropPrefabs == null || prefabIndex < 0 || prefabIndex >= goreDropPrefabs.Length)
            return;

        GameObject prefab = goreDropPrefabs[prefabIndex];
        if (prefab == null)
            return;

        GameObject piece = Instantiate(prefab, position, UnityEngine.Random.rotation);

        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null)
            rb = piece.AddComponent<Rigidbody>();

        rb.linearVelocity = velocity;
        rb.angularVelocity = UnityEngine.Random.insideUnitSphere * UnityEngine.Random.Range(2f, 6f);

        AttachGoreLandingDecalSpawner(piece);

        GoreFallSafety fallSafety = piece.AddComponent<GoreFallSafety>();
        fallSafety.Initialize(position.y - goreMaxFallDistance);

        GoreKinematicSettler kinematicSettler = piece.AddComponent<GoreKinematicSettler>();
        kinematicSettler.Initialize(rb, goreKinematicDelay);

        Destroy(piece, goreLifetime);
    }

    /// <summary>
    /// Adds a <see cref="GoreLandingDecalSpawner"/> to a physics-driven gore piece so a blood
    /// decal (and landing "splat" sound) appears where it first lands. No-op when both
    /// <see cref="bloodDecalPrefabs"/> and <see cref="goreLandingSound"/> are unassigned.
    /// </summary>
    private void AttachGoreLandingDecalSpawner(GameObject piece)
    {
        bool hasDecals = bloodDecalPrefabs != null && bloodDecalPrefabs.Length > 0;
        if (!hasDecals && goreLandingSound == null)
            return;

        GoreLandingDecalSpawner spawner = piece.AddComponent<GoreLandingDecalSpawner>();
        spawner.Initialize(bloodDecalPrefabs, goreGroundLayer, bloodDecalLifetime, bloodParticlePrefab, bloodParticleLifetime, goreLandingSound);
    }

    [ClientRpc]
    private void SpawnHitParticleClientRpc(Vector3 position)
    {
        if (hitParticlePrefab == null)
            return;

        GameObject fx = Instantiate(hitParticlePrefab, position, Quaternion.identity);

        if (fx.GetComponentInChildren<AutoDestroy>() == null)
            fx.AddComponent<AutoDestroy>();
    }

    /// <summary>
    /// Resolves the killing blow. Fire damage (<paramref name="killedByFire"/>) always results in a
    /// permanent death, even when <see cref="fleeInsteadOfDie"/> is set — otherwise a
    /// fleeInsteadOfDie unit flees and survives to be re-encountered later.
    /// </summary>
    private void Die(bool killedByFire)
    {
        _isDead = true;
        _agent.ResetPath();
        _agent.enabled = false;
        _networkSpeed.Value = 0f;

        bool permanentDeath = !fleeInsteadOfDie || killedByFire;
        DiedPermanently = permanentDeath;

        if (!permanentDeath)
        {
            // Restore health so IsDead stays true (flee path) but the unit remains functional
            // long enough to run the flee coroutine. _isDead prevents re-entry from TakeDamage.
            _agent.enabled = true;
            StartCoroutine(FleeAndDespawn());
            return;
        }

        // Stop chase music on all clients before the death sequence plays.
        StopChaseMusicClientRpc();

        // Notify any scripted task systems (e.g. KillMutantTask) that this enemy died.
        OnAnyMutantKilled?.Invoke();
        OnRemovedFromPlay?.Invoke();

        // Attempt to drop a MutantBit if the night phase is active.
        MutantThreat.Instance?.TryDropBitAt(transform.position);

        // Pop a burst of gore pieces out of the mutant's body on a permanent kill.
        SpawnDeathGoreBurst();

        // Disable this mutant's colliders on death so the corpse no longer blocks movement,
        // navigation, or weapon hits. Applied locally (server) and broadcast to all clients.
        DisableColliders();
        DisableCollidersClientRpc();

        // Disable this mutant's leg animator(s) on death so procedural leg IK stops fighting
        // the death pose/ragdoll. Applied locally (server) and broadcast to all clients.
        DisableLegsAnimators();
        DisableLegsAnimatorsClientRpc();

        // Disable this mutant's FLookAnimator on death so it stops turning its head/spine
        // toward the chase target once dead. Applied locally (server) and broadcast to all
        // clients.
        DisableLookAnimator();
        DisableLookAnimatorClientRpc();

        // Enable ragdoll physics (and disable the Animator driving the rig) on death so the
        // corpse falls naturally instead of playing a canned death animation. Applied locally
        // (server) and broadcast to all clients. Runs after DisableColliders() above so the
        // ragdoll's own colliders (disabled by that blanket pass) end up enabled again.
        EnableRagdoll();
        EnableRagdollClientRpc();

        if (deathBehaviour == DeathBehaviour.PlayAnimation)
        {
            // The corpse persists (never despawned — see DespawnAfterDelay comment below),
            // so let it be collected as junk, matching what happens to the gore it dropped.
            EnableCorpseJunkPickup();

            TriggerDeathAnimationClientRpc();
            //StartCoroutine(DespawnAfterDelay(deathAnimationDuration));
        }
        else
        {
            PlayDeathSoundClientRpc();

            if (IsSpawned)
                NetworkObject.Despawn();
        }
    }

    /// <summary>
    /// Enables this corpse as a collectible <see cref="JunkItem"/> so it can always be picked up
    /// by a player holding a TrashBag. Called on a permanent death when <see cref="deathBehaviour"/>
    /// is <see cref="DeathBehaviour.PlayAnimation"/> (the corpse persists in the scene instead of
    /// despawning immediately). No-op if <see cref="corpseJunkItem"/> isn't assigned. Mirrors
    /// <c>SuspectCharacter.EnableJunkPickup</c>/<c>ApplyJunkPickupState</c> for a body that becomes
    /// pickable trash once its owner is confirmed dead.
    ///
    /// Only enabled — and therefore only interactable/highlightable with the reticle at all —
    /// when this corpse died inside one of the task's yard <see cref="SpawnZone"/>s (mirroring
    /// <see cref="SpawnGoreJunkItem"/>'s same in-yard check for gore pieces), since only those
    /// corpses register with <see cref="TakeOutTrashTask"/> and count toward the trash/gore task
    /// total and <see cref="CheckpointIntegrityService"/>'s score. A body that dies outside the
    /// yard (e.g. a breach mutant killed mid-chase away from the trash zones) doesn't count
    /// toward the task or score, so its <see cref="JunkItem"/> component is left disabled and it
    /// stays a non-interactable corpse.
    /// </summary>
    private void EnableCorpseJunkPickup()
    {
        if (corpseJunkItem == null)
            return;

        StartCoroutine(EnableCorpseJunkPickupAfterSettle());
    }

    /// <summary>
    /// Waits for the just-activated ragdoll to come to rest (or a timeout) before deciding
    /// whether this corpse counts toward the Trash Task, then judges it by
    /// <see cref="corpseJunkItem"/>'s own settled position rather than the mutant root's
    /// <c>transform.position</c> — called the instant ragdoll physics is enabled (before
    /// Physics has even simulated a single step), the root can end up far from where the body
    /// actually flops to rest, especially for a mutant that dies straddling the yard boundary.
    /// Checking the stale root position let a corpse get counted (and reticle-highlighted as
    /// required junk) while its real, settled interaction collider ended up outside the yard —
    /// and out of the task's actual reach — which could soft-lock the "take out the gore" task on
    /// an uncollectible-but-required item. Using the same settled position for both the
    /// interactability check and the yard-count registration keeps the two always in agreement.
    /// </summary>
    private IEnumerator EnableCorpseJunkPickupAfterSettle()
    {
        const float settleTimeout = 5f;
        float elapsed = 0f;

        Rigidbody corpseRb = corpseJunkItem.GetComponent<Rigidbody>();
        if (corpseRb == null)
            corpseRb = corpseJunkItem.GetComponentInParent<Rigidbody>();

        while (corpseRb != null && !corpseRb.IsSleeping() && elapsed < settleTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Outside the yard, this corpse never counts toward the Trash Task, so leave the
        // JunkItem disabled entirely rather than enabling it as uncounted, still-interactable
        // junk — this keeps it out of reticle highlighting/interaction.
        if (TakeOutTrashTask.Instance == null || !TakeOutTrashTask.Instance.IsPositionInYard(corpseJunkItem.transform.position))
            yield break;

        // Apply immediately on the server so TakeOutTrashTask's FindObjectsByType scan (run
        // from RegisterExternalJunkItem's dynamic activation, or a later TriggerTask/
        // ActivateForExistingItems) counts this corpse as a pre-existing JunkItem right away.
        ApplyCorpseJunkPickupState();
        EnableCorpseJunkPickupClientRpc();

        TakeOutTrashTask.Instance.RegisterExternalJunkItem(NetworkObject);
    }

    [ClientRpc]
    private void EnableCorpseJunkPickupClientRpc()
    {
        if (IsServer) return; // already applied on the server above
        ApplyCorpseJunkPickupState();
    }

    private void ApplyCorpseJunkPickupState()
    {
        corpseJunkItem.enabled = true;

        if (corpseJunkInteractionCollider != null)
            corpseJunkInteractionCollider.enabled = true;
    }

    /// <summary>
    /// Flee-and-despawn sequence for fully-mutated civilian mutants that cannot be permanently
    /// killed. The mutant breaks off from its current target, sprints away from the nearest
    /// player at <see cref="fleeSpeed"/>, and despawns after <see cref="fleeDespawnTimeout"/>
    /// seconds regardless of distance. No kill event is fired and no MutantBit is dropped.
    /// </summary>
    private IEnumerator FleeAndDespawn()
    {
        if (!IsServer) yield break;

        DiedPermanently = false;

        // Boost speed, stop any attack animation, and stop chase music on all clients.
        _agent.speed = fleeSpeed;
        SetAttackAnimClientRpc(false);
        SetFleeingClientRpc(true);
        StopChaseMusicClientRpc();

        // Fire immediately — before the despawn timeout — so listeners can react to the flee
        // itself (e.g. ending a scripted finale encounter) without waiting for the mutant to
        // actually leave the scene.
        OnFleeStarted?.Invoke();

        float elapsed = 0f;

        while (elapsed < fleeDespawnTimeout)
        {
            // Continuously update destination away from the nearest player.
            Transform player = FindNearestLivingPlayer();
            if (player != null)
            {
                Vector3 awayDir = (transform.position - player.position).normalized;
                Vector3 fleeTarget = transform.position + awayDir * 20f;

                // Clamp to NavMesh surface.
                if (UnityEngine.AI.NavMesh.SamplePosition(fleeTarget, out UnityEngine.AI.NavMeshHit hit, 15f, UnityEngine.AI.NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }

            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        SetFleeingClientRpc(false);
        OnRemovedFromPlay?.Invoke();

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    [ClientRpc]
    private void SetAttackAnimClientRpc(bool attacking)
    {
        if (animator != null && !string.IsNullOrEmpty(attackBoolName))
            animator.SetBool(attackBoolName, attacking);
    }

    [ClientRpc]
    private void SetFleeingClientRpc(bool fleeing)
    {
        // Reuse the Speed parameter — the animator reads it for locomotion blend.
        // Optionally, set a dedicated "Fleeing" bool if the animator has one.
        if (animator != null)
            animator.SetBool("Fleeing", fleeing);
    }

    [ClientRpc]
    private void TriggerDeathAnimationClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(deathBoolName))
            animator.SetBool(deathBoolName, true);

        if (deathSound != null)
            SFXController.Instance.Play(deathSound);
    }

    [ClientRpc]
    private void PlayDeathSoundClientRpc()
    {
        if (deathSound != null)
            SFXController.Instance.Play(deathSound);
    }

    [ClientRpc]
    private void PlayHurtSoundClientRpc(int index)
    {
        if (_hurtSounds == null || index < 0 || index >= _hurtSounds.Length) return;
        AudioClip clip = _hurtSounds[index];
        if (clip != null)
            SFXController.Instance?.PlayAtPosition(clip, transform.position);
    }

    [ClientRpc]
    private void PlayChaseSoundClientRpc(int index)
    {
        if (_chaseSounds == null || index < 0 || index >= _chaseSounds.Length) return;
        AudioClip clip = _chaseSounds[index];
        if (clip != null)
            SFXController.Instance?.PlayAtPosition(clip, transform.position);
    }

    [ClientRpc]
    private void PlayFootstepClientRpc(bool outside, int index)
    {
        AudioClip[] clips = outside ? _outsideFootstepClips : _insideFootstepClips;
        if (clips == null || index < 0 || index >= clips.Length) return;
        AudioClip clip = clips[index];
        if (clip == null) return;

        float pitch = 1f + UnityEngine.Random.Range(-_footstepPitchRandomness, _footstepPitchRandomness);
        SFXController.Instance?.PlayAtPosition(clip, transform.position, 1f, pitch);
    }

    /// <summary>Fades out and stops the looping chase music on all clients.</summary>
    [ClientRpc]
    private void StopChaseMusicClientRpc()
    {
        MusicManager.Instance?.FadeOut(_chaseMusicFadeOutSeconds);
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (IsSpawned)
            NetworkObject.Despawn();
    }

    // ── Animation Sync ─────────────────────────────────────────────────────────

    private void OnNetworkSpeedChanged(float oldValue, float newValue)
    {
        ApplyAnimatorSpeed(newValue);
    }

    private void ApplyAnimatorSpeed(float speed)
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(speedParameterName))
            animator.SetFloat(speedParameterName, speed);

        if (!string.IsNullOrEmpty(runningBoolName))
            animator.SetBool(runningBoolName, speed > 0.1f);
    }

    private void OnNetworkGroundedChanged(bool oldValue, bool newValue)
    {
        ApplyAnimatorGrounded(newValue);
    }

    private void ApplyAnimatorGrounded(bool grounded)
    {
        if (animator != null && !string.IsNullOrEmpty(groundedParameterName))
            animator.SetBool(groundedParameterName, grounded);
    }

    // ── Look At Sync ───────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Re-evaluates whether the mutant should aim its <see cref="lookAnimator"/> at the
    /// player it is currently chasing, and syncs the result to clients via <see cref="_networkLookTargetId"/>.
    /// Called every frame from <see cref="Update"/>, which already early-returns on non-server instances.
    /// </summary>
    private void UpdateLookAtTarget()
    {
        if (lookAnimator == null) return;

        ulong newLookTargetId = 0;

        if (_currentTarget != null
            && Vector3.Distance(transform.position, _currentTarget.position) <= lookAtRange)
        {
            NetworkObject targetNetObj = _currentTarget.GetComponent<NetworkObject>();
            if (targetNetObj != null)
                newLookTargetId = targetNetObj.NetworkObjectId;
        }

        if (_networkLookTargetId.Value != newLookTargetId)
            _networkLookTargetId.Value = newLookTargetId;
    }

    private void OnNetworkLookTargetChanged(ulong oldValue, ulong newValue)
    {
        ApplyLookTarget(newValue);
    }

    /// <summary>
    /// Resolves a synced NetworkObjectId into a local Transform and assigns/clears it on the
    /// look animator. Runs on every client (including the server) so head-tracking is visible
    /// consistently regardless of who is watching.
    /// </summary>
    private void ApplyLookTarget(ulong targetId)
    {
        if (lookAnimator == null) return;

        if (targetId == 0)
        {
            if (lookAnimator.ObjectToFollow != null)
                lookAnimator.SetLookTarget(null);
            return;
        }

        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObj)
            && targetObj != null)
        {
            if (lookAnimator.ObjectToFollow != targetObj.transform)
                lookAnimator.SetLookTarget(targetObj.transform);
        }
    }
}
