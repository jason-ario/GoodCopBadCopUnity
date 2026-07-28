using System.Collections.Generic;
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
        if (_instance == this) _instance = null;
    }

    /// <summary>Registers a pickable so it is included in future captures/restores. Safe to call repeatedly.</summary>
    public void Register(PickableObject pickable)
    {
        if (pickable == null || string.IsNullOrEmpty(pickable.SaveId)) return;
        _pickables[pickable.SaveId] = pickable;
    }

    /// <summary>Unregisters a pickable, e.g. when it despawns.</summary>
    public void Unregister(PickableObject pickable)
    {
        if (pickable == null || string.IsNullOrEmpty(pickable.SaveId)) return;
        _pickables.Remove(pickable.SaveId);
    }

    /// <summary>Captures the current position/rotation of every registered pickable.</summary>
    public PickableObjectSaveData[] CaptureAll()
    {
        var result = new List<PickableObjectSaveData>(_pickables.Count);

        foreach (KeyValuePair<string, PickableObject> kvp in _pickables)
        {
            if (kvp.Value == null) continue;
            result.Add(kvp.Value.CaptureSaveData());
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
}
