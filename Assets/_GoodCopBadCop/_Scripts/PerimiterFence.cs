using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

/// <summary>
/// A single perimeter fence segment that can be damaged by mutants and repaired by players.
///
/// Health drives four visual states via <see cref="_damageStateMeshRoots"/>:
///   Index 0: Healthy  (≥ 75 % health)
///   Index 1: Slightly damaged  (≥ 50 %)
///   Index 2: Mostly damaged  (≥ 25 %)
///   Index 3: Critical — NavMeshObstacle disabled, mutants can pass through  (&lt; 25 %)
///
/// Prefab setup:
///   - NetworkObject on this GameObject.
///   - NavMeshObstacle on this GameObject (carving enabled).
///   - Four child GameObjects (one per visual state) assigned to DamageStateMeshRoots.
///   - AudioSource on this GameObject.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PerimiterFence : NetworkBehaviour
{
    // ── Configuration ─────────────────────────────────────────────────────────

    [Header("Damage State Meshes")]
    [Tooltip("One root GameObject per visual damage state. Index 0 = healthy, higher indices = more broken. " +
             "Each root should have its own LOD Group component.")]
    [SerializeField] private GameObject[] _damageStateMeshRoots;

    [Header("Health")]
    [Tooltip("Maximum health of this fence segment.")]
    [SerializeField] private float _maxHealth = 100f;

    [Tooltip("Health restored per hammer hit during player repair.")]
    [SerializeField] private float _hammerRepairAmount = 34f;

    [Tooltip("Health-percentage thresholds (descending) at which the visual damage state advances. " +
             "Must contain one fewer entry than DamageStateMeshRoots. " +
             "Default {75, 50, 25}: state 1 below 75 %, state 2 below 50 %, passable below 25 %.")]
    [SerializeField] private float[] _damageThresholds = { 75f, 50f, 25f };

    [Header("Audio")]
    [SerializeField] private AudioClip _hammerHitSound;
    [SerializeField] private AudioClip _repairCompleteSound;
    [SerializeField] private AudioClip _mutantHitSound;
    [SerializeField] private AudioSource _audioSource;

    [Header("VFX")]
    [Tooltip("Particle system prefab spawned at the contact point when a mutant hits this fence.")]
    [SerializeField] private ParticleSystem _mutantHitParticlePrefab;

    // ── Networked state ────────────────────────────────────────────────────────

    /// <summary>
    /// Current health. Authoritative on server, replicated to all clients.
    /// Starts at 0 and is set to <see cref="_maxHealth"/> in <see cref="OnNetworkSpawn"/>.
    /// </summary>
    private NetworkVariable<float> _health = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ────────────────────────────────────────────────────────────

    private NavMeshObstacle _navMeshObstacle;

    /// <summary>Prevents audio from playing during initial spawn synchronisation.</summary>
    private bool _initialized;

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>True when this fence segment has taken any damage.</summary>
    public bool IsBroken => _health.Value < _maxHealth;

    /// <summary>True when this fence is at full health.</summary>
    public bool IsRepaired => _health.Value >= _maxHealth;

    /// <summary>
    /// True when this fence is in its most-damaged state and mutants can walk through it.
    /// Health-based so this works whether or not a NavMeshObstacle is present on the prefab.
    /// By default, mutants can pass through once health drops below 25 %.
    /// </summary>
    public bool IsPassableByMutant => _health.Value < (_maxHealth * 0.25f);

    /// <summary>
    /// The highest valid integer damage level, derived from the number of mesh root entries.
    /// A fence with four mesh roots supports levels 0–3.
    /// Kept for backward-compatibility with <see cref="FenceRepairTask"/>.
    /// </summary>
    public int MaxDamageLevel => _damageStateMeshRoots != null
        ? Mathf.Max(0, _damageStateMeshRoots.Length - 1)
        : 0;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the server when a player's hammer hits restore this fence to full health.
    /// <see cref="FenceRepairTask"/> subscribes to this to track task progress.
    /// </summary>
    public event Action<PerimiterFence> OnFullyRepaired;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _navMeshObstacle = GetComponent<NavMeshObstacle>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _health.OnValueChanged += OnHealthChanged;

        // Server initialises health to full on first spawn.
        // Subsequent SetDamageLevelServer calls (from FenceRepairTask) override this.
        if (IsServer && _health.Value == 0f)
            _health.Value = _maxHealth;

        // Apply the current state immediately. OnValueChanged does not fire for initial values
        // on clients that join after the fence has already been synchronised.
        ApplyHealthState(_health.Value);

        _initialized = true;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _health.OnValueChanged -= OnHealthChanged;
        _initialized = false;
    }

    // ── Health state ───────────────────────────────────────────────────────────

    private void OnHealthChanged(float previous, float current)
    {
        ApplyHealthState(current);

        if (!_initialized || _audioSource == null) return;

        if (current >= _maxHealth && _repairCompleteSound != null)
            _audioSource.PlayOneShot(_repairCompleteSound);
        else if (current > previous && _hammerHitSound != null)
            _audioSource.PlayOneShot(_hammerHitSound);
        // Mutant hit sound is broadcast separately via PlayMutantHitSoundClientRpc.
    }

    /// <summary>
    /// Derives the visual damage state from the current health value, then applies mesh visuals
    /// accordingly. Physical (non-trigger) colliders are intentionally left untouched — they stay
    /// enabled at every damage state so the fence keeps physically blocking players even when
    /// fully broken, and so the hammer's hit collider (used to detect repair hits) keeps
    /// registering hits at the most-damaged state.
    /// </summary>
    private void ApplyHealthState(float health)
    {
        int state = GetDamageState(health);
        ApplyDamageVisuals(state);
        ApplyNavMeshObstacleState(state);
    }

    /// <summary>
    /// Maps a health value to a visual damage state index.
    /// State 0 = healthy; each higher index corresponds to a lower health band.
    /// The last state (index == <see cref="MaxDamageLevel"/>) is entered when health falls
    /// below the lowest <see cref="_damageThresholds"/> entry and disables the NavMeshObstacle.
    /// </summary>
    private int GetDamageState(float health)
    {
        if (_maxHealth <= 0f || _damageThresholds == null || _damageThresholds.Length == 0)
            return 0;

        float pct = (health / _maxHealth) * 100f;

        for (int i = 0; i < _damageThresholds.Length; i++)
        {
            if (pct >= _damageThresholds[i])
                return i;
        }

        // Below all thresholds → worst (passable) state.
        return _damageThresholds.Length;
    }

    private void ApplyDamageVisuals(int state)
    {
        if (_damageStateMeshRoots == null) return;

        for (int i = 0; i < _damageStateMeshRoots.Length; i++)
        {
            if (_damageStateMeshRoots[i] != null)
                _damageStateMeshRoots[i].SetActive(i == state);
        }
    }

    /// <summary>
    /// Enables/disables the NavMeshObstacle's carving based on damage state so mutants can
    /// pathfind straight through this fence once it reaches its most-damaged (passable) state.
    /// The physical BoxCollider is never touched here, so the player can never walk through the
    /// fence regardless of its damage state.
    /// </summary>
    private void ApplyNavMeshObstacleState(int state)
    {
        if (_navMeshObstacle == null) return;

        // Only the worst damage state (index == MaxDamageLevel) is passable by mutants.
        bool passable = state >= MaxDamageLevel;
        _navMeshObstacle.carving = !passable;
        _navMeshObstacle.enabled = !passable;
    }

    // ── Public server API ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets this fence to a specific integer damage level. 0 = fully repaired; higher = more broken.
    /// Called by <see cref="FenceRepairTask"/> at the start of each night phase. Server-only.
    /// Maps level 0 → full health and <see cref="MaxDamageLevel"/> → 0 health linearly.
    /// </summary>
    /// <param name="level">Target damage level. Clamped to 0–<see cref="MaxDamageLevel"/>.</param>
    public void SetDamageLevelServer(int level)
    {
        Debug.Assert(IsServer, "[PerimiterFence] SetDamageLevelServer must be called on the server.");

        int clamped = Mathf.Clamp(level, 0, MaxDamageLevel);
        float t = MaxDamageLevel > 0 ? 1f - (float)clamped / MaxDamageLevel : 1f;
        _health.Value = Mathf.Clamp(t * _maxHealth, 0f, _maxHealth);
    }

    /// <summary>
    /// Registers a single hammer hit, restoring health by <see cref="_hammerRepairAmount"/>.
    /// Raises <see cref="OnFullyRepaired"/> when health returns to maximum.
    /// Safe to call from any client — ownership is not required.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void HitWithHammerServerRpc()
    {
        if (IsRepaired) return;

        _health.Value = Mathf.Min(_maxHealth, _health.Value + _hammerRepairAmount);

        if (_health.Value >= _maxHealth)
            OnFullyRepaired?.Invoke(this);
    }

    /// <summary>
    /// Applies mutant melee damage, reducing health by <paramref name="damage"/>.
    /// Always triggers the mutant-hit feedback (sound + shake + particle) on all clients.
    /// Must be called on the server.
    /// </summary>
    /// <param name="damage">Damage amount to apply.</param>
    /// <param name="hitPosition">World-space contact point used to place the hit particle.</param>
    public void TakeMutantHitServer(float damage, Vector3 hitPosition)
    {
        Debug.Assert(IsServer, "[PerimiterFence] TakeMutantHitServer must be called on the server.");

        PlayMutantHitFeedbackClientRpc(hitPosition);

        if (_health.Value <= 0f) return;

        _health.Value = Mathf.Max(0f, _health.Value - damage);
    }

    [ClientRpc]
    private void PlayMutantHitFeedbackClientRpc(Vector3 hitPosition)
    {
        // Sound
        if (_audioSource != null && _mutantHitSound != null)
            _audioSource.PlayOneShot(_mutantHitSound);

        // Particle
        if (_mutantHitParticlePrefab != null)
        {
            ParticleSystem instance = Instantiate(_mutantHitParticlePrefab, hitPosition, Quaternion.identity);
            Destroy(instance.gameObject, instance.main.duration + instance.main.startLifetime.constantMax);
        }

        // Shake
        transform.DOComplete();
        transform.DOShakePosition(0.5f, strength: 0.10f, vibrato: 30, randomness: 90f, snapping: false, fadeOut: true);
    }
}
