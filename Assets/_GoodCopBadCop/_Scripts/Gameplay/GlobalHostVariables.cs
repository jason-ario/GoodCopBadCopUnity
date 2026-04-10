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

        UIController.Instance.PlayEarnedCashUIAnimation(total);
    }
}
