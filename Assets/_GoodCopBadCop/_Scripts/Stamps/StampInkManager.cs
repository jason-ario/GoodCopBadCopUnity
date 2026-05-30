using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Singleton NetworkBehaviour that tracks remaining ink uses for each limited stamp type.
/// - Pass: always infinite
/// - Quarantine: starts at 3 (max 3)
/// - Kill: starts at 2 (max 2)
///
/// Call <see cref="ConsumeInk"/> on the server before confirming a stamp action.
/// Call <see cref="AddInk"/> to refill uses when the player purchases more ink.
/// </summary>
public class StampInkManager : NetworkBehaviour
{
    public static StampInkManager Instance { get; private set; }

    [Header("Max Uses Per Type")]
    [SerializeField] private int quarantineMaxUses = 3;
    [SerializeField] private int killMaxUses = 2;

    private NetworkVariable<int> _quarantineUses = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<int> _killUses = new NetworkVariable<int>(
        2,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Raised on all clients whenever an ink count changes. Passes the stamp type and new count.
    /// </summary>
    public static event UnityAction<StampContainer.StampType, int> OnInkChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _quarantineUses.OnValueChanged += OnQuarantineUsesChanged;
        _killUses.OnValueChanged += OnKillUsesChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _quarantineUses.OnValueChanged -= OnQuarantineUsesChanged;
        _killUses.OnValueChanged -= OnKillUsesChanged;
    }

    private void OnQuarantineUsesChanged(int prev, int current)
        => OnInkChanged?.Invoke(StampContainer.StampType.Quarantine, current);

    private void OnKillUsesChanged(int prev, int current)
        => OnInkChanged?.Invoke(StampContainer.StampType.Kill, current);

    /// <summary>
    /// Returns remaining uses for the given stamp type.
    /// Returns -1 for Pass (infinite).
    /// </summary>
    public int GetUses(StampContainer.StampType type)
    {
        return type switch
        {
            StampContainer.StampType.Quarantine => _quarantineUses.Value,
            StampContainer.StampType.Kill       => _killUses.Value,
            _                                   => -1,
        };
    }

    /// <summary>Returns true when at least one use remains. Pass is always true.</summary>
    public bool HasInk(StampContainer.StampType type)
        => type == StampContainer.StampType.Pass || GetUses(type) > 0;

    /// <summary>
    /// Consumes one use of the given stamp type. Must be called server-side.
    /// Returns true on success, false when no uses remain or called on a client.
    /// Pass always returns true without decrementing anything.
    /// </summary>
    public bool ConsumeInk(StampContainer.StampType type)
    {
        if (!IsServer) return false;
        if (type == StampContainer.StampType.Pass) return true;

        switch (type)
        {
            case StampContainer.StampType.Quarantine when _quarantineUses.Value > 0:
                _quarantineUses.Value--;
                return true;
            case StampContainer.StampType.Kill when _killUses.Value > 0:
                _killUses.Value--;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Adds ink uses for the given stamp type, capped at the configured maximum.
    /// Must be called server-side (e.g. from a shop purchase handler).
    /// </summary>
    public void AddInk(StampContainer.StampType type, int amount)
    {
        if (!IsServer) return;

        switch (type)
        {
            case StampContainer.StampType.Quarantine:
                _quarantineUses.Value = Mathf.Min(_quarantineUses.Value + amount, quarantineMaxUses);
                break;
            case StampContainer.StampType.Kill:
                _killUses.Value = Mathf.Min(_killUses.Value + amount, killMaxUses);
                break;
        }
    }

    /// <summary>
    /// Returns the maximum allowed uses for the given stamp type.
    /// Returns -1 for Pass (infinite).
    /// </summary>
    public int GetMaxUses(StampContainer.StampType type)
    {
        return type switch
        {
            StampContainer.StampType.Quarantine => quarantineMaxUses,
            StampContainer.StampType.Kill       => killMaxUses,
            _                                   => -1,
        };
    }

    /// <summary>Resets all ink counts to their defaults. Must be called server-side.</summary>
    public void ResetAll()
    {
        if (!IsServer) return;
        _quarantineUses.Value = quarantineMaxUses;
        _killUses.Value = killMaxUses;
    }
}
