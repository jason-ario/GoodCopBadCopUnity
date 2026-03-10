using System;
using Unity.Netcode;
using UnityEngine;

public class NetworkHelper : MonoBehaviour
{
    public static NetworkHelper Instance;

    private void Awake()
    {
        Instance = this;
    }

    public static void DespawnWithChildren(NetworkObject netObj)
    {
        if (netObj == null || !netObj.IsSpawned) return;

        // Get all nested NetworkObjects in children
        var childNetworkObjects = netObj.GetComponentsInChildren<NetworkObject>();
        
        // Despawn children first (excluding the parent itself to avoid early destruction)
        for (int i = childNetworkObjects.Length - 1; i >= 0; i--)
        {
            if (childNetworkObjects[i] != netObj && childNetworkObjects[i].IsSpawned)
            {
                childNetworkObjects[i].Despawn();
            }
        }

        // Finally despawn the parent
        netObj.Despawn();
    }
}
