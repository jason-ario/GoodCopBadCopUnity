using System;
using Unity.Netcode;
using UnityEngine;

public class GlobalHostVariables : NetworkBehaviour
{
    public NetworkVariable<int> money = new NetworkVariable<int>();
    
    public static GlobalHostVariables Instance;

    private void Awake()
    {
        Instance = this;
    }
}
