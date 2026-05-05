using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the between-shift night-phase task list.
/// Tracks task completion and broadcasts readiness to all clients via NetworkVariable.
/// </summary>
public class BetweenShiftTaskManager : NetworkBehaviour
{
    public static BetweenShiftTaskManager Instance;

    /// <summary>
    /// Fired on all clients when every registered task has been completed.
    /// ShiftManager subscribes to this to trigger the shift-start button flash and announcer line.
    /// </summary>
    public static event Action OnAllTasksComplete;

    private readonly NetworkVariable<bool> _allTasksComplete = new(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool AllTasksComplete => _allTasksComplete.Value;

    /// <summary>
    /// Assign all IBetweenShiftTask MonoBehaviours here via the Inspector.
    /// Each entry must implement IBetweenShiftTask.
    /// </summary>
    [SerializeField] private MonoBehaviour[] _taskBehaviours;

    private IBetweenShiftTask[] _tasks;
    private int _completedTaskCount;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        BuildTaskList();
        _allTasksComplete.OnValueChanged += OnAllTasksCompleteChanged;
    }

    public override void OnNetworkDespawn()
    {
        _allTasksComplete.OnValueChanged -= OnAllTasksCompleteChanged;
    }

    private void BuildTaskList()
    {
        _tasks = new IBetweenShiftTask[_taskBehaviours.Length];
        for (int i = 0; i < _taskBehaviours.Length; i++)
        {
            _tasks[i] = _taskBehaviours[i] as IBetweenShiftTask;
            if (_tasks[i] == null)
                Debug.LogWarning($"[BetweenShiftTaskManager] Entry {i} ({_taskBehaviours[i]?.name}) does not implement IBetweenShiftTask.");
        }
    }

    private void OnAllTasksCompleteChanged(bool oldValue, bool newValue)
    {
        if (newValue)
            OnAllTasksComplete?.Invoke();
    }

    /// <summary>
    /// Resets all tasks and begins tracking completion for the new night phase.
    /// Must be called on the server only.
    /// </summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        _completedTaskCount = 0;
        _allTasksComplete.Value = false;

        foreach (var task in _tasks)
            task?.ResetTask();
    }

    /// <summary>
    /// Called by individual task scripts when their task is completed.
    /// Safe to call from both server and clients.
    /// </summary>
    public void NotifyTaskComplete(IBetweenShiftTask task)
    {
        if (IsServer)
            HandleTaskCompleted();
        else
            NotifyTaskCompleteServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyTaskCompleteServerRpc()
    {
        HandleTaskCompleted();
    }

    private void HandleTaskCompleted()
    {
        if (!IsServer) return;
        if (_allTasksComplete.Value) return;

        _completedTaskCount++;
        Debug.Log($"[BetweenShiftTaskManager] Tasks completed: {_completedTaskCount} / {_tasks.Length}");

        if (_completedTaskCount >= _tasks.Length)
            _allTasksComplete.Value = true;
    }
}
