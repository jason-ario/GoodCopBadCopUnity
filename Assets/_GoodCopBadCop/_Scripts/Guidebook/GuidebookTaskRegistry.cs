using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry for all systemic threats shown in the guidebook task list.
/// Threats can be registered or updated at any time by any system.
/// Self-instantiates on first access so no manual scene placement is needed.
/// </summary>
public class GuidebookTaskRegistry : MonoBehaviour
{
    public static GuidebookTaskRegistry Instance => GetOrCreate();

    /// <summary>Fired whenever the threat list changes (added, removed, replaced, or cleared).</summary>
    public static event Action OnTaskListChanged;

    /// <summary>
    /// Fired only when one or more threats are added to the registry.
    /// GuidebookIcon subscribes to this to show the notification badge.
    /// </summary>
    public static event Action OnTasksAdded;

    /// <summary>
    /// Fired when a threat's state changes without the list itself changing.
    /// GuidebookTaskRow subscribes to refresh its labels without rebuilding rows.
    /// </summary>
    public static event Action OnTaskStateChanged;

    private readonly List<ISystemicThreat> _threats = new();

    /// <summary>Read-only snapshot of the current threat list.</summary>
    public IReadOnlyList<ISystemicThreat> Threats => _threats;

    private static GuidebookTaskRegistry _instance;

    private static GuidebookTaskRegistry GetOrCreate()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<GuidebookTaskRegistry>();
        if (_instance != null) return _instance;

        var go = new GameObject("[GuidebookTaskRegistry]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<GuidebookTaskRegistry>();
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

    // ── Threat management ─────────────────────────────────────────────────────

    /// <summary>Adds a threat to the registry and fires OnTaskListChanged and OnTasksAdded.</summary>
    public void AddThreat(ISystemicThreat threat)
    {
        if (threat == null || _threats.Contains(threat)) return;
        _threats.Add(threat);
        OnTaskListChanged?.Invoke();
        OnTasksAdded?.Invoke();
        Debug.Log($"[GuidebookTaskRegistry] Threat added: '{threat.ThreatName}'. Total: {_threats.Count}");
    }

    /// <summary>Removes a threat from the registry and fires OnTaskListChanged.</summary>
    public void RemoveThreat(ISystemicThreat threat)
    {
        if (threat == null || !_threats.Contains(threat)) return;
        _threats.Remove(threat);
        OnTaskListChanged?.Invoke();
        Debug.Log($"[GuidebookTaskRegistry] Threat removed: '{threat.ThreatName}'. Total: {_threats.Count}");
    }

    /// <summary>
    /// Replaces the entire threat list and fires OnTaskListChanged.
    /// Also fires OnTasksAdded if the new list is non-empty.
    /// Null entries and duplicate references are silently skipped.
    /// </summary>
    public void SetThreats(IEnumerable<ISystemicThreat> threats)
    {
        _threats.Clear();

        if (threats != null)
        {
            foreach (ISystemicThreat t in threats)
                if (t != null && !_threats.Contains(t)) _threats.Add(t);
        }

        OnTaskListChanged?.Invoke();

        if (_threats.Count > 0)
            OnTasksAdded?.Invoke();

        Debug.Log($"[GuidebookTaskRegistry] Threat list set. Total: {_threats.Count}");
    }

    /// <summary>Clears all threats and fires OnTaskListChanged.</summary>
    public void ClearThreats()
    {
        _threats.Clear();
        OnTaskListChanged?.Invoke();
        Debug.Log("[GuidebookTaskRegistry] Threat list cleared.");
    }

    /// <summary>
    /// Call this when a threat's state changes so the guidebook can refresh
    /// labels without rebuilding the row list.
    /// </summary>
    public void NotifyTaskStateChanged()
    {
        Debug.Log($"[GuidebookTaskRegistry] NotifyTaskStateChanged fired. Subscriber count: {OnTaskStateChanged?.GetInvocationList().Length ?? 0}");
        OnTaskStateChanged?.Invoke();
    }

    // ── Backward-compatibility stubs ──────────────────────────────────────────

    /// <summary>Obsolete. No-op — task-based registry is replaced by SetThreats/AddThreat.</summary>
    [System.Obsolete("Use AddThreat(ISystemicThreat) instead.")]
    public void AddTask(IBetweenShiftTask task)
    {
        Debug.LogWarning("[GuidebookTaskRegistry] AddTask is obsolete and has no effect. Use AddThreat() with ISystemicThreat.");
    }

    /// <summary>Obsolete. No-op stub.</summary>
    [System.Obsolete("Use RemoveThreat(ISystemicThreat) instead.")]
    public void RemoveTask(IBetweenShiftTask task) { }

    /// <summary>Obsolete. No-op stub.</summary>
    [System.Obsolete("Use SetThreats(IEnumerable<ISystemicThreat>) instead.")]
    public void SetTasks(IEnumerable<IBetweenShiftTask> tasks) { }

    /// <summary>Obsolete. No-op stub.</summary>
    [System.Obsolete("Use ClearThreats() instead.")]
    public void ClearTasks() { }
}
