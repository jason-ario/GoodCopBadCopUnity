using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>One replicated objective row: host-assigned id, source text, completion state.</summary>
public struct ObjectiveRowState : INetworkSerializable, IEquatable<ObjectiveRowState>
{
    public int Id;
    public FixedString512Bytes Text;
    public bool Completed;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref Text);
        serializer.SerializeValue(ref Completed);
    }

    public bool Equals(ObjectiveRowState other) =>
        Id == other.Id && Completed == other.Completed && Text.Equals(other.Text);

    public override bool Equals(object obj) => obj is ObjectiveRowState other && Equals(other);
    public override int GetHashCode() => Id;
}

/// <summary>
/// Host-authoritative mirror of the HUD objective list (<see cref="TutorialObjectiveList"/>).
///
/// Why: day-script objective transitions (Day 1 tutorial, Day 2 mutation tutorial, ...) reach
/// clients only through one-shot ClientRpcs, so a late joiner replayed <c>DayActivated</c> from the
/// top and showed the day's opening objectives instead of the host's progress. Now:
///   - The host's list publishes every row add / text change / completion / removal into
///     <see cref="_rows"/> (a NetworkList, so late joiners receive the current rows on spawn).
///   - Every non-host client DISPLAYS only these rows. Its own day scripts keep running and keep
///     calling <see cref="TutorialObjectiveList"/> (so callbacks and step chaining still work), but
///     those calls produce hidden, silent shadow rows.
///
/// Scene setup: a dedicated GameObject with a NetworkObject + this component, next to
/// "--- Day 02 Network Sync" under ---CampaignManager.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class ObjectiveListSync : NetworkBehaviour
{
    public static ObjectiveListSync Instance { get; private set; }

    /// <summary>True on a connected non-host client: the local objective list mirrors the host's.</summary>
    public static bool IsMirroringLocally => Instance != null && Instance.IsSpawned && !Instance.IsServer;

    private NetworkList<ObjectiveRowState> _rows;

    private void Awake()
    {
        _rows = new NetworkList<ObjectiveRowState>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;

        if (IsServer)
        {
            _rows.Clear();
            TutorialObjectiveList.Instance?.PublishAllToSync();
            return;
        }

        _rows.OnListChanged += OnRowsChanged;
        TutorialObjectiveList.Instance?.EnterMirrorMode(Snapshot());
        Debug.Log($"[ObjectiveListSync] Client mirroring host objectives — {_rows.Count} row(s) on join.");
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            _rows.OnListChanged -= OnRowsChanged;
            TutorialObjectiveList.Instance?.ExitMirrorMode();
        }

        if (Instance == this) Instance = null;
        base.OnNetworkDespawn();
    }

    // ── Host publishing ───────────────────────────────────────────────────────

    /// <summary>Host-only: inserts or updates the row with <paramref name="id"/>. No-op elsewhere.</summary>
    public static void PublishUpsert(int id, string text, bool completed)
    {
        if (Instance == null || !Instance.IsSpawned || !Instance.IsServer || id == 0) return;

        var state = new ObjectiveRowState { Id = id, Completed = completed };
        state.Text.CopyFromTruncated(text ?? string.Empty);

        NetworkList<ObjectiveRowState> rows = Instance._rows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id != id) continue;
            if (!rows[i].Equals(state)) rows[i] = state;
            return;
        }
        rows.Add(state);
    }

    /// <summary>Host-only: removes the row with <paramref name="id"/>. No-op elsewhere.</summary>
    public static void PublishRemove(int id)
    {
        if (Instance == null || !Instance.IsSpawned || !Instance.IsServer || id == 0) return;

        NetworkList<ObjectiveRowState> rows = Instance._rows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id != id) continue;
            rows.RemoveAt(i);
            return;
        }
    }

    // ── Client mirroring ──────────────────────────────────────────────────────

    private void OnRowsChanged(NetworkListEvent<ObjectiveRowState> _) =>
        TutorialObjectiveList.Instance?.ApplyMirror(Snapshot(), playCues: true);

    private List<TutorialObjectiveList.MirrorRow> Snapshot()
    {
        var list = new List<TutorialObjectiveList.MirrorRow>(_rows.Count);
        foreach (ObjectiveRowState row in _rows)
            list.Add(new TutorialObjectiveList.MirrorRow(row.Id, row.Text.ToString(), row.Completed));
        return list;
    }
}
