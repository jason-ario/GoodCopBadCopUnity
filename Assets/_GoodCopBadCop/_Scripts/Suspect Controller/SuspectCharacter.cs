using GoodCopBadCop.Effects;
using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using FIMSpace.FLook;
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
    [SerializeField] Texture2D idPhoto;

    /// <summary>
    /// At runtime, returns the replacement face photo when this suspect has been initialized
    /// as a replacement. Falls back to the serialized idPhoto for all normal cases.
    /// </summary>
    public Texture2D IDPhoto => _isReplacement && suspectData != null && suspectData.replacementIDPhoto != null
        ? suspectData.replacementIDPhoto
        : idPhoto;

    /// <summary>True when this character has been spawned as an uncanny replacement of a killed suspect.</summary>
    public bool IsReplacement => _isReplacement;
    private bool _isReplacement;
    [SerializeField] Collider interactionCollider;
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

    [Header("Cameras")]
    [Tooltip("Per-character wide-shot camera used during dialogue. When assigned, this overrides the shared " +
             "scene-level 'At Booth Cam' for this specific character. Assign a child CinemachineCamera GameObject.")]
    [SerializeField] private GameObject _suspectCam;

    [Tooltip("Per-character close face camera. Referenced by the 'SuspectFaceCam' trigger key in " +
             "ScriptedDialogueRunner. Assign a child CinemachineCamera GameObject.")]
    [SerializeField] private GameObject _suspectFaceCam;

    /// <summary>Per-character wide-shot camera, or null to fall back to the shared scene-level suspect cam.</summary>
    public GameObject SuspectCam => _suspectCam;

    /// <summary>Per-character close face camera. Use camera trigger key 'SuspectFaceCam' in ScriptedDialogueNode.</summary>
    public GameObject SuspectFaceCam => _suspectFaceCam;


    // Navigation

    [Header("Navigation")]
    [Tooltip("Movement speed passed to the NavMeshAgent. Match to the walk animation speed.")]
    [SerializeField] private float _walkSpeed = 1.5f;

    [Tooltip("Rotation speed (degrees/second) while the NavMeshAgent is moving.")]
    [SerializeField] private float _angularSpeed = 240f;

    [Tooltip("Distance from the destination at which the agent is considered to have arrived.")]
    [SerializeField] private float _stoppingDistance = 0.1f;

    private NavMeshAgent _navAgent;
    private Coroutine _navMoveCoroutine;

    /// <summary>The cached NavMeshAgent, or null if no agent is attached.</summary>
    public NavMeshAgent NavAgent => _navAgent;

    /// <summary>
    /// Enables and configures the NavMeshAgent. Must be called server-side after the character
    /// spawns or is placed in the scene. No-op when no NavMeshAgent component is present.
    /// </summary>
    public void InitNavigation()
    {
        if (_navAgent == null) _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent == null) return;

        _navAgent.speed = _walkSpeed;
        _navAgent.angularSpeed = _angularSpeed;
        _navAgent.stoppingDistance = _stoppingDistance;
        _navAgent.updateRotation = false; // Manual rotation when stationary; enabled during NavigateTo.
        _navAgent.enabled = true;
    }

    /// <summary>
    /// Sets the NavMeshAgent destination and invokes <paramref name="onArrived"/> once the agent
    /// stops within <see cref="_stoppingDistance"/> of the target. Server-side only.
    /// Cancels any in-progress navigation before starting the new path.
    /// </summary>
    /// <param name="destination">World-space destination.</param>
    /// <param name="onArrived">Optional callback invoked on arrival.</param>
    public void NavigateTo(Vector3 destination, Action onArrived = null)
    {
        if (_navAgent == null || !_navAgent.enabled)
        {
            Debug.LogWarning($"[SuspectCharacter] NavigateTo called on '{name}' but NavMeshAgent is not available.");
            onArrived?.Invoke();
            return;
        }

        if (_navMoveCoroutine != null) StopCoroutine(_navMoveCoroutine);
        _navMoveCoroutine = StartCoroutine(NavMoveCoroutine(destination, onArrived));
    }

    /// <summary>Stops the current navigation immediately.</summary>
    public void StopNavigation()
    {
        if (_navMoveCoroutine != null)
        {
            StopCoroutine(_navMoveCoroutine);
            _navMoveCoroutine = null;
        }

        if (_navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh)
        {
            _navAgent.isStopped = true;
            _navAgent.updateRotation = false;
        }
    }

    private IEnumerator NavMoveCoroutine(Vector3 destination, Action onArrived)
    {
        const float retryDelay = 0.25f;
        const int maxRetries = 10;
        int retries = 0;

        _navAgent.updateRotation = true;
        _navAgent.isStopped = false;

        while (_navAgent.enabled)
        {
            _navAgent.SetDestination(destination);
            yield return null; // One frame for path calculation to begin.
            while (_navAgent.pathPending) yield return null;

            if (_navAgent.pathStatus == NavMeshPathStatus.PathComplete)
            {
                // Full path - wait until the agent actually arrives.
                while (_navAgent.enabled && (_navAgent.pathPending || _navAgent.remainingDistance > _navAgent.stoppingDistance))
                    yield return null;
                break;
            }

            // Partial or invalid path - a closed gate or door is likely blocking.
            // Let the agent walk as far as it can so the obstacle's proximity auto-open fires,
            // then wait for the NavMesh to update and retry.
            if (retries >= maxRetries)
            {
                Debug.LogWarning($"[SuspectCharacter] NavigateTo '{name}': could not reach destination after {maxRetries} retries - proceeding anyway.");
                break;
            }

            while (_navAgent.enabled && !_navAgent.pathPending
                   && _navAgent.remainingDistance > _navAgent.stoppingDistance
                   && !float.IsInfinity(_navAgent.remainingDistance))
                yield return null;

            // Already close enough - treat as arrived.
            if (Vector3.Distance(transform.position, destination) <= _navAgent.stoppingDistance + 0.05f)
                break;

            // Brief wait for the obstacle to clear and NavMesh to re-bake.
            yield return new WaitForSeconds(retryDelay);
            retries++;
        }

        if (_navAgent.enabled && _navAgent.isOnNavMesh)
        {
            _navAgent.isStopped = true;
            _navAgent.updateRotation = false;
        }

        _navMoveCoroutine = null;
        onArrived?.Invoke();
    }

    /// <summary>All currently active SuspectCharacter instances. Updated automatically via OnEnable/OnDisable.</summary>
    public static readonly List<SuspectCharacter> ActiveInstances = new List<SuspectCharacter>();

    private void OnEnable()  => ActiveInstances.Add(this);
    private void OnDisable() => ActiveInstances.Remove(this);


    // Networked Animation Helpers

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

    [Header("Combat")]
    [Tooltip("Maximum health points. Reaching zero triggers the death animation.")]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("Particle prefab spawned at the world-space hit point on all clients when the suspect is struck.")]
    [SerializeField] private GameObject hitParticlePrefab;

    [Tooltip("Animator trigger name to play on hit. Must exist in the suspect's Animator Controller.")]
    [SerializeField] private string hitAnimTrigger = "Hit";

    private float _health;
    private bool _isDead;

    /// <summary>True once this suspect has died, regardless of visual state.</summary>
    public bool IsDead => _isDead;

    /// <summary>Fired on the server whenever this suspect takes damage (before death check).</summary>
    public event Action OnHit;


    // Junk pickup (dead-body interaction)

    /// <summary>
    /// Optional JunkItem component on this GameObject. When present and the suspect
    /// is killed, this is activated so the body can be collected as trash. Keep it
    /// disabled by default in the Inspector - EnableJunkPickup() activates it on all clients.
    /// </summary>
    private JunkItem _junkItem;

    /// <summary>Raised on the server when the suspect is killed by a player melee hit.</summary>
    public static event Action<SuspectCharacter> OnSuspectKilledByPlayer;

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

        _junkItem = GetComponent<JunkItem>();

        // Cache and disable NavMeshAgent by default; server enables it via InitNavigation().
        _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null)
            _navAgent.enabled = false;
    }

    /// <summary>Fired on the server when a suspect at stage 3 or 4 reaches the booth window.</summary>
    public static event Action<SuspectCharacter, int> OnSuspectPresentingUncanny;

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
        suspectRecordViewer.SetRecord(record);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();

        drunkBehaviour?.TryActivate();

        if (record.IsFullyMutated)
            OnSuspectPresentingUncanny?.Invoke(this, record.infectionScore);
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
    /// (e.g. Ivan on Day 1) that must exhibit paperwork discrepancies and nothing else.
    /// Syncs disabled anomaly states to all clients.
    /// </summary>
    public void InitializeWithDocumentationAnomalies(int count)
    {
        anomalyController.InitializeWithDocumentationAnomalies(count);

        SyncAnomalySnapshot();

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
            suspectRecordViewer.SetRecord(record);
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
    /// Applies overlapping anomalies and all uncanny anomalies, then replicates
    /// visual modifiers (skin desaturation, idle suppression) to clients.
    /// </summary>
    /// <param name="data">The DoppelgangerData driving anomaly count and visual overrides.</param>
    public void InitializeAsDoppelganger(DoppelgangerData data)
    {
        // Anomaly initialization - full doppelganger loadout will be wired here
        // once AnomalyController.InitializeAsDoppelganger is implemented.
        anomalyController.Initialize();

        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
            suspectRecordViewer.SetRecord(record);
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
            suspectRecordViewer.SetRecord(record);
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
            suspectRecordViewer.SetRecord(record);
        else
            Debug.Log($"[SuspectCharacter] No record found for replacement '{suspectData.name}'.");

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        SyncAnomalySnapshot();
        SyncReplacementClientRpc(true);

        OnSuspectPresentingUncanny?.Invoke(this, 100);

        Debug.Log($"[SuspectCharacter] '{suspectData.name}' initialized as replacement.");
    }

    [ClientRpc]
    private void SyncReplacementClientRpc(bool isReplacement)
    {
        if (IsServer) return;
        _isReplacement = isReplacement;
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
        if (_junkItem == null || !_junkItem.enabled)
            return;

        base.Highlight(highlight);
    }

    public override void Interact(PlayerInteractionController player)
    {
        // Route to junk collection when the body is collectible (JunkItem enabled on death).
        if (_junkItem != null && _junkItem.enabled)
        {
            _junkItem.Interact(player);
            return;
        }

        // Direct interaction no longer opens the dialogue view.
        // Dialogue is initiated exclusively through scripted cutscenes (ScriptedDialogueRunner).
    }

    public void SetCanInteract(bool canInteract)
    {
        if (interactionCollider != null)
            interactionCollider.enabled = canInteract;
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
        _junkItem.enabled = true;
        SetCanInteract(true);
        interactText = JunkItem.DefaultInteractText;

        // Mirror the JunkItem's compatible items onto SuspectCharacter so
        // PlayerInteractionController.TryItemUse routes InteractWithItem correctly.
        itemsThatCanInteractWith = _junkItem.itemsThatCanInteractWith;
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        // Route to junk collection when the body is collectible (JunkItem enabled on death).
        if (_junkItem != null && _junkItem.enabled)
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
    /// </summary>
    /// <param name="amount">Damage points to subtract.</param>
    /// <param name="hitPoint">World-space impact point used to position the blood particle.</param>
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (!IsServer || _isDead)
            return;

        _health -= amount;
        SpawnHitParticleClientRpc(hitPoint);
        OnHit?.Invoke();

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
            EnableJunkPickupClientRpc();
    }

    /// <summary>Disables the interaction collider on all clients so the corpse cannot be interacted with.</summary>
    [ClientRpc]
    private void DisableInteractionClientRpc()
    {
        SetCanInteract(false);
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
        _isDead = true;
        GetShotClientRpc();
    }

    [ClientRpc]
    private void GetShotClientRpc()
    {
        if (bloodExplosion != null)
            bloodExplosion.SetActive(true);

        animator.SetTrigger("Die");
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
        animator.SetTrigger(_folderGivingAnimationData.animationTriggerName);
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
