using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Catch-all sweep that removes every runtime blood splatter so none carries over into the next
/// day, regardless of which system spawned it (mutant gore, gore landing decals, trash task,
/// booth mess, follow-trail end splatters and UV trail marks).
///
/// Every blood splatter prefab (<c>Random Blood Splatter Variant</c>, <c>Invisible Blood</c>)
/// carries a <see cref="BloodTextureRandomizer"/>, which is used as the marker. The removable
/// unit is the owning <see cref="NetworkObject"/> when there is one, otherwise the GameObject
/// holding the randomizer.
///
/// Must run on EVERY peer: the server despawns spawned NetworkObjects (replicated to clients),
/// while each peer destroys its own local-only instances (cosmetic landing decals and trail
/// marks are never network-spawned). In-scene placed objects are never touched.
/// </summary>
public static class BloodSplatterCleanup
{
    /// <returns>Number of splatters removed on this peer.</returns>
    public static int ClearAllForDayTransition()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool isServer = nm == null || !nm.IsListening || nm.IsServer;

        var targets = new HashSet<GameObject>();
        BloodTextureRandomizer[] markers =
            Object.FindObjectsByType<BloodTextureRandomizer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BloodTextureRandomizer marker in markers)
        {
            if (marker == null) continue;
            NetworkObject netObj = marker.GetComponentInParent<NetworkObject>();
            targets.Add(netObj != null ? netObj.gameObject : marker.gameObject);
        }

        int count = 0;
        foreach (GameObject target in targets)
        {
            if (target == null) continue;

            if (target.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
            {
                // Spawned copies are owned by the server; clients receive the despawn.
                if (!isServer || netObj.IsSceneObject == true) continue;
                netObj.Despawn(destroy: true);
                count++;
                continue;
            }

            // Local-only instance (never spawned). Skip anything authored in the scene file.
            if (netObj != null && netObj.IsSceneObject == true) continue;
            if (netObj == null && IsAuthoredSceneObject(target)) continue;

            Object.Destroy(target);
            count++;
        }

        if (count > 0)
            Debug.Log($"[BloodSplatterCleanup] Day transition — removed {count} blood splatter(s).");
        return count;
    }

    // Runtime Instantiate() clones are named "(Clone)"; authored scene placements are not.
    private static bool IsAuthoredSceneObject(GameObject go) => !go.name.EndsWith("(Clone)");
}
