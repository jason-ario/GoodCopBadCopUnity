using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER: protects late-join scene synchronization from destroyed NetworkBehaviours.
///
/// If a NetworkBehaviour component (or the child GameObject carrying it) is destroyed while its
/// NetworkObject stays spawned, and NGO's own cleanup didn't unregister it (e.g. a subclass hides
/// OnDestroy without calling base, or it was destroyed while not flagged spawned), the dead entry
/// stays in NetworkObject.ChildNetworkBehaviours with disposed NetworkVariables. The next client
/// sync then throws "[NetworkVariable][Initialize] NetworkBehaviour parameter passed in is null!"
/// inside HandleConnectionApproval: the joiner is dropped ("lost connection") and its half-added
/// NGO client lingers as a ghost that counts toward the player cap.
///
/// <see cref="PurgeDestroyedBehaviours"/> removes those dead entries right before a client is
/// approved and logs exactly which object/type was affected so the root cause can be fixed.
/// NGO frames each object's sync data with a size prefix, so at worst the late joiner logs a
/// size-mismatch warning for that one object instead of failing the whole join.
/// </summary>
public static class NetworkSyncGuard
{
    private static readonly FieldInfo ChildBehavioursField =
        typeof(NetworkObject).GetField("ChildNetworkBehaviours", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static bool _loggedMissingField;
    private static readonly List<ushort> DeadKeys = new List<ushort>();

    /// <summary>SERVER: removes destroyed behaviours from every spawned NetworkObject. Returns how many were purged.</summary>
    public static int PurgeDestroyedBehaviours(NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsServer || networkManager.SpawnManager == null)
            return 0;

        if (ChildBehavioursField == null)
        {
            if (!_loggedMissingField)
            {
                _loggedMissingField = true;
                Debug.LogWarning("[NetworkSyncGuard] NetworkObject.ChildNetworkBehaviours not found (NGO version changed?) — guard disabled.");
            }
            return 0;
        }

        int purged = 0;
        foreach (NetworkObject netObj in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null)
                continue;

            if (ChildBehavioursField.GetValue(netObj) is not Dictionary<ushort, NetworkBehaviour> behaviours || behaviours.Count == 0)
                continue;

            DeadKeys.Clear();
            foreach (KeyValuePair<ushort, NetworkBehaviour> pair in behaviours)
            {
                if (pair.Value == null) // Unity-null: destroyed component.
                    DeadKeys.Add(pair.Key);
            }

            foreach (ushort key in DeadKeys)
            {
                string typeName = ReferenceEquals(behaviours[key], null) ? "<null>" : behaviours[key].GetType().Name;
                Debug.LogWarning($"[NetworkSyncGuard] Purged destroyed {typeName} (behaviourId={key}) from still-spawned " +
                                 $"NetworkObject '{netObj.name}' (id={netObj.NetworkObjectId}, sceneObject={netObj.IsSceneObject}). " +
                                 "Something destroyed this component without despawning its NetworkObject.");
                behaviours.Remove(key);
                purged++;
            }
        }

        return purged;
    }
}
