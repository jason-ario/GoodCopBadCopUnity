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
    
    public static GlobalHostVariables Instance;

    private void Awake()
    {
        Instance = this;
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
