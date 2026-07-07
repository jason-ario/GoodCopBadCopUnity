using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: "Follow the Trail".
/// A grotesque corpse appears at the start of the day, leading to an invisible trail
/// of particles only visible under UV light. Investigating the final destination
/// resolves the threat.
///
/// Also acts as the central network-sync hub for the post-shift Vlad Out-Back sequence:
/// three NetworkVariables (<see cref="_meetVladActive"/>, <see cref="_followTrailActive"/>,
/// <see cref="_killMutantActive"/>) drive TaskRegistry registration on ALL clients so the HUD
/// stays in sync without requiring Day_02 to be a NetworkBehaviour itself.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FollowTrailThreat : NetworkBehaviour, ISystemicThreat
{
    public static FollowTrailThreat Instance { get; private set; }

    [Serializable]
    public struct FollowTrailLocation
    {
        public Transform       CorpsePoint;
        public Transform       DestinationPoint;
        /// <summary>
        /// Spline-based trail. Spawn count and jitter are configured on the TrailController itself.
        /// </summary>
        public TrailController Trail;
    }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Follow the Trail";
    [SerializeField] private float _scoreWeight = 1.5f;

    [Header("Prefabs")]
    [SerializeField] private GameObject _corpsePrefab;
    [SerializeField] private GameObject _trailParticlesPrefab;
    [SerializeField] private GameObject _destinationPrefab;

    [Header("Spawn Locations")]
    [SerializeField] private List<FollowTrailLocation> _possibleLocations;

    [Header("Trail Placement")]
    [Tooltip("Layers to hit when snapping particles to the terrain. Set to your terrain/ground layer for best results.")]
    [SerializeField] private LayerMask _terrainLayerMask = Physics.AllLayers;

    [Tooltip("Y offset above the terrain surface applied to every spawned blood particle.")]
    [SerializeField] private float _trailGroundOffset = 0.05f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDiscovered = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// When true, the "Meet Vlad out back" task appears in every client's HUD.
    /// Set via <see cref="SetMeetVladActive"/>; cleared when the player approaches Vlad.
    /// </summary>
    private readonly NetworkVariable<bool> _meetVladActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// When true, the "Follow the trail" task (this object) appears in every client's HUD.
    /// Set via <see cref="SetFollowTrailTaskActive"/>; cleared when the trail destination is found.
    /// </summary>
    private readonly NetworkVariable<bool> _followTrailActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// When true, the "Kill the mutant" task appears in every client's HUD.
    /// Set via <see cref="SetKillMutantActive"/>; cleared when a mutant is killed while active.
    /// </summary>
    private readonly NetworkVariable<bool> _killMutantActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private GameObject _spawnedCorpse;
    private GameObject _spawnedDestination;
    private readonly List<GameObject> _spawnedTrailParticles = new();

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription => _isDiscovered.Value
        ? "Trail investigated."
        : (_networkThreatLevel.Value > 0 ? "Grotesque corpse found. Follow the residue." : "No active trail.");

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _meetVladActive.OnValueChanged    += OnMeetVladActiveChanged;
        _followTrailActive.OnValueChanged += OnFollowTrailActiveChanged;
        _killMutantActive.OnValueChanged  += OnKillMutantActiveChanged;

        // Apply initial values for late-joining clients so they see any already-active tasks.
        if (_meetVladActive.Value)    MeetVladOutBackTask.CreateAndRegister();
        if (_followTrailActive.Value) TaskRegistry.Instance?.AddThreat(this);
        if (_killMutantActive.Value)  KillMutantTask.CreateAndRegister();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _meetVladActive.OnValueChanged    -= OnMeetVladActiveChanged;
        _followTrailActive.OnValueChanged -= OnFollowTrailActiveChanged;
        _killMutantActive.OnValueChanged  -= OnKillMutantActiveChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── NetworkVariable callbacks — fire on ALL clients ───────────────────────

    private void OnMeetVladActiveChanged(bool previous, bool current)
    {
        if (current) MeetVladOutBackTask.CreateAndRegister();
        else         MeetVladOutBackTask.CompleteAndRemove();
    }

    private void OnFollowTrailActiveChanged(bool previous, bool current)
    {
        if (current) TaskRegistry.Instance?.AddThreat(this);
        else         TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnKillMutantActiveChanged(bool previous, bool current)
    {
        if (current) KillMutantTask.CreateAndRegister();
        else         KillMutantTask.CompleteAndRemove();
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public void BeginNightPhase() { }
    public void EndNightPhase()   { }

    // ── Public API — task visibility setters (server only) ────────────────────

    /// <summary>Shows or hides the "Meet Vlad out back" HUD task on all clients. Server only.</summary>
    public void SetMeetVladActive(bool active)
    {
        if (!IsServer) return;
        _meetVladActive.Value = active;
    }

    /// <summary>Shows or hides the "Follow the trail" HUD task on all clients. Server only.</summary>
    public void SetFollowTrailTaskActive(bool active)
    {
        if (!IsServer) return;
        _followTrailActive.Value = active;
    }

    /// <summary>Shows or hides the "Kill the mutant" HUD task on all clients. Server only.</summary>
    public void SetKillMutantActive(bool active)
    {
        if (!IsServer) return;
        _killMutantActive.Value = active;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Optional server-side callback injected by a day script (e.g. Day_02) to intercept trail
    /// resolution. When assigned, called in place of <see cref="BetweenShiftTaskManager.HandleNightPhaseReady"/>
    /// when the player reaches the trail destination. The day script is then responsible for
    /// advancing the night phase (e.g. after the Kill Mutant task is completed).
    /// Set to null to restore default behaviour.
    /// </summary>
    public Action OnDestinationDiscoveredOverride;

    /// <summary>
    /// Manually spawns the trail event from an external system (e.g. Day_02 post-shift).
    /// Server only. Safe to call when CanFollowTrailEvent is false on DayBase.
    /// Picks a location randomly from <see cref="_possibleLocations"/>.
    /// </summary>
    public void TriggerTrailEvent()
    {
        if (!IsServer) return;
        Cleanup();
        SpawnEvent();
    }

    /// <summary>
    /// Manually spawns the trail event at a specific location index.
    /// Use this when a day script needs a deterministic trail start point
    /// (e.g. Day 2 always starts from the dead animal).
    /// Server only.
    /// </summary>
    public void TriggerTrailEvent(int locationIndex)
    {
        if (!IsServer) return;

        if (_possibleLocations == null || locationIndex < 0 || locationIndex >= _possibleLocations.Count)
        {
            Debug.LogWarning($"[FollowTrailThreat] Location index {locationIndex} is out of range — falling back to random.", this);
            TriggerTrailEvent();
            return;
        }

        Cleanup();
        SpawnEvent(_possibleLocations[locationIndex]);
    }

    /// <summary>
    /// Called by TrailDestinationInteractable when the player interacts with the final point.
    /// </summary>
    public void OnDestinationDiscovered()
    {
        if (!IsServer)
        {
            OnDestinationDiscoveredServerRpc();
            return;
        }

        if (_isDiscovered.Value) return;

        _isDiscovered.Value       = true;
        _networkThreatLevel.Value = 0f;

        // Remove the "Follow the trail" HUD task from all clients.
        _followTrailActive.Value = false;

        // If a day script has registered a custom override, delegate resolution to it.
        // Otherwise fall back to the default: immediately advance the night phase timer.
        if (OnDestinationDiscoveredOverride != null)
            OnDestinationDiscoveredOverride.Invoke();
        else
            BetweenShiftTaskManager.Instance?.HandleNightPhaseReady();

        Debug.Log("[FollowTrailThreat] Destination discovered. Threat resolved.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void OnDestinationDiscoveredServerRpc()
    {
        OnDestinationDiscovered();
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        if (!IsServer) return;

        Cleanup();

        // Reset all task visibility flags for the new day.
        _meetVladActive.Value    = false;
        _followTrailActive.Value = false;
        _killMutantActive.Value  = false;

        if (CampaignManager.Instance?.ActiveDay == null || !CampaignManager.Instance.ActiveDay.CanFollowTrailEvent)
        {
            _networkThreatLevel.Value = 0f;
            _isDiscovered.Value       = false;
            return;
        }

        SpawnEvent();
    }

    private void SpawnEvent()
    {
        if (_possibleLocations == null || _possibleLocations.Count == 0)
        {
            Debug.LogWarning("[FollowTrailThreat] No possible locations assigned.");
            return;
        }

        SpawnEvent(_possibleLocations[UnityEngine.Random.Range(0, _possibleLocations.Count)]);
    }

    private void SpawnEvent(FollowTrailLocation location)
    {
        if (_corpsePrefab != null && location.CorpsePoint != null)
        {
            _spawnedCorpse = Instantiate(_corpsePrefab, location.CorpsePoint.position, location.CorpsePoint.rotation);
            _spawnedCorpse.GetComponent<NetworkObject>().Spawn(true);
        }

        if (_destinationPrefab != null && location.DestinationPoint != null)
        {
            _spawnedDestination = Instantiate(_destinationPrefab, location.DestinationPoint.position, location.DestinationPoint.rotation);
            _spawnedDestination.GetComponent<NetworkObject>().Spawn(true);
        }

        if (_trailParticlesPrefab == null)
        {
            Debug.LogWarning("[FollowTrailThreat] _trailParticlesPrefab is not assigned — no blood trail will spawn.", this);
        }
        else if (location.Trail == null)
        {
            Debug.LogWarning("[FollowTrailThreat] location.Trail (TrailController) is not assigned on this FollowTrailLocation — no blood trail will spawn.", this);
        }
        else
        {
            List<Vector3> spawnPositions = location.Trail.GetSpawnPositions();
            for (int i = 0; i < spawnPositions.Count; i++)
                spawnPositions[i] = SnapToTerrain(spawnPositions[i], _trailGroundOffset);
            Debug.Log($"[FollowTrailThreat] Spawning {spawnPositions.Count} trail particles.", this);
            SpawnTrailParticlesClientRpc(spawnPositions.ToArray());
        }

        _networkThreatLevel.Value = 1.0f;
        _isDiscovered.Value       = false;

        Debug.Log("[FollowTrailThreat] Event spawned.");
    }

    private void Cleanup()
    {
        if (_spawnedCorpse != null)
        {
            _spawnedCorpse.GetComponent<NetworkObject>().Despawn(true);
            _spawnedCorpse = null;
        }

        if (_spawnedDestination != null)
        {
            _spawnedDestination.GetComponent<NetworkObject>().Despawn(true);
            _spawnedDestination = null;
        }

        // Trail particles are local instances on each client — clean up via ClientRpc.
        if (IsSpawned)
            CleanupTrailParticlesClientRpc();
        else
        {
            foreach (GameObject trail in _spawnedTrailParticles)
                if (trail != null) Destroy(trail);
            _spawnedTrailParticles.Clear();
        }
    }

    /// <summary>
    /// Casts downward from above <paramref name="position"/> to find the terrain surface,
    /// then applies <paramref name="yOffset"/>. Falls back to the original position + offset
    /// if nothing is hit.
    /// </summary>
    private Vector3 SnapToTerrain(Vector3 position, float yOffset)
    {
        const float castOriginHeight = 50f;
        const float castDistance     = 100f;

        Vector3 origin = new Vector3(position.x, position.y + castOriginHeight, position.z);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castDistance, _terrainLayerMask))
            return hit.point + Vector3.up * yOffset;

        return new Vector3(position.x, position.y + yOffset, position.z);
    }

    /// <summary>
    /// Instantiates the blood-particle prefab at each position on every client.
    /// Trail particles are purely visual so they don't need a NetworkObject.
    /// </summary>
    [ClientRpc]
    private void SpawnTrailParticlesClientRpc(Vector3[] positions)
    {
        foreach (Vector3 pos in positions)
        {
            GameObject trail = Instantiate(_trailParticlesPrefab, pos, Quaternion.identity);
            _spawnedTrailParticles.Add(trail);
        }
    }

    /// <summary>
    /// Destroys all locally-instantiated trail particle objects on every client.
    /// </summary>
    [ClientRpc]
    private void CleanupTrailParticlesClientRpc()
    {
        foreach (GameObject trail in _spawnedTrailParticles)
            if (trail != null) Destroy(trail);
        _spawnedTrailParticles.Clear();
    }
}
