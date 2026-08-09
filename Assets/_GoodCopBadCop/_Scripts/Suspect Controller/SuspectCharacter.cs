using GoodCopBadCop.Effects;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using FIMSpace.FLook;
using FIMSpace.FProceduralAnimation;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;


public class SuspectCharacter : Interactable
{
    [Header("Suspect Data")] [SerializeField]
    private SuspectData suspectData;

    public SuspectData Data => suspectData;
    public string ExpirationDate => suspectData.EntryPermitExpiryDate;

    [Header("Suspect State")] public int InfectionScore;

    [Header("Suspect Set Up")] public FLookAnimator lookAnimator;
    public Animator animator;
    public AudioSource audioSource;
    [SerializeField] private SpeakingInteraction speaking;

    /// <summary>
    /// At runtime, returns the replacement face photo when this suspect has been initialized
    /// as a replacement. For normal suspects, SuspectData is the source of truth.
    /// </summary>
    public Texture2D IDPhoto => _isReplacement && suspectData != null && suspectData.replacementIDPhoto != null
        ? suspectData.replacementIDPhoto
        : suspectData != null ? suspectData.IDPhoto : null;

    /// <summary>True when this character has been spawned as an uncanny replacement of a killed suspect.</summary>
    public bool IsReplacement => _isReplacement;
    private bool _isReplacement;
    [SerializeField] Collider interactionCollider;

    [Tooltip("When assigned, direct interaction (LMB / E) opens a simple 3-choice world " +
             "conversation instead of the no-op fallthrough below. Used for scene-placed " +
             "suspects that are talked to directly rather than through the booth/interrogation " +
             "flow (e.g. the Day 1 Suspect_Soldier). Leave null for normal booth suspects.")]
    [SerializeField] private SuspectWorldDialogue worldDialogue;

    /// <summary>
    /// Assigns the <see cref="SuspectWorldDialogue"/> used by direct interaction. Used by scripted
    /// sequences that dynamically attach a world dialogue conversation to a runtime-spawned
    /// suspect (e.g. Day_02's yard hand-off) once their scripted task is complete.
    /// </summary>
    public void SetWorldDialogue(SuspectWorldDialogue dialogue) => worldDialogue = dialogue;
    [SerializeField] private GameObject bloodExplosion;
    public Transform lookPos;
    public Vector3 standPosOffset;
    public bool attackImmediately;
    [SerializeField] private ParticleSystem[] vomitParticles;
    SuspectRecordViewer suspectRecordViewer;


    #region Folder

    public enum FolderGivingAnimation
    {
        HandOver,
        Throw
    }

    [System.Serializable]
    public struct FolderGivingAnimationData
    {
        public FolderGivingAnimation animation;
        public string animationTriggerName;
    }

    [SerializeField] private FolderGivingAnimationData[] folderGivingAnimationDatas;
    [SerializeField] private FolderGivingAnimation _folderGivingAnimation = FolderGivingAnimation.HandOver;
    private FolderGivingAnimationData _folderGivingAnimationData;
    [SerializeField] private Transform handSpawnPos;

    #endregion

    [Header("Full Mutant Form")]
    [Tooltip("The 'Base Version' container GameObject — the entire civilian mesh and its child hierarchy. " +
             "Assign the direct child named 'Base Version'. Disabled when the fully-mutated form activates.")]
    [SerializeField] private GameObject _baseVersion;

    [Tooltip("The 'Mutated Version' container GameObject — the mutated mesh and its child hierarchy. " +
             "Assign the direct child named 'Mutated Version'. Enabled when the fully-mutated form activates. " +
             "Keep this GameObject disabled in the prefab.")]
    [SerializeField] private GameObject _mutatedVersion;

    [Tooltip("The MutantEnemy component on this prefab. Assign in the Inspector; falls back to " +
             "GetComponent at runtime. Kept disabled until BeginMutantBehavior() fires after the booth cutscene.")]
    [SerializeField] private MutantEnemy _mutantEnemy;

    [Tooltip("The MutantSuspectBehaviour component on this prefab. When assigned, BeginMutantBehavior() " +
             "hands off to this component for the window-breach sequence (climb-through or shutter bang) " +
             "instead of enabling MutantEnemy directly. Assign in the Inspector; falls back to GetComponent.")]
    [SerializeField] private MutantSuspectBehaviour _mutantSuspectBehaviour;

    [Tooltip("MutantIntruderData config used for the booth window-breach phase after the full-mutant " +
             "cutscene. Controls walk, climb, and shutter-bang timings. Assign a MutantIntruderData asset.")]
    [SerializeField] private MutantIntruderData _fullMutantIntruderData;

    [Tooltip("The SetOnFire component on this prefab. Assign in the Inspector; falls back to " +
             "GetComponent at runtime. Re-targeted to the Mutated Version's Animator whenever the " +
             "full-mutant form activates, so flamethrower fire spawns on the correct skeleton — " +
             "burning to death is the only way to permanently kill a fully-mutated resident.")]
    [SerializeField] private SetOnFire _setOnFire;

    /// <summary>
    /// The <see cref="NetworkAnimator"/> that syncs the Base Version's <see cref="Animator"/>. Cached on
    /// first use via <see cref="GetComponent{T}"/> on this root GameObject. Disabled whenever the
    /// Mutated Version's own <see cref="NetworkAnimator"/> takes over, so only one NetworkAnimator is
    /// ever actively replicating state for this suspect at a time.
    /// </summary>
    private Unity.Netcode.Components.NetworkAnimator _baseNetworkAnimator;

    /// <summary>
    /// The <see cref="NetworkAnimator"/> that syncs the Mutated Version's <see cref="Animator"/>. Found
    /// inside <see cref="_mutatedVersion"/> the first time <see cref="AssignMutatedAnimator"/> runs.
    /// </summary>
    private Unity.Netcode.Components.NetworkAnimator _mutantNetworkAnimator;

    // Booth references injected by SuspectController at spawn time for the window-breach phase.
    private Transform _fullMutantStandPos;
    private Transform _fullMutantDespawnPos;
    private Transform _fullMutantClimbTargetPos;
    private ShutterController _fullMutantShutterController;
    private SuspectController _fullMutantController;

    /// <summary>True when a mutated version container is configured on this prefab.</summary>
    public bool HasFullMutantForm => _mutatedVersion != null;

    /// <summary>
    /// Switches this suspect to their full-mutant visual form on the server and replicates to all clients.
    /// Disables the Base Version container, enables the Mutated Version container, dynamically
    /// re-assigns the <see cref="animator"/> field (and related components) to the mutated mesh's Animator,
    /// and ensures <see cref="MutantEnemy"/> is disabled so <see cref="SuspectCharacter"/> stays in control
    /// until the booth cutscene completes.
    /// Must be called on the server.
    /// </summary>
    public void ActivateFullMutantForm()
    {
        if (_baseVersion != null) _baseVersion.SetActive(false);
        if (_mutatedVersion != null) _mutatedVersion.SetActive(true);

        // Disable MutantEnemy before AssignMutatedAnimator so any exception inside
        // AssignMutatedAnimator cannot leave it in an active state.
        // NOTE: MutantEnemy's own Unity 'enabled' flag is never toggled anymore (it stays
        // permanently true) — dormancy is tracked by MutantEnemy's internal _isActive
        // NetworkVariable instead, which InitialiseServer() sets. It already defaults to
        // false, so there is nothing to reset here before InitialiseServer() runs.

        AssignMutatedAnimator();
        ActivateFullMutantFormClientRpc();
    }

    /// <summary>
    /// Finds the Animator inside the active mutated version and assigns it to all animator-consuming
    /// components on this GameObject. Also remaps the <see cref="FLookAnimator"/> bone chain to the
    /// mutant skeleton and re-initializes it so look-at tracking follows the mutant head.
    /// Called locally on both server and clients.
    /// </summary>
    private void AssignMutatedAnimator()
    {
        if (_mutatedVersion == null) return;

        Animator mutantAnim = _mutatedVersion.GetComponentInChildren<Animator>(true);
        if (mutantAnim == null)
        {
            Debug.LogWarning($"[SuspectCharacter] '{name}' Mutated Version has no Animator in its hierarchy — cannot reassign.", this);
            return;
        }

        animator = mutantAnim;

        var adapter = GetComponent<GoodCopBadCop.SuspectBehaviorAnimation.SuspectBehaviorAnimationAdapter>();
        adapter?.UpdateAnimatorReference(mutantAnim);

        _mutantEnemy?.SetAnimator(mutantAnim);
        _mutantSuspectBehaviour?.SetAnimator(mutantAnim);
        _setOnFire?.SetAnimator(mutantAnim);

        // Hand NetworkAnimator authority over to the mutant mesh's own NetworkAnimator (added
        // to the Mutated Version's Animator GameObject) and disable the Base Version's, so only
        // one NetworkAnimator ever replicates state for this suspect at a time. Each version's
        // Animator lives on a different GameObject/mesh, and NetworkAnimator caches its target
        // Animator at spawn — it cannot be retargeted onto a different Animator instance at
        // runtime, so each version needs (and already has, per-prefab) its own NetworkAnimator.
        _baseNetworkAnimator ??= GetComponent<Unity.Netcode.Components.NetworkAnimator>();
        _mutantNetworkAnimator ??= _mutatedVersion.GetComponentInChildren<Unity.Netcode.Components.NetworkAnimator>(true);

        if (_baseNetworkAnimator != null) _baseNetworkAnimator.enabled = false;
        if (_mutantNetworkAnimator != null) _mutantNetworkAnimator.enabled = true;
        else Debug.LogWarning($"[SuspectCharacter] '{name}' Mutated Version has no NetworkAnimator — mutant animation state will not replicate over the network.", this);

        // Remap the FLookAnimator bone chain to the mutant skeleton so look-at tracking
        // follows the mutant head instead of the now-disabled civilian bones.
        RemapLookAnimatorToMutantSkeleton();
    }

