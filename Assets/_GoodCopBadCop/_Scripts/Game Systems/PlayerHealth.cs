using Unity.Netcode;
using UnityEngine.Events;

/// <summary>
/// Networked player health component.
/// The server is the sole writer of <see cref="_networkHealth"/>; all clients receive
/// the updated value via <see cref="NetworkVariable{T}"/> replication and fire the
/// local <see cref="OnHealthChanged"/> / <see cref="OnDeath"/> events so UI stays in sync.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const float DefaultMaxHealth = 100f;

    public float MaxHealth => DefaultMaxHealth;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Fired on every client whenever health changes.</summary>
    public UnityAction OnHealthChanged;

    /// <summary>Fired on every client when health reaches zero for the first time.</summary>
    public UnityAction OnDeath;

    // ── Networked State ────────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkHealth = new NetworkVariable<float>(
        DefaultMaxHealth,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _networkIsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local accessors ────────────────────────────────────────────────────────

    /// <summary>Current health, readable on all clients.</summary>
    public float Health => _networkHealth.Value;

    /// <summary>Whether this player is dead, readable on all clients.</summary>
    public bool IsDead => _networkIsDead.Value;

    /// <summary>When true, all incoming damage is ignored. Server-side only.</summary>
    public bool IsInvincible { get; set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkHealth.OnValueChanged += HandleHealthChanged;
        _networkIsDead.OnValueChanged += HandleDeadChanged;

        // Sync initial state immediately on late-joining clients.
        OnHealthChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _networkHealth.OnValueChanged -= HandleHealthChanged;
        _networkIsDead.OnValueChanged -= HandleDeadChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reduces health by the given amount.
    /// Can be called from any client — routes through a ServerRpc when not on the server.
    /// </summary>
    public void TakeDamage(float damage)
    {
        if (IsServer)
            ApplyDamageServer(damage);
        else
            TakeDamageServerRpc(damage);
    }

    /// <summary>Restores health by the given amount. Has no effect while the player is dead.</summary>
    public void Heal(float healAmount)
    {
        if (IsServer)
            ApplyHealServer(healAmount);
        else
            HealServerRpc(healAmount);
    }

    /// <summary>Resets health to max and clears the dead state.</summary>
    public void ResetHealth()
    {
        if (IsServer)
            ApplyResetServer();
        else
            ResetHealthServerRpc();
    }

    // ── ServerRpcs ─────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(float damage)
    {
        ApplyDamageServer(damage);
    }

    [ServerRpc(RequireOwnership = false)]
    private void HealServerRpc(float healAmount)
    {
        ApplyHealServer(healAmount);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResetHealthServerRpc()
    {
        ApplyResetServer();
    }

    // ── Server-only logic ──────────────────────────────────────────────────────

    private void ApplyDamageServer(float damage)
    {
        if (_networkIsDead.Value)
            return;

        if (IsInvincible)
            return;

        _networkHealth.Value = UnityEngine.Mathf.Clamp(_networkHealth.Value - damage, 0f, DefaultMaxHealth);

        if (_networkHealth.Value <= 0f)
            _networkIsDead.Value = true;
    }

    private void ApplyHealServer(float healAmount)
    {
        if (_networkIsDead.Value)
            return;

        _networkHealth.Value = UnityEngine.Mathf.Clamp(_networkHealth.Value + healAmount, 0f, DefaultMaxHealth);
    }

    private void ApplyResetServer()
    {
        _networkIsDead.Value = false;
        _networkHealth.Value = DefaultMaxHealth;
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────────

    private void HandleHealthChanged(float previousValue, float newValue)
    {
        OnHealthChanged?.Invoke();
    }

    private void HandleDeadChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            OnHealthChanged?.Invoke();
            OnDeath?.Invoke();
        }
    }
}
