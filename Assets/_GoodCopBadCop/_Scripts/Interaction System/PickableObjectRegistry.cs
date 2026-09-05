using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks every live <see cref="PickableObject"/> in the scene so their transforms can be
/// captured to, and restored from, the active save file. Used for:
///   - general save/load persistence (see <see cref="SaveDataManager.SaveDuskCheckpoint"/>), and
///   - resetting pickables back to their last checkpoint position when a player dies and
///     retries, instead of leaving them wherever they were thrown/dropped mid-attempt.
///
/// Registration is automatic — every <see cref="PickableObject"/> registers itself in
/// OnNetworkSpawn and unregisters in OnNetworkDespawn. No manual scene setup required.
/// Self-instantiates on first access, mirroring <see cref="TaskRegistry"/>.
/// </summary>
public class PickableObjectRegistry : MonoBehaviour
{
    public static PickableObjectRegistry Instance => GetOrCreate();

    private static PickableObjectRegistry _instance;

    private readonly Dictionary<string, PickableObject> _pickables = new();
    private readonly HashSet<string> _knownPickableIds = new();

    private static PickableObjectRegistry GetOrCreate()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<PickableObjectRegistry>();
        if (_instance != null) return _instance;

        var go = new GameObject("[PickableObjectRegistry]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<PickableObjectRegistry>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        UnsubscribeFromDisconnects();
        if (_instance == this) _instance = null;
    }

    /// <summary>Registers a pickable so it is included in future captures/restores. Safe to call repeatedly.</summary>
    public void Register(PickableObject pickable)
    {
        if (pickable == null || string.IsNullOrEmpty(pickable.SaveId)) return;
        _pickables[pickable.SaveId] = pickable;
        _knownPickableIds.Add(pickable.SaveId);

        // Hook the disconnect sweep here rather than in Awake: this registry self-instantiates
        // before NetworkManager.Singleton necessarily exists, whereas a pickable registering
        // always means the network session is up.
        SubscribeToDisconnects();
    }

    /// <summary>Unregisters a pickable, e.g. when it despawns.</summary>
    public void Unregister(PickableObject pickable)
    {
        if (pickable == null || string.IsNullOrEmpty(pickable.SaveId)) return;
        _pickables.Remove(pickable.SaveId);
    }

    /// <summary>
    /// Captures the current transform and existence of every pickable observed this session.
    /// Keeping tombstones for despawned scene items prevents a scene reload from silently
    /// recreating an item that was consumed earlier in the workday.
    /// </summary>
    public PickableObjectSaveData[] CaptureAll()
    {
        var result = new List<PickableObjectSaveData>(_knownPickableIds.Count);
        var liveIds = new HashSet<string>();

        foreach (KeyValuePair<string, PickableObject> kvp in _pickables)
        {
            if (kvp.Value == null) continue;
            liveIds.Add(kvp.Key);
            result.Add(kvp.Value.CaptureSaveData());
        }

        foreach (string id in _knownPickableIds)
        {
            if (liveIds.Contains(id)) continue;
            result.Add(new PickableObjectSaveData
            {
                HasExistenceState = true,
                Exists = false,
                Id = id
            });
        }

        return result.ToArray();
    }

    /// <summary>
    /// Restores every registered pickable that has a matching saved entry to its saved
    /// position/rotation. Entries for pickables no longer present in the scene are ignored.
    /// Actual transform writes only happen server-side — see <see cref="PickableObject.ApplySaveData"/>.
    /// </summary>
    public void RestoreAll(PickableObjectSaveData[] data)
    {
        if (data == null || data.Length == 0) return;

        int restoredCount = 0;
        foreach (PickableObjectSaveData entry in data)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
            if (!_pickables.TryGetValue(entry.Id, out PickableObject pickable) || pickable == null) continue;

            pickable.ApplySaveData(entry);
            restoredCount++;
        }

        Debug.Log($"[PickableObjectRegistry] Restored {restoredCount}/{data.Length} pickable object(s) from checkpoint.");
    }

    // ── Disconnect rescue ─────────────────────────────────────────────────────

    private bool _subscribedToDisconnects;

    private void SubscribeToDisconnects()
    {
        if (_subscribedToDisconnects) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        nm.OnClientDisconnectCallback += OnClientDisconnected;
        _subscribedToDisconnects = true;
    }

    private void UnsubscribeFromDisconnects()
    {
        if (!_subscribedToDisconnects) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null) nm.OnClientDisconnectCallback -= OnClientDisconnected;
        _subscribedToDisconnects = false;
    }

    /// <summary>
    /// Server-side rescue for items a leaving player was carrying. A disconnecting client can no
    /// longer deliver its own release/drop ServerRpcs (the ones
    /// <see cref="PlayerInventory.OnNetworkDespawn"/> attempts), so anything it held or had
    /// stowed stayed pinned to a client id that never returns — invisible if stowed, and
    /// unpickable either way. Hand every such item back to the world instead.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // The host also receives this callback for its own shutdown; nothing to rescue then.
        if (clientId == nm.LocalClientId) return;

        int releasedCount = 0;
        foreach (PickableObject pickable in new List<PickableObject>(_pickables.Values))
        {
            if (pickable == null || !pickable.IsSpawned) continue;
            if (pickable.HolderClientId != clientId) continue;

            pickable.ForceReleaseToWorldServer();
            releasedCount++;
        }

        if (releasedCount > 0)
            Debug.Log($"[PickableObjectRegistry] Released {releasedCount} item(s) back to the world after client {clientId} disconnected.");
    }
}
