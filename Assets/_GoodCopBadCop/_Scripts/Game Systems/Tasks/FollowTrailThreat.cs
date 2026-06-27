using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: "Follow the Trail".
/// A grotesque corpse appears at the start of the day, leading to an invisible trail
/// of particles only visible under UV light. Investigating the final destination
/// resolves the threat.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FollowTrailThreat : NetworkBehaviour, ISystemicThreat
{
    public static FollowTrailThreat Instance { get; private set; }

    [Serializable]
    public struct FollowTrailLocation
    {
        public Transform CorpsePoint;
        public Transform DestinationPoint;
        public Transform[] TrailPath;
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

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isDiscovered = new(
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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public void BeginNightPhase()
    {
        // Logic starts at day start, but we can use this to ensure visibility or status if needed.
        // For this specific threat, it persists from day into night.
    }

    public void EndNightPhase()
    {
        // Clean up or lock state.
    }

    // ── Public API ────────────────────────────────────────────────────────────

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

        _isDiscovered.Value = true;
        _networkThreatLevel.Value = 0f;
        
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

        if (CampaignManager.Instance?.ActiveDay == null || !CampaignManager.Instance.ActiveDay.CanFollowTrailEvent)
        {
            _networkThreatLevel.Value = 0f;
            _isDiscovered.Value = false;
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

        FollowTrailLocation location = _possibleLocations[UnityEngine.Random.Range(0, _possibleLocations.Count)];

        // Spawn Corpse
        if (_corpsePrefab != null && location.CorpsePoint != null)
        {
            _spawnedCorpse = Instantiate(_corpsePrefab, location.CorpsePoint.position, location.CorpsePoint.rotation);
            _spawnedCorpse.GetComponent<NetworkObject>().Spawn(true);
        }

        // Spawn Destination
        if (_destinationPrefab != null && location.DestinationPoint != null)
        {
            _spawnedDestination = Instantiate(_destinationPrefab, location.DestinationPoint.position, location.DestinationPoint.rotation);
            _spawnedDestination.GetComponent<NetworkObject>().Spawn(true);
        }

        // Spawn Trail Particles
        if (_trailParticlesPrefab != null && location.TrailPath != null)
        {
            foreach (Transform p in location.TrailPath)
            {
                if (p == null) continue;
                GameObject trail = Instantiate(_trailParticlesPrefab, p.position, p.rotation);
                trail.GetComponent<NetworkObject>().Spawn(true);
                _spawnedTrailParticles.Add(trail);
            }
        }

        _networkThreatLevel.Value = 1.0f;
        _isDiscovered.Value = false;
        
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

        foreach (GameObject trail in _spawnedTrailParticles)
        {
            if (trail != null)
            {
                trail.GetComponent<NetworkObject>().Despawn(true);
            }
        }
        _spawnedTrailParticles.Clear();
    }
}
