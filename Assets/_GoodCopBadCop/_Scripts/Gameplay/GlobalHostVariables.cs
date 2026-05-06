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
}