    /// <summary>
    /// Builds a name-keyed lookup of every Transform inside <see cref="_mutatedVersion"/>,
    /// then replaces each <see cref="FLookAnimator.LookBones"/> Transform reference and
    /// <see cref="FLookAnimator.LeadBone"/> with the matching mutant bone.
    /// Finally calls <see cref="FLookAnimator.InitializeBaseVariables"/> so all internal
    /// rotation correction data is recalculated for the new skeleton.
    /// </summary>
    private void RemapLookAnimatorToMutantSkeleton()
    {
        if (lookAnimator == null || _mutatedVersion == null) return;

        // Index every bone in the mutant hierarchy by name for O(1) matching.
        var mutantBones = new Dictionary<string, Transform>();
        foreach (Transform t in _mutatedVersion.GetComponentsInChildren<Transform>(true))
        {
            if (!mutantBones.ContainsKey(t.name))
                mutantBones[t.name] = t;
        }

        // Swap each LookBone Transform to its mutant counterpart (same Mixamo bone name).
        foreach (FLookAnimator.LookBone lookBone in lookAnimator.LookBones)
        {
            if (lookBone.Transform != null && mutantBones.TryGetValue(lookBone.Transform.name, out Transform mutantBone))
                lookBone.Transform = mutantBone;
        }

        // Reassign LeadBone (the primary head bone driven by look rotation).
        if (lookAnimator.LeadBone != null && mutantBones.TryGetValue(lookAnimator.LeadBone.name, out Transform mutantLeadBone))
        {
            lookAnimator.LeadBone = mutantLeadBone;
        }
        else
        {
            // LeadBone was null or its name wasn't found — fall back to Humanoid head bone.
            Transform fallback = animator.GetBoneTransform(HumanBodyBones.Head);
            if (fallback != null)
                lookAnimator.LeadBone = fallback;
            else
                Debug.LogWarning($"[SuspectCharacter] '{name}' could not find a mutant LeadBone by name or HumanBodyBones.Head — FLookAnimator LeadBone not updated.", this);
        }

        // Recalculate internal bone-direction correction data for the new skeleton.
        if (lookAnimator.LeadBone != null)
        {
            try
            {
                lookAnimator.InitializeBaseVariables();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SuspectCharacter] '{name}' FLookAnimator.InitializeBaseVariables threw after mutant bone remap — look-at may be miscalibrated but spawn will continue. ({e.GetType().Name}: {e.Message})", this);
            }
        }
    }

    [ClientRpc]
    private void ActivateFullMutantFormClientRpc()
    {
        if (IsServer) return;
        if (_baseVersion != null) _baseVersion.SetActive(false);
        if (_mutatedVersion != null) _mutatedVersion.SetActive(true);
        AssignMutatedAnimator();
        // See ActivateFullMutantForm above — MutantEnemy's 'enabled' flag is never toggled.
    }

    /// <summary>
    /// Plays the fully-mutated booth cutscene for this suspect via <see cref="ScriptedDialogueRunner"/>.
    /// Mirrors the intro-dialogue pattern from <see cref="SuspectEncounterManager"/>: starts a
    /// coroutine on this component, waits one second for the character to settle at the window,
    /// then calls <see cref="ScriptedDialogueRunner.PlayDialogue"/> with
    /// <see cref="SuspectData.FullMutantConfig.boothCutscene"/> from this character's own data.
    /// <see cref="BeginMutantBehavior"/> fires automatically as the <c>onComplete</c> callback.
    /// Must be called on the server from <see cref="SuspectController"/> when the suspect arrives
    /// at the booth window.
    /// </summary>
    public void StartFullMutantCutscene()
    {
        if (!IsServer) return;
        StartCoroutine(PlayFullMutantCutsceneRoutine());
    }

    private IEnumerator PlayFullMutantCutsceneRoutine()
    {
        ScriptedDialogue cutscene = suspectData?.fullMutantDialogue;

        if (cutscene == null)
        {
            Debug.LogWarning($"[SuspectCharacter] '{name}': fullMutantDialogue is not assigned on '{suspectData?.name}' — skipping cutscene and going straight to mutant behaviour.", this);
            BeginMutantBehavior();
            yield break;
        }

        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning($"[SuspectCharacter] '{name}': ScriptedDialogueRunner.Instance is null — cannot play full-mutant cutscene.", this);
            BeginMutantBehavior();
            yield break;
        }

        // Settle beat — character finishes facing the player before the first line plays.
        // Matches the 1-second wait used by SuspectEncounterManager.PlayIntroDialogue.
        yield return new WaitForSeconds(1f);

        Debug.Log($"[SuspectCharacter] '{name}': starting full-mutant cutscene '{cutscene.name}'.");
        ScriptedDialogueRunner.Instance.PlayDialogue(this, cutscene, onComplete: BeginMutantBehavior);
    }

    /// <summary>
    /// Stores the booth references needed by <see cref="MutantSuspectBehaviour.BeginAtStandPos"/>
    /// so <see cref="BeginMutantBehavior"/> can start the window-breach sequence without
    /// reaching back into <see cref="SuspectController"/>.
    /// Must be called on the server from <see cref="SuspectController"/> immediately after
    /// <see cref="ActivateFullMutantForm"/> when spawning a full-mutant slot.
    /// </summary>
    public void SetupFullMutantWindowBreach(
        Transform standPos,
        Transform despawnPos,
        Transform climbTargetPos,
        ShutterController shutterController,
        SuspectController controller)
    {
        _fullMutantStandPos         = standPos;
        _fullMutantDespawnPos       = despawnPos;
        _fullMutantClimbTargetPos   = climbTargetPos;
        _fullMutantShutterController = shutterController;
        _fullMutantController       = controller;
    }

    /// <summary>
    /// Hands control from <see cref="SuspectCharacter"/> to the window-breach phase after the
    /// booth cutscene completes.
    /// When <see cref="_mutantSuspectBehaviour"/> and <see cref="_fullMutantIntruderData"/> are
    /// both configured, delegates to <see cref="MutantSuspectBehaviour.BeginAtStandPos"/> which
    /// drives the rotate-to-window → climb-through-or-bang sequence and enables
    /// <see cref="MutantEnemy"/> itself after a successful breakthrough.
    /// Falls back to enabling <see cref="MutantEnemy"/> directly when no
    /// <see cref="MutantSuspectBehaviour"/> is present.
    /// Called automatically as the <c>onComplete</c> of <see cref="StartFullMutantCutscene"/>.
    /// </summary>
    public void BeginMutantBehavior()
    {
        if (!IsServer) return;

        _isMutant = true;
        StopNavigation();
        _suspectUpdateDisabled = true;
        TransitionToMutantBehaviorClientRpc(enableMutantEnemy: false);
        SetMutantVoiceClientRpc(true);

        // Track how this encounter resolves (permanently killed vs. beaten-and-fled) so the
        // legacy-mutant pool in SuspectRunRecords stays in sync. Subscribed once here regardless
        // of which path below actually enables MutantEnemy, since both fire the same event.
        if (_mutantEnemy != null)
            _mutantEnemy.OnRemovedFromPlay += HandleFullMutantResolved;

        // Flag this character as having a live full-mutant instance so no second instance of the
        // same SuspectData can be spawned elsewhere (e.g. a MutantSpawner world spawn) while this
        // booth encounter is still in progress.
        SuspectRunRecords.Instance?.RegisterActiveFullMutant(suspectData);

        // Preferred path — MutantSuspectBehaviour drives the window-breach sequence and
        // calls MutantEnemy.InitialiseServer() itself after a successful climb-through.
        if (_mutantSuspectBehaviour != null && _fullMutantIntruderData != null
            && _fullMutantStandPos != null)
        {
            _mutantSuspectBehaviour.BeginAtStandPos(
                _fullMutantIntruderData,
                _fullMutantStandPos,
                _fullMutantDespawnPos,
                _fullMutantClimbTargetPos,
                _fullMutantShutterController,
                _fullMutantController);
            return;
        }

        // Fallback — no MutantSuspectBehaviour or intruder data: enable MutantEnemy directly.
        if (_mutantEnemy == null)
        {
            Debug.LogWarning($"[SuspectCharacter] '{name}' has no MutantEnemy — cannot begin mutant behaviour.", this);
            return;
        }

        _mutantEnemy.InitialiseServer();
        EnableMutantEnemyClientRpc();
    }

    [ClientRpc]
    private void EnableMutantEnemyClientRpc()
    {
        // No-op now that MutantEnemy's 'enabled' flag is never toggled — its InitialiseServer()
        // call already set its internal _isActive NetworkVariable, which replicates on its own.
        // Kept as a harmless RPC stub in case future callers still expect this hook to exist.
    }

    /// <summary>
    /// Fired once when this suspect's full-mutant encounter (booth or legacy world spawn) is
    /// resolved — either a permanent kill (<see cref="MutantEnemy.DiedPermanently"/>, which
    /// requires fire since these units have fleeInsteadOfDie enabled) or a beaten-and-fled
    /// escape. Updates <see cref="SuspectRunRecords"/> so this resident's legacy-mutant
    /// eligibility stays accurate for future <see cref="MutantSpawner"/> world spawns, and clears
    /// the active-instance flag so this character becomes spawnable again. Server-only.
    /// </summary>
    private void HandleFullMutantResolved()
    {
        if (_mutantEnemy != null)
            _mutantEnemy.OnRemovedFromPlay -= HandleFullMutantResolved;

        SuspectRunRecords runRecords = SuspectRunRecords.Instance;
        if (runRecords == null || suspectData == null) return;

        runRecords.UnregisterActiveFullMutant(suspectData);

        if (_mutantEnemy != null && _mutantEnemy.DiedPermanently)
            runRecords.ClearLegacyMutant(suspectData);
        else
            runRecords.MarkAsLegacyMutant(suspectData);
    }

    /// <summary>
    /// Spawns this suspect directly in full-mutant form as a roaming world threat, bypassing the
    /// booth cutscene and window-breach sequence entirely. Used by <see cref="MutantSpawner"/> to
    /// re-introduce a resident who previously escaped a full-mutant encounter (tracked via
    /// <see cref="SuspectRecord.isLegacyMutant"/>) as a "legacy mutant". <see cref="MutantEnemy"/>
    /// is enabled immediately with all its normal chase/attack/flee behaviour active.
    /// Must be called on the server immediately after this instance's NetworkObject is spawned.
    /// </summary>
    /// <param name="initialAggroTarget">
    /// Optional target (e.g. the player or booth) the mutant heads toward on spawn, mirroring
    /// <see cref="MutantSpawner"/>'s aggroTarget. When non-null, forces aggro so the legacy
    /// mutant is immediately hostile. When null (the normal ambient-reintroduction case), the
    /// mutant spawns with no aggro target at all and starts non-aggroed, ignoring
    /// <see cref="MutantEnemyData.aggroChance"/> entirely.
    /// </param>
    public void ActivateAsLegacyMutant(Transform initialAggroTarget)
    {
        if (!IsServer) return;

        ActivateFullMutantForm();
        _isMutant = true;
        _suspectUpdateDisabled = true;
        TransitionToMutantBehaviorClientRpc(enableMutantEnemy: false);
        SetMutantVoiceClientRpc(true);

        if (_mutantEnemy == null)
        {
            Debug.LogWarning($"[SuspectCharacter] '{name}' has no MutantEnemy — cannot activate as a legacy mutant.", this);
            return;
        }

        _mutantEnemy.OnRemovedFromPlay += HandleFullMutantResolved;

        // Flag this character as having a live full-mutant instance so no second instance of the
        // same SuspectData can be spawned elsewhere (booth or another MutantSpawner) while this
        // one is alive.
        SuspectRunRecords.Instance?.RegisterActiveFullMutant(suspectData);

        if (initialAggroTarget != null)
        {
            _mutantEnemy.SetAggroTarget(initialAggroTarget);
            _mutantEnemy.SetForceAggro(true);
        }
        _mutantEnemy.InitialiseServer();
        EnableMutantEnemyClientRpc();
    }

    /// <summary>
    /// Disables <see cref="SuspectCharacter"/>'s own Update() on all non-server clients.
    /// When <paramref name="enableMutantEnemy"/> is true, the fallback path expects
    /// <see cref="MutantEnemy"/>'s InitialiseServer()-driven _isActive NetworkVariable to have
    /// already replicated (no explicit action needed here). The preferred path lets
    /// <see cref="MutantSuspectBehaviour"/> hand off to <see cref="MutantEnemy"/> after breakthrough.
    /// </summary>
    [ClientRpc]
    private void TransitionToMutantBehaviorClientRpc(bool enableMutantEnemy)
    {
        _suspectUpdateDisabled = true;
    }

    [ClientRpc]
    private void SetMutantVoiceClientRpc(bool isMutant = true)
    {
        speaking?.SetMutantVoice(isMutant);
    }

    [ClientRpc]
    private void PlayUncannyArriveSoundClientRpc()
    {
        if (_uncannyArrivesSound != null)
            SFXController.Instance?.Play(_uncannyArrivesSound);
    }

    [Header("Sounds")]
    [Tooltip("Played on all clients when a fully-mutated suspect presents at the booth (uncanny arrival sting).")]
    [SerializeField] private AudioClip _uncannyArrivesSound;

    [Header("Cameras")]
    [Tooltip("Per-character wide-shot camera used during dialogue. When assigned, this overrides the shared " +
             "scene-level 'At Booth Cam' for this specific character. Assign a child CinemachineCamera GameObject.")]
    [SerializeField] private GameObject _suspectCam;

    [Tooltip("Per-character close face camera. Referenced by the 'SuspectFaceCam' trigger key in " +
             "ScriptedDialogueRunner. Assign a child CinemachineCamera GameObject.")]
    [SerializeField] private GameObject _suspectFaceCam;

    [Tooltip("Optional wide-shot camera used when the mutated form is active. " +
             "When assigned and the Mutated Version mesh is visible, this replaces _suspectCam. " +
             "Leave empty to keep using _suspectCam for both forms. " +
             "Assign a child CinemachineCamera inside the 'Mutated Version' container.")]
    [SerializeField] private GameObject _mutantSuspectCam;

    [Tooltip("Optional close face camera used when the mutated form is active. " +
             "When assigned and the Mutated Version mesh is visible, this replaces _suspectFaceCam " +
             "for the 'SuspectFaceCam' trigger key. Leave empty to fall back to _suspectFaceCam. " +
             "Assign a child CinemachineCamera inside the 'Mutated Version' container.")]
    [SerializeField] private GameObject _mutantFaceCam;

    /// <summary>
    /// Per-character wide-shot camera. Returns the mutant version when the mutated form is active
    /// and <see cref="_mutantSuspectCam"/> is assigned; otherwise falls back to <see cref="_suspectCam"/>.
    /// Returns null when neither cam is assigned (SuspectController uses the shared scene-level cam as fallback).
    /// </summary>
    public GameObject SuspectCam =>
        (_mutatedVersion != null && _mutatedVersion.activeSelf && _mutantSuspectCam != null)
            ? _mutantSuspectCam
            : _suspectCam;

    /// <summary>
    /// Per-character close face camera. Returns the mutant version when the mutated form is active
    /// and <see cref="_mutantFaceCam"/> is assigned; otherwise falls back to <see cref="_suspectFaceCam"/>.
    /// Use camera trigger key 'SuspectFaceCam' in a ScriptedDialogueNode to cut to this camera.
    /// </summary>
    public GameObject SuspectFaceCam =>
        (_mutatedVersion != null && _mutatedVersion.activeSelf && _mutantFaceCam != null)
            ? _mutantFaceCam
            : _suspectFaceCam;


    // Navigation

    [Header("Navigation")]
    [Tooltip("Walk speed in units per second. Used to calculate DOTween movement duration " +
             "and also applied to the NavMeshAgent when InitNavigation() is called explicitly " +
             "for special cases (e.g. MutantSuspectBehaviour retreat pathfinding).")]
    [SerializeField] private float _walkSpeed = 1.5f;

    [Tooltip("Rotation speed (degrees/second) used when the NavMeshAgent is active for special cases.")]
    [SerializeField] private float _angularSpeed = 240f;

    [Tooltip("Stopping distance applied to the NavMeshAgent when it is explicitly enabled for special cases.")]
    [SerializeField] private float _stoppingDistance = 0.1f;

    [Tooltip("Speed threshold at or above which the 'Running' animator bool is set instead of 'Walking'.")]
    [SerializeField] private float _runThreshold = 2.5f;

    private NavMeshAgent _navAgent;
    private Coroutine _navMoveCoroutine;
    private Tween _activeTween;

    /// <summary>The cached NavMeshAgent, or null if no agent is attached.</summary>
    public NavMeshAgent NavAgent => _navAgent;

    /// <summary>
    /// Configures the NavMeshAgent properties (speed, angular speed, stopping distance) without
    /// enabling it. The agent stays disabled during normal DOTween-based movement and is only
    /// enabled explicitly by systems that require pathfinding (e.g. MutantSuspectBehaviour for
    /// retreat/climb-through sequences).
    /// No-op when no NavMeshAgent component is present.
    /// </summary>
    public void InitNavigation()
    {
        if (_navAgent == null) _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent == null) return;

        _navAgent.speed = _walkSpeed;
        _navAgent.angularSpeed = _angularSpeed;
        _navAgent.stoppingDistance = _stoppingDistance;
        _navAgent.updateRotation = false;
        // Agent stays disabled — MutantSuspectBehaviour enables it directly when pathfinding is needed.
    }

    /// <summary>
    /// Moves the character to <paramref name="destination"/> by pathfinding across the NavMesh
    /// with the <see cref="NavMeshAgent"/>, then invokes <paramref name="onArrived"/>. The agent
    /// is enabled for the duration of the move (restoring its previous enabled state afterward)
    /// so callers don't need to manage it manually. Cancels any in-progress movement before
    /// starting. Falls back to a direct DOTween move only if no NavMeshAgent is present or the
    /// character can't be linked onto the NavMesh (e.g. spawned off-mesh), so Vlad and other
    /// suspects walk around geometry instead of straight through it.
    /// </summary>
    /// <param name="destination">World-space destination.</param>
    /// <param name="onArrived">Optional callback invoked on arrival.</param>
    public void NavigateTo(Vector3 destination, Action onArrived = null)
    {
        if (_navMoveCoroutine != null) StopCoroutine(_navMoveCoroutine);
        _activeTween?.Kill();
        _navMoveCoroutine = StartCoroutine(NavigateToCoroutine(destination, onArrived));
    }

    /// <summary>
    /// Moves the character to <paramref name="destination"/> in a straight line via DOTween,
    /// bypassing the NavMeshAgent entirely. Use this instead of <see cref="NavigateTo"/> when
    /// NavMesh pathfinding is undesired (e.g. suspects walking up to the booth window), since
    /// the tween gives a consistent, predictable arrival every time regardless of NavMesh state.
    /// Cancels any in-progress movement before starting.
    /// </summary>
    /// <param name="destination">World-space destination.</param>
    /// <param name="onArrived">Optional callback invoked on arrival.</param>
    public void WalkTo(Vector3 destination, Action onArrived = null)
    {
        if (_navMoveCoroutine != null) StopCoroutine(_navMoveCoroutine);
        _activeTween?.Kill();

        if (_navAgent == null) _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null && _navAgent.enabled)
            _navAgent.enabled = false;

        _navMoveCoroutine = StartCoroutine(WalkToCoroutine(destination, onArrived));
    }

    /// <summary>Stops the current movement (NavMeshAgent path or DOTween) immediately.</summary>
    public void StopNavigation()
    {
        if (_navMoveCoroutine != null)
        {
            StopCoroutine(_navMoveCoroutine);
            _navMoveCoroutine = null;
        }

        if (_navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh)
            _navAgent.ResetPath();

        _activeTween?.Kill();
        _activeTween = null;
    }

    private IEnumerator NavigateToCoroutine(Vector3 destination, Action onArrived)
    {
        if (_navAgent == null) _navAgent = GetComponent<NavMeshAgent>();

        if (_navAgent == null)
        {
            // No NavMeshAgent on this character at all — legacy straight-line fallback.
            yield return StartCoroutine(WalkToCoroutine(destination, onArrived));
            yield break;
        }

        bool wasEnabled = _navAgent.enabled;
        if (!_navAgent.enabled)
        {
            _navAgent.enabled = true;
            // Give the agent a frame to link onto the NavMesh after being re-enabled.
            yield return null;
        }

        if (!_navAgent.isOnNavMesh)
        {
            Debug.LogWarning($"[SuspectCharacter] {name}: NavMeshAgent is not on the NavMesh at " +
                              $"{transform.position} — falling back to a direct move for this leg.");
            yield return StartCoroutine(WalkToCoroutine(destination, onArrived));
            _navAgent.enabled = wasEnabled;
            yield break;
        }

        _navAgent.speed = _walkSpeed;
        _navAgent.angularSpeed = _angularSpeed;
        _navAgent.stoppingDistance = _stoppingDistance;

        bool previousUpdateRotation = _navAgent.updateRotation;
        _navAgent.updateRotation = true; // Turn along the path while actively pathfinding.
        _navAgent.isStopped = false;
        _navAgent.SetDestination(destination);

        while (_navAgent.pathPending)
            yield return null;

        while (_navAgent.enabled && _navAgent.isOnNavMesh &&
               (_navAgent.remainingDistance > _navAgent.stoppingDistance || _navAgent.velocity.sqrMagnitude > 0.01f))
        {
            yield return null;
        }

        if (_navAgent.enabled && _navAgent.isOnNavMesh)
            _navAgent.ResetPath();

        _navAgent.updateRotation = previousUpdateRotation;
        _navAgent.enabled = wasEnabled;

        _navMoveCoroutine = null;
        onArrived?.Invoke();
    }

    /// <summary>
    /// Legacy direct-line movement via DOTween. Used only as a fallback when a NavMeshAgent
    /// isn't available or the character can't be linked onto the NavMesh.
    /// </summary>
    private IEnumerator WalkToCoroutine(Vector3 destination, Action onArrived)
    {
        // Snap to face the destination direction before moving.
        Vector3 dir = destination - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);

        // Calculate flat-plane distance so vertical offsets don't inflate the duration.
        Vector3 flatStart = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatEnd   = new Vector3(destination.x,        0f, destination.z);
        float distance    = Vector3.Distance(flatStart, flatEnd);
        float duration    = distance / Mathf.Max(0.01f, _walkSpeed);

        bool done = false;
        _activeTween = transform
            .DOMove(destination, duration)
            .SetEase(Ease.Linear)
            .OnComplete(() => done = true);

        yield return new WaitUntil(() => done);

        _activeTween = null;
        _navMoveCoroutine = null;
        onArrived?.Invoke();
    }

    /// <summary>All currently active SuspectCharacter instances. Updated automatically via OnEnable/OnDisable.</summary>
    public static readonly List<SuspectCharacter> ActiveInstances = new List<SuspectCharacter>();

    private void OnEnable()  => ActiveInstances.Add(this);
    private void OnDisable() => ActiveInstances.Remove(this);


    // Networked Animation Helpers

    /// <summary>
    /// Sets the locomotion animation state and replicates to all clients.
    /// Compares <see cref="_walkSpeed"/> against <see cref="_runThreshold"/>:
    /// if moving and speed is at or above the threshold the 'Running' bool is set; otherwise 'Walking'.
    /// Both bools are cleared when <paramref name="moving"/> is false.
    /// </summary>
    public void SetLocomotionState(bool moving)
    {
        bool shouldRun = moving && _walkSpeed >= _runThreshold;
        SetAnimatorBool("Walking", moving && !shouldRun);
        SetAnimatorBool("Running", shouldRun);
    }

    /// <summary>
    /// Sets the movement speed and immediately re-evaluates the locomotion animation state.
    /// Also updates the NavMeshAgent speed if the agent is present, for special-case pathfinding.
    /// </summary>
    public void SetMovementSpeed(float speed)
    {
        _walkSpeed = speed;
        if (_navAgent != null) _navAgent.speed = speed;
        // Re-evaluate animation state only if a movement is currently in progress.
        bool isMoving = _activeTween != null && _activeTween.IsActive() && _activeTween.IsPlaying();
        if (isMoving) SetLocomotionState(true);
    }

    /// <summary>
    /// Sets an animator bool parameter on the server and replicates it to all clients.
    /// Use this instead of <c>animator.SetBool()</c> directly for any server-driven
    /// animation state that must be visible to all players. Must be called on the server.
    /// </summary>
    public void SetAnimatorBool(string paramName, bool value)
    {
        animator.SetBool(paramName, value);
        SyncAnimatorBoolClientRpc(paramName, value);
    }

    /// <summary>
    /// Fires an animator trigger on the server and replicates it to all clients.
    /// Use this instead of <c>animator.SetTrigger()</c> for server-driven one-shot
    /// animation events. Must be called on the server.
    /// </summary>
    public void FireAnimatorTrigger(string triggerName)
    {
        animator.SetTrigger(triggerName);
        SyncAnimatorTriggerClientRpc(triggerName);
    }

    [ClientRpc]
    private void SyncAnimatorBoolClientRpc(string paramName, bool value)
    {
        if (IsServer) return;
        animator.SetBool(paramName, value);
    }

    [ClientRpc]
    private void SyncAnimatorTriggerClientRpc(string triggerName)
    {
        if (IsServer) return;
        animator.SetTrigger(triggerName);
    }

    private bool _facingPlayer;

    /// <summary>
    /// Gates Update() once this suspect hands off control to MutantEnemy. Previously this was
    /// done by setting this NetworkBehaviour's own Unity 'enabled' flag to false — but doing so
    /// at runtime made this component's inclusion in Netcode's scene-object synchronization
    /// stream diverge between the server (component disabled) and any client that joins later
    /// (freshly-loaded, still enabled), corrupting that client's sync buffer and crashing it.
    /// This flag is purely local/cosmetic (it only suppresses a look-at-player rotation in
    /// Update()), so it never needs to be perfectly synced to late joiners the way the actual
    /// networked mutant-transition state does.
    /// </summary>
    private bool _suspectUpdateDisabled;

    [Header("Combat")]
    [Tooltip("When enabled, this suspect completely ignores damage and shots from players: no hit " +
             "reaction, no flee, no death. Use for background NPCs (e.g. guard soldiers, non-interactive " +
             "story characters) that should never be affected by player weapons.")]
    [SerializeField] private bool isImmuneToDamage;

    [Tooltip("Maximum health points. Reaching zero triggers the death animation.")]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("Particle prefab spawned at the world-space hit point on all clients when the suspect is struck.")]
    [SerializeField] private GameObject hitParticlePrefab;

    [Tooltip("Animator trigger name to play on hit. Must exist in the suspect's Animator Controller.")]
    [SerializeField] private string hitAnimTrigger = "Hit";

    [Header("Wounded Flee (pre-mutation)")]
    [Tooltip("Damage a non-mutant suspect can absorb at the window before fleeing instead of dying. " +
             "Intentionally high (default 150, ~6 melee hits at 25 dmg/hit) so this only fires from " +
             "sustained, clearly intentional attacks — not an accidental stray hit.")]
    [SerializeField] private float woundedFleeHealth = 150f;

    [Tooltip("Animator trigger name to play when a non-mutant suspect flees after being beaten past " +
             "woundedFleeHealth. Must exist in the suspect's Animator Controller.")]
    [SerializeField] private string fleeAnimTrigger = "Flee";

    private float _health;
    private float _woundedHealth;
    private bool _isDead;
    private bool _isMutant;
    private bool _hasFled;

    /// <summary>
    /// True only while this suspect is the current one standing at the booth window
    /// (set by <see cref="SuspectController"/> once its arrival sequence completes).
    /// Server-authoritative and intentionally not networked — combat is only ever
    /// resolved server-side (see <see cref="TakeDamage"/> and <see cref="GetShotServerRpc"/>),
    /// so clients never need to read it. Suspects that are merely standing/walking around the
    /// scene (e.g. a scripted intro NPC before it reaches the window) are not shootable.
    /// </summary>
    private bool _isAtBooth;

    /// <summary>True once this suspect has died, regardless of visual state.</summary>
    public bool IsDead => _isDead;

    /// <summary>True once this suspect has fled the booth after being wounded past woundedFleeHealth.</summary>
    public bool HasFled => _hasFled;

    /// <summary>True while this suspect is standing at the booth window and can be shot or wounded.</summary>
    public bool IsAtBooth => _isAtBooth;

    /// <summary>
    /// Server-only. Marks whether this suspect is currently standing at the booth window.
    /// Called by <see cref="SuspectController"/> when the suspect arrives (true) and when it
    /// stops being the current suspect — despawned, replaced, or otherwise removed (false).
    /// </summary>
    public void SetIsAtBooth(bool isAtBooth)
    {
        if (!IsServer) return;
        _isAtBooth = isAtBooth;
    }

    /// <summary>Fired on the server whenever this suspect takes damage (before death check).</summary>
    public event Action OnHit;


    // Junk pickup (dead-body interaction)

    /// <summary>
    /// Optional JunkItem component on this GameObject. When present and the suspect
    /// is killed, this is activated so the body can be collected as trash. Keep it
    /// disabled by default in the Inspector - EnableJunkPickup() activates it on all clients.
    /// </summary>
    private JunkItem _junkItem;

    /// <summary>The optional JunkItem on this GameObject, if any — see <see cref="_junkItem"/>.</summary>
    public JunkItem JunkItem => _junkItem;

    /// <summary>Raised on the server when the suspect is killed by a player melee hit.</summary>
    public static event Action<SuspectCharacter> OnSuspectKilledByPlayer;

    /// <summary>
    /// Raised on the server when a non-mutant suspect flees the booth after absorbing damage past
    /// woundedFleeHealth without ever being stamped. They survive the encounter, but
    /// <see cref="FleeFromWounds"/> forces their persistent infection score to the fully-mutated
    /// threshold, guaranteeing their next appearance in the shift pool spawns as a full mutant.
    /// </summary>
    public static event Action<SuspectCharacter> OnSuspectFledFromWounds;

    [Header("Anomalies")] [SerializeField] private AnomalyController anomalyController;
    public AnomalyController AnomalyController => anomalyController;

    [Header("Drunk Behaviour")] [SerializeField] private DrunkBehaviour drunkBehaviour;

    /// <summary>Returns true if this suspect has at least one active anomaly.</summary>
    public bool IsInfected => anomalyController != null && anomalyController.activeAnomalies.Count > 0;

    //Responses
    public int ChosenEntryReasonIndex = -1;
    public int ChosenSymptomResponseIndex = -1;
    public int ChosenWhoDoYouLiveWithIndex = -1;

    public int radiationAmount = 10;
    public int heartRateBpm = 72;
    private Vector2 radiationNormal = new Vector2(0, 30);
    private Vector2 radiationSuspicious = new Vector2(31, 70);
    private Vector2 radiationInfected = new Vector2(71, 100);

    /// <summary>The SpeakingInteraction component that handles networked speech and dialogue choices.</summary>
    public SpeakingInteraction Speaking => speaking;


    protected override void Awake()
    {
        base.Awake();
        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand);
        suspectRecordViewer = GetComponent<SuspectRecordViewer>();

        if (folderGivingAnimationDatas != null && folderGivingAnimationDatas.Length > 0)
        {
            _folderGivingAnimationData = folderGivingAnimationDatas[0];
        }

        if (suspectData != null)
            interactText = $"{suspectData.FirstName}";

        _health = maxHealth;
        _woundedHealth = woundedFleeHealth;

        _junkItem = GetComponent<JunkItem>();

        // Cache MutantEnemy — it lives on the same GameObject (same prefab root).
        // The Inspector field is preferred; fall back to GetComponent so the prefab
        // works even if it was not wired up manually.
        if (_mutantEnemy == null)
            _mutantEnemy = GetComponent<MutantEnemy>();

        if (_mutantSuspectBehaviour == null)
            _mutantSuspectBehaviour = GetComponent<MutantSuspectBehaviour>();

        // MutantEnemy must stay fully dormant until BeginMutantBehavior() fires after the booth
        // cutscene. DisableAutoInit() prevents InitialiseServer() from firing automatically on
        // spawn — MutantEnemy's own internal _isActive flag (a NetworkVariable, defaulting to
        // false) tracks dormancy from here on, not this component's Unity 'enabled' flag, which
        // MutantEnemy now keeps permanently true from its own Awake(). Toggling 'enabled' at
        // runtime made a live mutant's component-inclusion state diverge from a late-joining
        // client's freshly-loaded default state, corrupting that client's scene synchronization
        // buffer and crashing it outright — see MutantEnemy._isActive for the full explanation.
        if (_mutantEnemy != null)
        {
            _mutantEnemy.DisableAutoInit();
        }

        // Cache and disable NavMeshAgent by default; server enables it via InitNavigation().
        _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null)
            _navAgent.enabled = false;

        _bodyCollider = GetComponent<CapsuleCollider>();

        // Scene-placed characters that must stay active for NGO registration (e.g. the Soldier)
        // hide their renderers/collider until RevealVisuals() is called. This runs identically on
        // every client since it only reads local scene state, so no networking is required here.
        if (_hiddenUntilRevealed)
            SetVisualsHidden(true);

        // Zero out procedural leg IK from the moment this suspect exists. The walk-in (and, for
        // full mutants, the later window climb-through) is driven by transform.DOMove tweens, not
        // real grounded locomotion — LegsAnimator's foot-planting logic fights that tweened motion
        // and produces broken-looking walk/climb poses. Leg IK stays disabled for the rest of a
        // normal suspect's time at the booth window — it is only ever blended back in for a full
        // mutant's window climb-through, once that climb-through completes (see
        // MutantSuspectBehaviour.ClimbThroughSequence). Setting the Blend field (rather than
        // toggling 'enabled') is deliberate: LegsAnimator only calls its own Initialize() from
        // Start()/OnEnable(), and disabling the component before its first Start() ever runs
        // would permanently skip that initialization — Blend has no such lifecycle pitfall. This
        // runs identically and locally on every peer, so no RPC is needed.
        SetLegsAnimatorsBlend(0f);
    }

    /// <summary>
    /// Sets <see cref="LegsAnimator.LegsAnimatorBlend"/> to <paramref name="blend"/> on every
    /// LegsAnimator found on this suspect (and its children). Used instead of toggling the
    /// component's 'enabled' flag so LegsAnimator's own Start()/Initialize() lifecycle is never
    /// disrupted — see the call site in Awake() for the full explanation.
    /// </summary>
    private void SetLegsAnimatorsBlend(float blend)
    {
        foreach (LegsAnimator legsAnimator in GetComponentsInChildren<LegsAnimator>(true))
        {
            legsAnimator.LegsAnimatorBlend = blend;
        }
    }

    /// <summary>
    /// Restores full procedural leg IK on all clients. Not called for a normal suspect's booth
    /// arrival (their leg IK stays disabled for the whole encounter — see the note in Awake()).
    /// Available for scripted/special cases that explicitly need leg IK back (mutants restore it
    /// themselves after their window climb-through instead — see <see cref="MutantSuspectBehaviour"/>).
    /// Must be called on the server.
    /// </summary>
    public void RestoreLegsAnimators()
    {
        SetLegsAnimatorsBlend(1f);
        RestoreLegsAnimatorsClientRpc();
    }

    [ClientRpc]
    private void RestoreLegsAnimatorsClientRpc()
    {
        if (IsServer) return;
        SetLegsAnimatorsBlend(1f);
    }

    /// <summary>Fired on the server when a suspect at stage 3 or 4 reaches the booth window.</summary>
    public static event Action<SuspectCharacter, int> OnSuspectPresentingUncanny;

    /// <summary>
    /// Fires the same "uncanny presence" signal normally raised when a fully-mutated suspect (or
    /// a replacement, see <see cref="InitializeAsReplacement"/>) arrives at the booth — this is
    /// what drives <see cref="GlitchController"/>'s screen glitch/film-grain effect. Use this for
    /// scripted encounters that need that same glitch beat without touching InfectionScore or
    /// spawning an actual mutant form (e.g. Ocho's booth jumpscare in <c>OchoBoothEncounter</c>).
    /// The effect clears automatically the next time <see cref="SuspectController.OnCurrentSuspectDespawned"/>
    /// fires, so callers don't need to turn it off manually as long as the suspect eventually despawns.
    /// Server-only, matching every other call site of this event.
    /// </summary>
    public void TriggerUncannyGlitchPresence()
    {
        if (!IsServer) return;
        OnSuspectPresentingUncanny?.Invoke(this, 100);
    }

    /// <summary>
    /// Primary initialization path for regular suspects.
    /// Reads the persistent infection score and activates the proportional anomaly set.
    /// </summary>
    public void InitializeByInfectionStage()
    {
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);

        if (record == null)
        {
            Debug.LogWarning($"[SuspectCharacter] No record found for '{suspectData.name}' - falling back to clean initialization.");
            InitializeClean();
            return;
        }

        anomalyController.InitializeByInfectionScore(record.infectionScore);
        MarkSuspectShown(record);
        suspectRecordViewer.SetRecord(record);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();

        if (record.IsFullyMutated)
        {
            ActivateFullMutantForm();
            OnSuspectPresentingUncanny?.Invoke(this, record.infectionScore);
            SetMutantVoiceClientRpc(true);
            PlayUncannyArriveSoundClientRpc();
        }
        else
        {
            if (_baseVersion != null) _baseVersion.SetActive(true);
            if (_mutatedVersion != null) _mutatedVersion.SetActive(false);
            SetMutantVoiceClientRpc(false);
        }
    }

    /// <summary>
    /// Initializes the suspect using the legacy random anomaly pool. Prefer <see cref="InitializeByInfectionStage"/> for campaign spawns.
    /// </summary>
    public void Initialize()
    {
        anomalyController.Initialize();
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }
        else
        {
            Debug.Log("No record found for " + suspectData.name);
        }
        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();
    }

    /// <summary>
    /// Initializes this suspect with documentation-only anomalies.
    /// All other anomaly categories are disabled. Used for scripted tutorial suspects
    /// (e.g. the Day 1 quarantine tutorial suspect) that must exhibit paperwork discrepancies and nothing else.
    /// Syncs disabled anomaly states to all clients.
    /// </summary>
    public void InitializeWithDocumentationAnomalies(int count)
    {
        anomalyController.InitializeWithDocumentationAnomalies(count);

        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }

        SyncAnomalySnapshot();

    }

    /// <summary>
    /// Initializes this suspect with anomalies drawn only from the documentation and mutation
    /// (physical) pools. All other anomaly categories are disabled. Used for scripted tutorial
    /// suspects (e.g. the Day 2 kill tutorial suspect) that must exhibit exactly a specific number
    /// of paperwork and physical symptoms, and nothing from any other category.
    /// Syncs disabled anomaly states to all clients.
    /// </summary>
    public void InitializeWithDocumentationAndPhysicalAnomalies(int count)
    {
        anomalyController.InitializeWithDocumentationAndPhysicalAnomalies(count);

        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }

        SyncAnomalySnapshot();
    }

    /// <summary>
    /// Initializes this suspect with exactly the anomaly types named in <paramref name="typeNames"/>,
    /// bypassing <see cref="AnomalyUnlockManager"/> entirely. Every other anomaly is disabled.
    /// Used for scripted "too far gone" tutorial suspects that must exhibit anomalies not yet
    /// unlocked for normal gameplay. Syncs the forced anomaly state to all clients.
    /// </summary>
    public void InitializeWithForcedAnomalyTypes(IEnumerable<string> typeNames)
    {
        anomalyController.InitializeWithForcedAnomalyTypes(typeNames);

        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();
    }

    /// <summary>
    /// Initializes this suspect with exactly <paramref name="count"/> anomalies chosen from the
    /// currently unlocked pool. The clean-chance roll is bypassed. Used for tutorial suspects
    /// that must always exhibit a specific number of anomalies.
    /// </summary>
    /// <param name="count">Exact number of anomalies to force.</param>
    public void InitializeWithExactAnomalyCount(int count)
    {
        anomalyController.InitializeWithExactAnomalyCount(count);

        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }
        else
            Debug.Log("No record found for " + suspectData.name);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();
    }

    /// <summary>
    /// Initializes this suspect as a doppelganger using the provided configuration.
    /// Anomaly activation follows the same score-based rules as a normal suspect —
    /// the doppelganger's accrued infection score drives which anomalies are shown.
    /// Replicates visual modifiers (skin desaturation, idle suppression) to clients.
    /// </summary>
    /// <param name="data">The DoppelgangerData driving visual overrides.</param>
    public void InitializeAsDoppelganger(DoppelgangerData data)
    {
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);

        int score = record?.infectionScore ?? 0;
        anomalyController.InitializeByInfectionScore(score);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }
        else
            Debug.Log($"[SuspectCharacter] No record found for doppelganger target '{suspectData.name}'.");

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();

        Debug.Log($"[SuspectCharacter] '{suspectData.name}' initialized as doppelganger " +
                  $"(overlapping: {data.overlappingAnomalyCount}, desaturation: {data.skinDesaturationAmount:F2}, " +
                  $"removeIdle: {data.removeIdleMicroMovements}).");
    }

    /// <summary>
    /// Initializes the suspect with no anomalies. Used for tutorial suspects that must
    /// be clean regardless of the anomaly distribution settings.
    /// </summary>
    public void InitializeClean()
    {
        anomalyController.InitializeClean();
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }
        else
            Debug.Log("No record found for " + suspectData.name);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);
        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();
    }

    /// <summary>
    /// Initializes this suspect as their uncanny replacement version.
    /// The character spawns clean (no physical anomalies) but will serve replacement
    /// dialogue, use the replacement ID photo, and present as subtly wrong.
    /// Replicated to all clients via <see cref="SyncReplacementClientRpc"/>.
    /// </summary>
    public void InitializeAsReplacement()
    {
        _isReplacement = true;

        anomalyController.InitializeClean();

        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record != null)
        {
            MarkSuspectShown(record);
            suspectRecordViewer.SetRecord(record);
        }
        else
            Debug.Log($"[SuspectCharacter] No record found for replacement '{suspectData.name}'.");

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();
        SyncReplacementClientRpc(true);

        OnSuspectPresentingUncanny?.Invoke(this, 100);
        PlayUncannyArriveSoundClientRpc();

        Debug.Log($"[SuspectCharacter] '{suspectData.name}' initialized as replacement.");
    }

    [ClientRpc]
    private void SyncReplacementClientRpc(bool isReplacement)
    {
        if (IsServer) return;
        _isReplacement = isReplacement;
    }

    private void MarkSuspectShown(SuspectRecord record)
    {
        if (record == null)
            return;

        int currentDay = ShiftManager.Instance != null
            ? ShiftManager.Instance.CurrentDay
            : CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;

        if (currentDay > 0 && record.lastDayShown == currentDay)
            return;

        bool isFirstAppearance = record.daysShown == 0;

        record.daysShown++;
        if (currentDay > 0)
            record.lastDayShown = currentDay;

        // First time this suspect is ever shown: if the campaign has already advanced past day 1,
        // bump their starting mutation score up to reflect the "unseen backlog" of days that passed
        // while the player wasn't looking — without making them as advanced as a suspect who was
        // actually seen and progressed day over day (that path never hits this branch again since
        // isFirstAppearance is only true once).
        if (isFirstAppearance && currentDay > 0 && AnomalyManager.Instance != null && !record.IsFullyMutated)
        {
            int tieredStartingScore = AnomalyManager.Instance.GetNeverSeenStartingScore(currentDay);
            if (tieredStartingScore > record.infectionScore)
            {
                Debug.Log($"[SuspectCharacter] '{record.SuspectData?.name}' first seen on day {currentDay} — " +
                          $"tiered starting score {record.infectionScore} -> {tieredStartingScore}.");
                record.infectionScore = tieredStartingScore;
            }
        }

        if (SuspectRunRecords.Instance != null && SaveDataManager.Instance != null)
            SuspectRunRecords.Instance.SaveRecords();
    }

    private void SyncAnomalySnapshot()
    {
        if (anomalyController == null)
            return;

        anomalyController.BuildSnapshot(
            out int[] activeAnomalyIds,
            out int[] disabledAnomalyIds,
            out int[] tentacleAnomalyIds,
            out int[] tentacleCounts,
            out int[] tentacleFlatIndices,
            out int[] tumorAnomalyIds,
            out int[] tumorCounts,
            out int[] tumorFlatIndices);

        SyncAnomalySnapshotClientRpc(
            activeAnomalyIds,
            disabledAnomalyIds,
            tentacleAnomalyIds,
            tentacleCounts,
            tentacleFlatIndices,
            tumorAnomalyIds,
            tumorCounts,
            tumorFlatIndices);
    }

    [ClientRpc]
    private void SyncAnomalySnapshotClientRpc(
        int[] activeAnomalyIds,
        int[] disabledAnomalyIds,
        int[] tentacleAnomalyIds,
        int[] tentacleCounts,
        int[] tentacleFlatIndices,
        int[] tumorAnomalyIds,
        int[] tumorCounts,
        int[] tumorFlatIndices)
    {
        if (IsServer)
            return;

        StartCoroutine(ApplyAnomalySnapshotWhenReady(
            activeAnomalyIds,
            disabledAnomalyIds,
            tentacleAnomalyIds,
            tentacleCounts,
            tentacleFlatIndices,
            tumorAnomalyIds,
            tumorCounts,
            tumorFlatIndices));
    }

    private IEnumerator ApplyAnomalySnapshotWhenReady(
        int[] activeAnomalyIds,
        int[] disabledAnomalyIds,
        int[] tentacleAnomalyIds,
        int[] tentacleCounts,
        int[] tentacleFlatIndices,
        int[] tumorAnomalyIds,
        int[] tumorCounts,
        int[] tumorFlatIndices)
    {
        const int maxFramesToWait = 60;
        int framesWaited = 0;

        while (SuspectController.Instance == null && framesWaited < maxFramesToWait)
        {
            framesWaited++;
            yield return null;
        }

        if (SuspectController.Instance != null)
        {
            SuspectController.Instance.InjectLegacyDependencies(gameObject);
        }
        else
        {
            Debug.LogWarning($"[SuspectCharacter] Could not inject anomaly dependencies before snapshot apply on '{gameObject.name}'.", this);
        }

        anomalyController?.ApplySnapshot(
            activeAnomalyIds,
            disabledAnomalyIds,
            tentacleAnomalyIds,
            tentacleCounts,
            tentacleFlatIndices,
            tumorAnomalyIds,
            tumorCounts,
            tumorFlatIndices);
    }


    /// <summary>
    /// Calls InitializeDisabled on all non-active anomalies across every category and
    /// replicates the call to clients. Invoke this when the suspect arrives at the booth
    /// to ensure locked-category anomalies (excluded from the initial activation pass)
    /// also have their shader state cleaned up.
    /// </summary>
    public void InitializeDisabledOnArrival()
    {
        anomalyController.InitializeDisabledOnArrival();
        InitializeDisabledOnArrivalClientRpc();
    }

    /// <summary>Mirrors the server-side InitializeDisabledOnArrival call to all clients.</summary>
    [ClientRpc]
    private void InitializeDisabledOnArrivalClientRpc()
    {
        if (IsServer) return;
        anomalyController.InitializeDisabledOnArrival();
    }

    /// <summary>
    /// Only highlight when the suspect is dead and collectible as junk.
    /// Suppresses the standard highlight effect while the suspect is alive.
    /// </summary>
    public override void Highlight(bool highlight)
    {
        if (_junkItem == null || !_junkItem.IsCollectible.Value)
            return;

        base.Highlight(highlight);
    }

    public override void Interact(PlayerInteractionController player)
    {
        // Route to junk collection when the body is collectible (JunkItem enabled on death).
        if (_junkItem != null && _junkItem.IsCollectible.Value)
        {
            _junkItem.Interact(player);
            return;
        }

        // A player who backed out of (or never joined) an active scripted dialogue with this
        // suspect can rejoin by interacting with them again — resumes wherever the dialogue
        // presently is, rather than restarting it.
        var runner = ScriptedDialogueRunner.Instance;
        if (runner != null)
        {
            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && ScriptedDialogueRunner.ActiveDialogueSpeakerNetId == netObj.NetworkObjectId)
            {
                runner.RequestRejoinScriptedDialogueServerRpc();
                return;
            }
        }

        // Scene-placed suspects that are talked to directly (not through the booth) can be
        // configured with a SuspectWorldDialogue for a simple 3-choice conversation.
        if (worldDialogue != null)
        {
            worldDialogue.BeginConversation();
            return;
        }

        // Booth suspects: direct interaction used to open the free-form question dialogue
        // (sourced from SuspectData.questionResponses) so the player could ask questions
        // independent of the linear scripted intro/cutscene sequence — including checking for
        // story mismatches via SuspectCharacter.GetQuestionResponse / StoryMismatchAnomaly.
        // DISABLED FOR NOW: booth suspects should only speak via their scripted intro/exit
        // dialogue (ScriptedDialogueRunner), not via a player-initiated choice-based dialogue.
        // The scripted intro/exit flow and SuspectWorldDialogue conversations above are
        // unaffected by this.
    }

    public void SetCanInteract(bool canInteract)
    {
        if (interactionCollider != null)
            interactionCollider.enabled = canInteract;
    }

    /// <summary>
    /// Networked wrapper for <see cref="SetCanInteract"/> — broadcasts to every client so a
    /// suspect that shouldn't currently be interactable (e.g. Vlad walking between waypoints
    /// in Day 2's opening/out-back sequences) reads as non-interactable in everyone's game, not
    /// just the server's. Call from server-side code only.
    /// </summary>
    public void SetCanInteractNetworked(bool canInteract)
    {
        SetCanInteractClientRpc(canInteract);
    }

    [ClientRpc]
    private void SetCanInteractClientRpc(bool canInteract)
    {
        SetCanInteract(canInteract);
    }

    [Header("Scene-Placed Visibility")]
    [Tooltip("When true, this character's renderers, interaction collider, physical body collider, " +
             "Animator, and NavMeshAgent all start disabled even though the GameObject itself stays " +
             "active. Used for scripted, scene-placed characters (e.g. the Soldier on Day 1) that " +
             "must remain active at scene load so NGO registers their in-scene NetworkObject for " +
             "every client, but shouldn't be visible, interactable, animating, colliding, or moving " +
             "until their sequence begins. Call RevealVisuals() on the server to show the character " +
             "(e.g. from SuspectController.IntroduceSceneSuspect).")]
    [SerializeField] private bool _hiddenUntilRevealed = false;

    private Renderer[] _cachedRenderers;
    private bool _visualsHidden;
    private CapsuleCollider _bodyCollider;

    /// <summary>
    /// Enables or disables everything a hidden-until-revealed character shouldn't be doing while
    /// dormant: renderers, the interaction collider, the physical body collider, the Animator, and
    /// the NavMeshAgent. Runs identically on every client since it is called from deterministic
    /// startup state (Awake) or from a replicated RPC — it never needs to touch networked state
    /// itself.
    /// The GameObject and its NetworkObject/NetworkBehaviours are deliberately left active/enabled
    /// throughout — deactivating the whole GameObject at runtime is unsafe for an already-spawned
    /// NetworkObject (it stops receiving RPCs and NetworkVariable updates), and would also prevent
    /// NGO from ever registering this in-scene NetworkObject for clients in the first place.
    /// MutantEnemy/MutantSuspectBehaviour are intentionally NOT touched here — they're kept
    /// permanently disabled from Awake() and only turned on later by BeginMutantBehavior(), and
    /// NavMeshAgent is intentionally left disabled on reveal — InitNavigation() turns it on when
    /// the walk-in sequence actually begins.
    /// </summary>
    private void SetVisualsHidden(bool hidden)
    {
        _visualsHidden = hidden;

        if (_cachedRenderers == null)
            _cachedRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in _cachedRenderers)
            if (r != null) r.enabled = !hidden;

        SetCanInteract(!hidden);

        if (_bodyCollider != null) _bodyCollider.enabled = !hidden;
        if (animator != null) animator.enabled = !hidden;

        if (hidden)
        {
            if (_navAgent == null) _navAgent = GetComponent<NavMeshAgent>();
            if (_navAgent != null) _navAgent.enabled = false;
        }
    }

    /// <summary>
    /// Server-only. Makes a scene-placed character that started with <see cref="_hiddenUntilRevealed"/>
    /// visible and interactable again, and replicates the change to all clients. No-op if the
    /// character was never hidden.
    /// </summary>
    public void RevealVisuals()
    {
        if (!IsServer) return;
        if (!_visualsHidden) return;

        SetVisualsHidden(false);
        RevealVisualsClientRpc();
    }

    [ClientRpc]
    private void RevealVisualsClientRpc()
    {
        if (IsServer) return; // already applied above
        SetVisualsHidden(false);
    }

    /// <summary>
    /// Activates the body as a collectible JunkItem on all clients. Call server-side when
    /// the suspect dies and has a JunkItem component. Re-enables the interaction collider
    /// so the body is raycasted, enables the JunkItem, and updates the interact label.
    /// </summary>
    public void EnableJunkPickup()
    {
        if (!IsServer) return;
        if (_junkItem == null) return;

        // Apply immediately on the server so TriggerTask's FindObjectsByType scan
        // counts this body as a pre-existing JunkItem before spawning trash.
        _junkItem.SetCollectible(true);
        ApplyJunkPickupState();
        EnableJunkPickupClientRpc();
    }

    [ClientRpc]
    private void EnableJunkPickupClientRpc()
    {
        if (IsServer) return; // already applied above
        if (_junkItem == null)
        {
            Debug.LogWarning($"[SuspectCharacter] EnableJunkPickupClientRpc: no JunkItem on {gameObject.name}.");
            return;
        }

        ApplyJunkPickupState();
    }

    private void ApplyJunkPickupState()
    {
        SetCanInteract(true);
        interactText = JunkItem.DefaultInteractText;

        // Mirror the JunkItem's compatible items onto SuspectCharacter so
        // PlayerInteractionController.TryItemUse routes InteractWithItem correctly.
        itemsThatCanInteractWith = _junkItem.itemsThatCanInteractWith;
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        // Route to junk collection when the body is collectible (JunkItem enabled on death).
        if (_junkItem != null && _junkItem.IsCollectible.Value)
        {
            _junkItem.InteractWithItem(playerInteractionController, item);
            return;
        }

        if (item == null)
        {
            // Empty-hand interaction no longer opens the dialogue view.
            return;
        }

        if (item is Vaccine vaccine)
        {
            vaccine.UseSyringe(this);
            return;
        }

        if (item.ItemData.name == "Shotgun")
        {
            base.InteractWithItem(playerInteractionController, item);
            GetShot();
        }
    }

    /// <summary>Routes a vaccine application to the server so anomaly removal is authoritative.</summary>
    public void ReceiveVaccine()
    {
        ReceiveVaccineServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReceiveVaccineServerRpc()
    {
        int anomalyId = anomalyController.RemoveRandomActiveAnomaly();
        if (anomalyId >= 0)
            ReceiveVaccineClientRpc(anomalyId);
    }

    /// <summary>Replicates the server's anomaly removal choice to all non-server clients.</summary>
    [ClientRpc]
    private void ReceiveVaccineClientRpc(int anomalyId)
    {
        if (IsServer) return;
        anomalyController.RemoveAnomalyById(anomalyId);
    }


    // Combat

    /// <summary>
    /// Applies damage to this suspect. Server-only. Triggers a hit reaction and,
    /// when health reaches zero, plays the death animation on all clients.
    /// Non-mutant suspects don't use <see cref="_health"/> at all — instead they absorb damage
    /// into <see cref="_woundedHealth"/> and flee once it's depleted (see <see cref="FleeFromWounds"/>).
    /// </summary>
    /// <param name="amount">Damage points to subtract.</param>
    /// <param name="hitPoint">World-space impact point used to position the blood particle.</param>
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (!IsServer || isImmuneToDamage || _isDead || _hasFled || !_isAtBooth)
            return;

        SpawnHitParticleClientRpc(hitPoint);
        OnHit?.Invoke();

        if (!_isMutant)
        {
            _woundedHealth -= amount;

            if (_woundedHealth <= 0f)
                FleeFromWounds();

            return;
        }

        _health -= amount;

        if (_health <= 0f)
            KillSuspect();
    }

    /// <summary>Spawns the blood hit particle and plays the hit reaction animation on all clients.</summary>
    [ClientRpc]
    private void SpawnHitParticleClientRpc(Vector3 hitPoint)
    {
        if (hitParticlePrefab != null)
        {
            GameObject fx = Instantiate(hitParticlePrefab, hitPoint, Quaternion.identity);
            if (fx.GetComponentInChildren<AutoDestroy>() == null)
                fx.AddComponent<AutoDestroy>();
        }

        if (animator != null && !string.IsNullOrEmpty(hitAnimTrigger))
            animator.SetTrigger(hitAnimTrigger);
    }

    /// <summary>Marks this suspect as dead and triggers death visuals on all clients.</summary>
    private void KillSuspect()
    {
        _isDead = true;
        OnSuspectKilledByPlayer?.Invoke(this);
        DisableInteractionClientRpc();
        // Reuse the existing networked death visuals (blood explosion + Die trigger).
        GetShotClientRpc();

        // If this suspect has a JunkItem, re-enable the body as collectible trash.
        if (_junkItem != null)
            EnableJunkPickup();
    }

    /// <summary>
    /// Kills this suspect immediately, bypassing the booth/interrogation-flow gate that
    /// <see cref="TakeDamage"/> enforces via <c>_isAtBooth</c>. Used when a guard soldier
    /// standing post (not part of the booth interrogation flow) is killed by a mutant —
    /// see <c>SoldierMutantResponder</c>. Reuses the same death visuals as a normal kill
    /// (blood explosion + Die animation) but does not fire <see cref="OnSuspectKilledByPlayer"/>,
    /// since this isn't a player-scored kill. Server-only.
    /// </summary>
    public void KillAsGuard()
    {
        if (!IsServer || _isDead)
            return;

        _isDead = true;
        DisableInteractionClientRpc();
        GetShotClientRpc();
    }

    /// <summary>Disables the interaction collider on all clients so the corpse cannot be interacted with.</summary>
    [ClientRpc]
    private void DisableInteractionClientRpc()
    {
        SetCanInteract(false);
    }

    /// <summary>
    /// Marks this non-mutant suspect as having fled the booth after absorbing damage past
    /// woundedFleeHealth. Unlike <see cref="KillSuspect"/>, this does not despawn or kill the
    /// suspect record — it survives, but is forced onto the fully-mutated infection threshold so
    /// the next time it's drawn into a shift's lineup, it spawns as a full mutant automatically.
    /// Server-only.
    /// </summary>
    private void FleeFromWounds()
    {
        _hasFled = true;
        DisableInteractionClientRpc();
        FleeClientRpc();

        OnSuspectFledFromWounds?.Invoke(this);

        SuspectRunRecords.Instance?.ForceFullMutation(suspectData);
    }

    /// <summary>Plays the flee reaction animation on all clients.</summary>
    [ClientRpc]
    private void FleeClientRpc()
    {
        if (animator != null && !string.IsNullOrEmpty(fleeAnimTrigger))
            animator.SetTrigger(fleeAnimTrigger);
    }

    public void GetShot()
    {
        if (NetworkManager.Singleton.IsClient)
        {
            GetShotServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void GetShotServerRpc()
    {
        if (isImmuneToDamage || _isDead || !_isAtBooth)
            return;

        _isDead = true;
        GetShotClientRpc();
    }

    [ClientRpc]
    private void GetShotClientRpc()
    {
        if (bloodExplosion != null)
            bloodExplosion.SetActive(true);

        animator.SetTrigger("Die");

        DisableLegsAnimators();
    }

    /// <summary>
    /// Disables every <see cref="LegsAnimator"/> on this suspect (and its children) so
    /// procedural leg IK stops driving the rig once the death animation takes over.
    /// </summary>
    private void DisableLegsAnimators()
    {
        foreach (LegsAnimator legsAnimator in GetComponentsInChildren<LegsAnimator>(true))
        {
            legsAnimator.enabled = false;
        }
    }

    public void AimAtPlayer()
    {
        StartCoroutine(StartFiring());
    }

    public void StartVomiting()
    {
        foreach (var vomitParticle in vomitParticles)
        {
            vomitParticle.Play();
        }
    }

    public void StopVomiting()
    {
        foreach (var vomitParticle in vomitParticles)
        {
            vomitParticle.Stop();
        }
    }

    IEnumerator StartFiring()
    {
        _facingPlayer = true;
        yield return new WaitForSeconds(1);
        animator.SetBool("Aiming Rifle", true);
        speaking.Say("You.. You're a traitor!!");
        yield return new WaitForSeconds(2);
        animator.SetBool("FiringRifle", true);

        while (true)
        {
            PlayerInstance.Instance.PlayerHealth.TakeDamage(1f, EffectKeys.ScriptedRifleDamage);
            yield return new WaitForSeconds(.5f);
        }
    }

    private void Update()
    {
        if (_suspectUpdateDisabled) return;

        if (_facingPlayer)
        {
            Vector3 targetPosition = PlayerInstance.Instance.transform.position;
            targetPosition.y = transform.position.y;
            transform.LookAt(targetPosition);
        }
    }

    public void GivePaperwork()
    {
        StartCoroutine(GivePaperworkCoroutine());
    }

    IEnumerator GivePaperworkCoroutine()
    {
        string triggerName = _folderGivingAnimationData.animationTriggerName;
        if (!string.IsNullOrEmpty(triggerName))
            animator.SetTrigger(triggerName);
        yield return new WaitForSeconds(1f);
        SuspectController.Instance.SpawnPaperwork();
    }

    public void SetFolderGivingAnimation(FolderGivingAnimation folderGivingAnimation)
    {
        foreach (var folderGivingAnimationData in folderGivingAnimationDatas)
        {
            if (folderGivingAnimationData.animation == folderGivingAnimation)
            {
                _folderGivingAnimationData = folderGivingAnimationData;
                _folderGivingAnimation = folderGivingAnimation;
                return;
            }
        }
    }

    public string GetEntryDialogue()
    {
        if (drunkBehaviour != null && drunkBehaviour.IsDrunk)
        {
            string drunkLine = drunkBehaviour.GetDrunkEntryDialogue();
            if (drunkLine != null) return drunkLine;
        }

        int dayN0 = ShiftManager.Instance.CurrentDay;

        // Uncanny override: fully-mutated suspects AND replacements use uncanny entry dialogues when authored.
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (_isReplacement || (record != null && record.IsFullyMutated))
        {
            SuspectData.DialogueByVerdict uncannySet = suspectData.uncannyEntryDialogues;
            string[] uncannyLines = dayN0 < 11
                ? uncannySet.dialoguesEarlyDays
                : dayN0 < 21
                    ? uncannySet.dialoguesMidDays
                    : uncannySet.dialoguesFinalDays;

            if (uncannyLines != null && uncannyLines.Length > 0)
                return uncannyLines[UnityEngine.Random.Range(0, uncannyLines.Length)];
        }

        // Normal entry dialogue
        SuspectData.DialogueByVerdict dialogueByVerdict = suspectData.entryDialogues;
        string[] entryDialogues;

        if (dayN0 < 11)
            entryDialogues = dialogueByVerdict.dialoguesEarlyDays;
        else if (dayN0 < 21)
            entryDialogues = dialogueByVerdict.dialoguesMidDays;
        else
            entryDialogues = dialogueByVerdict.dialoguesFinalDays;

        return entryDialogues[UnityEngine.Random.Range(0, entryDialogues.Length)];
    }

    /// <summary>
    /// Returns the response string for the given choice index based on the current day band.
    /// If StoryMismatchAnomaly is active on this suspect, the mismatch answer for the current
    /// day band is served instead, provided one has been authored. Falls back to the normal
    /// answer if the mismatch field is empty.
    /// Returns null if the index is out of range or the resolved answer text is empty.
    /// </summary>
    public string GetQuestionResponse(int choiceIndex)
    {
        if (suspectData.questionResponses == null || choiceIndex >= suspectData.questionResponses.Length)
            return null;

        SuspectData.QuestionResponseSet set = suspectData.questionResponses[choiceIndex];

        string answer;
        if (ShiftManager.Instance.IsEarlyDays)
            answer = set.earlyDaysAnswer;
        else if (ShiftManager.Instance.IsMidDays)
            answer = set.midDaysAnswer;
        else
            answer = set.finalDaysAnswer;

        if (anomalyController != null && anomalyController.ActiveCountOfType<StoryMismatchAnomaly>() > 0)
        {
            string mismatch;
            if (ShiftManager.Instance.IsEarlyDays)
                mismatch = set.mismatchEarlyDaysAnswer;
            else if (ShiftManager.Instance.IsMidDays)
                mismatch = set.mismatchMidDaysAnswer;
            else
                mismatch = set.mismatchFinalDaysAnswer;

            if (!string.IsNullOrEmpty(mismatch))
                answer = mismatch;
        }

        // Uncanny override: fully-mutated suspects AND replacements serve the uncanny answer when authored.
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (_isReplacement || (record != null && record.IsFullyMutated))
        {
            string uncannyAnswer;
            if (ShiftManager.Instance.IsEarlyDays)
                uncannyAnswer = set.uncannyEarlyDaysAnswer;
            else if (ShiftManager.Instance.IsMidDays)
                uncannyAnswer = set.uncannyMidDaysAnswer;
            else
                uncannyAnswer = set.uncannyFinalDaysAnswer;

            if (!string.IsNullOrEmpty(uncannyAnswer))
                answer = uncannyAnswer;
        }

        return string.IsNullOrEmpty(answer) ? null : answer;
    }
}
