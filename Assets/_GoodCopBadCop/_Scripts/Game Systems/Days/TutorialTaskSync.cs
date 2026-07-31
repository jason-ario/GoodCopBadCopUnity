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

    /// <summary>Fired on all clients when the quarantine tutorial suspect's documentation tutorial begins.</summary>
    public static event Action OnQuarantineDocumentTutorialStartedAllClients;

    /// <summary>Fired on all clients when any player picks up an exam notebook during the tutorial.</summary>
    public static event Action OnExamNotebookPickedUpAllClients;

    /// <summary>Fired on all clients when any exam notebook page is filed during the tutorial.</summary>
    public static event Action OnExamPageFiledAllClients;

    /// <summary>
    /// Fired on ALL clients once the post-coupon megaphone dialogue completes,
    /// signalling that it is time to show the "Press the button" task and arm the switch.
    /// </summary>
    public static event Action OnPressButtonReadyAllClients;

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

    /// <summary>
    /// Fired on ALL clients once the server is ready to show the end-of-shift trash task.
    /// <see cref="TakeOutTrashTask"/>'s progress itself is already networked via
    /// NetworkVariable, but the initial <c>TutorialObjectiveList.AddObjective</c> call that
    /// shows the task needs to run on every client — not just wherever the scripted
    /// cutscene callback happens to execute (the server).
    /// </summary>
    public static event Action OnTrashTaskReadyAllClients;

    /// <summary>
    /// Fired on ALL clients once the server confirms a player grabbed a bag from the Day 1
    /// trash bag dispenser for the first time this cycle. <see cref="TrashBagPicker"/>'s
    /// dispense signal only fires locally on whichever client performed the interaction, so
    /// this relay is required to dismiss the dispenser tutorial arrow and reveal the highlighted
    /// junk items for every connected client.
    /// </summary>
    public static event Action OnTrashBagGrabbedAllClients;

    // ── Server-side counters / guards ─────────────────────────────────────────

    private int  _vladDocsPickedUpCount;
    private bool _quarantineTutorialBroadcast;
    private bool _examPickedUpBroadcast;
    private bool _deskFolderPlacedBroadcast;
    private bool _windowHandOffBroadcast;
    private bool _trashBagGrabbedBroadcast;

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
        _quarantineTutorialBroadcast = false;
        _examPickedUpBroadcast = false;
        _deskFolderPlacedBroadcast = false;
        _windowHandOffBroadcast = false;
        _trashBagGrabbedBroadcast = false;
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

    // ── Quarantine tutorial suspect documentation tutorial ────────────────────

    /// <summary>
    /// Called by any client when the quarantine tutorial suspect's documentation tutorial
    /// is triggered. The server ensures the broadcast fires exactly once even if multiple
    /// clients report it.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportQuarantineTutorialTriggerServerRpc()
    {
        if (_quarantineTutorialBroadcast) return;
        _quarantineTutorialBroadcast = true;
        BroadcastQuarantineTutorialStartedClientRpc();
    }

    [ClientRpc]
    private void BroadcastQuarantineTutorialStartedClientRpc()
    {
        OnQuarantineDocumentTutorialStartedAllClients?.Invoke();
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
        // Set the static flag so the server-side WaitUntil in QuarantineDocumentationBarkRoutine
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
    /// Server-only. Called after the post-coupon megaphone dialogue finishes.
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

    // ── Trash task ready ──────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Called once the post-Alexei megaphone dialogue completes and the
    /// end-of-shift trash task should be shown. Broadcasts to all clients so every
    /// player's <see cref="TutorialObjectiveList"/> gets the objective, not just the host.
    /// </summary>
    public void BroadcastTrashTaskReadyServer()
    {
        if (!IsServer) return;
        BroadcastTrashTaskReadyClientRpc();
    }

    [ClientRpc]
    private void BroadcastTrashTaskReadyClientRpc()
    {
        OnTrashTaskReadyAllClients?.Invoke();
    }

    // ── Trash bag grabbed (Day 1 dispenser tutorial) ──────────────────────────

    /// <summary>
    /// Called by whichever client grabs a bag from the Day 1 trash bag dispenser
    /// (<see cref="TrashBagPicker.OnBagDispensedLocally"/>). The server ensures the broadcast
    /// fires exactly once even if reported more than once (e.g. two players both grab a bag).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportTrashBagGrabbedServerRpc()
    {
        if (_trashBagGrabbedBroadcast) return;
        _trashBagGrabbedBroadcast = true;
        BroadcastTrashBagGrabbedClientRpc();
    }

    [ClientRpc]
    private void BroadcastTrashBagGrabbedClientRpc()
    {
        OnTrashBagGrabbedAllClients?.Invoke();
    }
}
