using System;
using Unity.Netcode;
using UnityEngine;

public class GlobalHostVariables : NetworkBehaviour
{
    public NetworkVariable<int> money = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Server-authoritative cumulative damage on the booth window glass, in hits.
    /// Owns the single source of truth for BOTH the crack progress ("how broken it is") and the
    /// fully-smashed state, so every peer — including late joiners — renders the same glass and
    /// shows/hides the repair purchase interactable identically.
    ///
    /// Lives here rather than on <see cref="BreakableGlassController"/> because this GameObject is
    /// always active and therefore always has a spawned NetworkObject. The Breakable Glass and
    /// Purchase Glass objects are both deactivated at times (main menu / not-yet-damaged), and NGO
    /// never auto-spawns an inactive in-scene NetworkObject — which is exactly why the previous
    /// ClientRpc-only approach dropped state and let clients diverge.
    ///
    /// Consumers should subscribe to <see cref="GlassHitsChanged"/> rather than reading this
    /// directly, so they also receive the initial value at spawn time.
    /// </summary>
    public NetworkVariable<int> glassHits = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Raised on every peer with the authoritative glass hit count: once when this object spawns
    /// (delivering the replicated value to late joiners) and again on every subsequent change.
    /// Static so <see cref="BreakableGlassController"/> can subscribe before this singleton spawns.
    /// </summary>
    public static event Action<int> GlassHitsChanged;

    /// <summary>True while the networked glass state is live and authoritative (session running).</summary>
    public static bool IsGlassStateNetworked => Instance != null && Instance.IsSpawned;

    /// <summary>The replicated glass hit count, or 0 when there is no live session.</summary>
    public static int CurrentGlassHits => IsGlassStateNetworked ? Instance.glassHits.Value : 0;

    public static GlobalHostVariables Instance;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        glassHits.OnValueChanged += HandleGlassHitsChanged;

        // Push the current value immediately so a late-joining client's BreakableGlassController
        // adopts the host's state instead of its own local default.
        GlassHitsChanged?.Invoke(glassHits.Value);
    }

    public override void OnNetworkDespawn()
    {
        glassHits.OnValueChanged -= HandleGlassHitsChanged;
    }

    private void HandleGlassHitsChanged(int previous, int current) => GlassHitsChanged?.Invoke(current);

    /// <summary>
    /// Server-only. Sets the authoritative glass hit count, replicating it to every client.
    /// No-ops off the server or before this object is spawned, so callers can invoke it
    /// unconditionally (offline play simply keeps its local state).
    /// </summary>
    public void SetGlassHits(int hits)
    {
        if (!IsSpawned || !IsServer) return;

        int clamped = Mathf.Max(0, hits);
        if (glassHits.Value == clamped) return;

        glassHits.Value = clamped;
    }

    public void AddMoney(int total)
    {
        money.Value += total;
        if (money.Value < 0)
        {
            money.Value = 0;
        }
    }

    /// <summary>
    /// Attempts to subtract <paramref name="amount"/> from the shared money pool.
    /// Must only be called on the server. Returns true if funds were sufficient.
    /// </summary>
    public bool SubtractMoney(int amount)
    {
        if (money.Value < amount)
            return false;

        money.Value -= amount;
        return true;
    }

    /// <summary>
    /// Callable from any client. Routes the deduction through a ServerRpc so the
    /// NetworkVariable write always happens on the server.
    /// </summary>
    public void SubtractMoneyFromClient(int amount)
    {
        if (IsServer)
            SubtractMoney(amount);
        else
            SubtractMoneyServerRpc(amount);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubtractMoneyServerRpc(int amount)
    {
        SubtractMoney(amount);
    }

    /// <summary>
    /// Server-only. Forces the shared money pool to an exact value, clamped to non-negative.
    /// Used to restore the coupon total to its last Dusk checkpoint when a death-retry
    /// fast-forwards back into the post-shift phase (see <see cref="ShiftManager.RestartIntoPostShiftPhase"/>).
    /// </summary>
    public void SetMoney(int amount)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GlobalHostVariables] SetMoney called on non-server; ignoring.");
            return;
        }

        money.Value = Mathf.Max(0, amount);
    }
}
