using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single perimeter fence segment that can be damaged and repaired.
///
/// Prefab setup:
///   - Add a NetworkObject component to this GameObject.
///   - Add child GameObjects — one per damage level — and assign them to DamageStateMeshRoots:
///       Index 0: Healthy (normal fence mesh with LOD group)
///       Index 1: Slightly damaged mesh with LOD group
///       Index 2: Mostly damaged mesh with LOD group
///       Index 3: Destroyed / most broken mesh with LOD group
///   - Ensure the fence has a Collider so HammerPickable's OverlapSphere can detect it.
///
/// The task (FenceRepairTask) calls SetDamageLevelServer() to break a fence.
/// Players hit it with HammerPickable, which calls HitWithHammerServerRpc() once per swing.
/// Each hit decrements the damage level by one; reaching 0 means the fence is repaired.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PerimiterFence : NetworkBehaviour
{
    [Header("Damage State Meshes")]
    [Tooltip("One root GameObject per damage level. Index 0 = healthy, higher indices = more broken.\n" +
             "Each root should have its own LOD Group component for mesh detail.")]
    [SerializeField] private GameObject[] _damageStateMeshRoots;

    [Header("Audio")]
    [SerializeField] private AudioClip _hammerHitSound;
    [SerializeField] private AudioClip _repairCompleteSound;
    [SerializeField] private AudioSource _audioSource;

    /// <summary>
    /// Current damage level. 0 = healthy/fully repaired; higher = more broken.
    /// Authoritative on server, replicated to all clients via NetworkVariable.
    /// </summary>
    private NetworkVariable<int> _damageLevel = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True when this fence segment requires repair.</summary>
    public bool IsBroken => _damageLevel.Value > 0;

    /// <summary>True when this fence is in its default undamaged state.</summary>
    public bool IsRepaired => _damageLevel.Value == 0;

    /// <summary>
    /// The highest valid damage index, derived from the number of mesh root entries.
    /// A fence with four mesh roots supports damage levels 0–3.
    /// </summary>
    public int MaxDamageLevel => _damageStateMeshRoots != null
        ? Mathf.Max(0, _damageStateMeshRoots.Length - 1)
        : 0;

    /// <summary>
    /// Raised on the server when a player's hammer hit reduces this fence's damage to 0.
    /// FenceRepairTask subscribes to this to track overall task progress.
    /// </summary>
    public event Action<PerimiterFence> OnFullyRepaired;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _damageLevel.OnValueChanged += OnDamageLevelChanged;
        ApplyDamageVisuals(_damageLevel.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _damageLevel.OnValueChanged -= OnDamageLevelChanged;
    }

    private void OnDamageLevelChanged(int previous, int current)
    {
        ApplyDamageVisuals(current);

        if (_audioSource == null) return;

        if (current == 0 && previous > 0 && _repairCompleteSound != null)
            _audioSource.PlayOneShot(_repairCompleteSound);
        else if (_hammerHitSound != null)
            _audioSource.PlayOneShot(_hammerHitSound);
    }

    /// <summary>
    /// Activates only the child mesh root that matches the given damage level,
    /// hiding all others. Runs on every client via the NetworkVariable callback.
    /// </summary>
    private void ApplyDamageVisuals(int level)
    {
        if (_damageStateMeshRoots == null) return;

        for (int i = 0; i < _damageStateMeshRoots.Length; i++)
        {
            if (_damageStateMeshRoots[i] != null)
                _damageStateMeshRoots[i].SetActive(i == level);
        }
    }

    /// <summary>
    /// Sets this fence to a specific damage level. Server-only.
    /// Called by FenceRepairTask.ResetTask() at the start of each night phase.
    /// </summary>
    /// <param name="level">Target damage level. Clamped to 0–MaxDamageLevel.</param>
    public void SetDamageLevelServer(int level)
    {
        Debug.Assert(IsServer, "[PerimiterFence] SetDamageLevelServer must be called on the server.");
        _damageLevel.Value = Mathf.Clamp(level, 0, MaxDamageLevel);
    }

    /// <summary>
    /// Registers a single hammer hit on this fence.
    /// Decrements the damage level by one. When it reaches 0, OnFullyRepaired is raised.
    /// Safe to call from any client — ownership is not required.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void HitWithHammerServerRpc()
    {
        if (!IsBroken) return;

        _damageLevel.Value = Mathf.Max(0, _damageLevel.Value - 1);

        if (_damageLevel.Value == 0)
            OnFullyRepaired?.Invoke(this);
    }
}
