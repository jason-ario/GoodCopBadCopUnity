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

    /// <summary>
    /// Fired on all clients after the post-clock-in megaphone dialogue completes,
    /// signalling that it is time to show the "Press the button" task and arm the switch.
    /// </summary>
    public static event Action OnPressButtonReadyAllClients;

    /// <summary>
    /// Fired on ALL clients once the clock-in nag dialogue finishes and the time card machine
    /// is enabled. Signals that it is time to show the clock-in task and marker.
    /// </summary>
    public static event Action OnClockInReadyAllClients;

    /// <summary>
    /// Fired on all clients once the server confirms the tutorial folder was placed on the
    /// desk placement board. <see cref="PlacementBoard.OnItemPlaced"/> only fires locally on
    /// whichever client performs the drop, so this relay is required to keep the task list and
    /// tutorial arrow in sync for every connected client.
    /// </summary>
    public static event Action OnFolderPlacedOnDeskAllClients;

    /// <summary>
    /// Fired on all clients once the server confirms the stamped folder was placed on the
    /// window hand-off placement board, carrying a reference to the folder so the server can
    /// lock it and start the closing dialogue regardless of which client performed the drop.
    /// </summary>
    public static event Action<NetworkObjectReference> OnFolderHandedToVladAllClients;

    // ── Server-side counters / guards ─────────────────────────────────────────

    private int  _vladDocsPickedUpCount;
    private bool _ivanTutorialBroadcast;
    private bool _examPickedUpBroadcast;
    private bool _deskFolderPlacedBroadcast;
    private bool _windowHandOffBroadcast;

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
        _deskFolderPlacedBroadcast = false;
        _windowHandOffBroadcast = false;
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

    // ── Press-button ready ────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Called after the post-clock-in megaphone dialogue finishes.
    /// Broadcasts to all clients that it is time to show the "Press the button" task
    /// and arm the switch button.
    /// </summary>
    public void BroadcastPressButtonReadyServer()
    {
        if (!IsServer) return;
        BroadcastPressButtonReadyClientRpc();
    }

    [ClientRpc]
    private void BroadcastPressButtonReadyClientRpc()
    {
        OnPressButtonReadyAllClients?.Invoke();
    }

    // ── Clock-in ready ────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Called after the clock-in nag dialogue finishes and the time card
    /// machine has been enabled. Broadcasts to all clients that the clock-in task
    /// and marker should now be shown.
    /// </summary>
    public void BroadcastClockInReadyServer()
    {
        if (!IsServer) return;
        BroadcastClockInReadyClientRpc();
    }

    [ClientRpc]
    private void BroadcastClockInReadyClientRpc()
    {
        OnClockInReadyAllClients?.Invoke();
    }

    // ── Folder placed on desk ─────────────────────────────────────────────────

    /// <summary>
    /// Called by whichever client places the tutorial folder on the desk placement board.
    /// The server ensures the broadcast fires exactly once even if reported more than once.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportFolderPlacedOnDeskServerRpc()
    {
        if (_deskFolderPlacedBroadcast) return;
        _deskFolderPlacedBroadcast = true;
        BroadcastFolderPlacedOnDeskClientRpc();
    }

    [ClientRpc]
    private void BroadcastFolderPlacedOnDeskClientRpc()
    {
        OnFolderPlacedOnDeskAllClients?.Invoke();
    }

    // ── Folder handed to Vlad at the window ───────────────────────────────────

    /// <summary>
    /// Called by whichever client places the stamped folder on the window hand-off board.
    /// Carries a reference to the folder so the server can lock it and start the closing
    /// dialogue regardless of which client performed the drop. The server ensures the
    /// broadcast fires exactly once even if reported more than once.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportFolderHandedToVladServerRpc(NetworkObjectReference folderRef)
    {
        if (_windowHandOffBroadcast) return;
        _windowHandOffBroadcast = true;
        BroadcastFolderHandedToVladClientRpc(folderRef);
    }

    [ClientRpc]
    private void BroadcastFolderHandedToVladClientRpc(NetworkObjectReference folderRef)
    {
        OnFolderHandedToVladAllClients?.Invoke(folderRef);
    }
}
