using System;
using Unity.Netcode;

/// <summary>
/// Scene-placed <see cref="NetworkBehaviour"/> that relays Day 1 tutorial task-state
/// transitions from whoever triggers them (any client) to every connected client.
///
/// Pattern:
///   1. Any client detects a local interaction (document pickup, folder equip, etc.)
///      and calls the matching <c>Report…ServerRpc</c>.
///   2. The server validates / de-duplicates, then broadcasts a <c>…ClientRpc</c>.
///   3. The ClientRpc fires the corresponding static C# event on all clients.
///   4. <see cref="Day_01"/> subscribes to those events and updates the
///      <see cref="TaskRegistry"/> identically on every machine.
/// </summary>
public class TutorialTaskSync : NetworkBehaviour
{
    public static TutorialTaskSync Instance { get; private set; }

    // ── Static events fired on ALL clients via ClientRpc ──────────────────────

    /// <summary>Fired on all clients once the server confirms both Vlad docs were picked up.</summary>
    public static event Action OnVladDocsBothPickedUpAllClients;

    /// <summary>Fired on all clients when the Ivan documentation tutorial begins.</summary>
    public static event Action OnIvanDocumentTutorialStartedAllClients;

    /// <summary>Fired on all clients when any player picks up an exam notebook during the tutorial.</summary>
    public static event Action OnExamNotebookPickedUpAllClients;

    /// <summary>Fired on all clients when any exam notebook page is filed during the tutorial.</summary>
    public static event Action OnExamPageFiledAllClients;

    // ── Server-side counters / guards ─────────────────────────────────────────

    private int  _vladDocsPickedUpCount;
    private bool _ivanTutorialBroadcast;
    private bool _examPickedUpBroadcast;

    // ── NetworkBehaviour lifecycle ────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all server-side counters and guards.
    /// Call from the server at the start of Day 1 (or when retrying the day).
    /// </summary>
    public void ResetServerState()
    {
        if (!IsServer) return;
        _vladDocsPickedUpCount = 0;
        _ivanTutorialBroadcast = false;
        _examPickedUpBroadcast = false;
    }

    // ── Vlad document pickup ──────────────────────────────────────────────────

    /// <summary>
    /// Called by any client whenever they pick up one of Vlad's tutorial documents.
    /// The server counts globally across all clients; once the total reaches two it
    /// broadcasts <see cref="OnVladDocsBothPickedUpAllClients"/> to every client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportVladDocPickedUpServerRpc()
    {
        _vladDocsPickedUpCount++;
        if (_vladDocsPickedUpCount >= 2)
            BroadcastVladDocsBothPickedUpClientRpc();
    }

    [ClientRpc]
    private void BroadcastVladDocsBothPickedUpClientRpc()
    {
        OnVladDocsBothPickedUpAllClients?.Invoke();
    }

    // ── Ivan documentation tutorial ───────────────────────────────────────────

    /// <summary>
    /// Called by any client when the Ivan documentation tutorial is triggered.
    /// The server ensures the broadcast fires exactly once even if multiple clients report it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportIvanTutorialTriggerServerRpc()
    {
        if (_ivanTutorialBroadcast) return;
        _ivanTutorialBroadcast = true;
        BroadcastIvanTutorialStartedClientRpc();
    }

    [ClientRpc]
    private void BroadcastIvanTutorialStartedClientRpc()
    {
        OnIvanDocumentTutorialStartedAllClients?.Invoke();
    }

    // ── Exam notebook pickup ──────────────────────────────────────────────────

    /// <summary>
    /// Called by any client when they pick up an exam notebook during the tutorial.
    /// The server ensures the broadcast fires exactly once.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportExamPickedUpServerRpc()
    {
        if (_examPickedUpBroadcast) return;
        _examPickedUpBroadcast = true;
        BroadcastExamPickedUpClientRpc();
    }

    [ClientRpc]
    private void BroadcastExamPickedUpClientRpc()
    {
        // Set the static flag so the server-side WaitUntil in IvanDocumentationBarkRoutine
        // unblocks regardless of which client picked up the notebook.
        ExamNotebook.AnyExamNotebookPickedUp = true;
        OnExamNotebookPickedUpAllClients?.Invoke();
    }

    // ── Exam page filed ───────────────────────────────────────────────────────

    /// <summary>
    /// Called by any client when an exam notebook page is filed into a folder during the tutorial.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportExamPageFiledServerRpc()
    {
        BroadcastExamPageFiledClientRpc();
    }

    [ClientRpc]
    private void BroadcastExamPageFiledClientRpc()
    {
        ExamNotebook.AnyPageFiled = true;
        OnExamPageFiledAllClients?.Invoke();
    }
}
