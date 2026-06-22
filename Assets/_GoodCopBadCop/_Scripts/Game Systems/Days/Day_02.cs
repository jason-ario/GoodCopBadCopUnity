using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 2 — mutation exam tutorial shift.
///
/// Unlocks mutation anomalies alongside the existing documentation set.
/// Waits for the first suspect that has at least one mutation anomaly, then guides
/// the player through picking up the Mutation Exam notebook, ticking the checklist,
/// and filing the page into the folder. Remaining suspects are unscripted.
///
/// All tutorial coroutines run server-only. Megaphone barks are broadcast to all
/// clients via MegaphoneDialogueManager.ShowDialogueSynced. Object-pickup gates use
/// the synced IsHeld NetworkVariable so either player's action advances the tutorial.
/// </summary>
public class Day_02 : DayBase
{
    [Header("Day 2 Tutorial")]
    [Tooltip("The Mutation Exam notebook — hidden until the tutorial beat.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Header("Other Day Notebooks — Hidden During Day 2")]
    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 2.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    // Whether the mutation notebook tutorial beat has already fired this shift.
    private bool _mutationTutorialFired;

    // Persistent flags for early-action guards (mirrors Day_01 pattern).
    private bool _notebookPageFiled;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Unlock the mutation exam refill in the tool locker shop for all clients.
        // This is a one-way operation — the unlock is saved and persists across sessions.
        if (NetworkManager.Singleton.IsServer && MegaphoneDialogueManager.Instance != null)
            MegaphoneDialogueManager.Instance.SetShopItemAvailableSynced("Mutation Exams (5)");

        // Hide the mutation notebook until the tutorial beat spawns it in.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);

        // Hide the biological notebook — it is not introduced until Day 3.
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        _mutationTutorialFired = false;

        // Listen for each suspect's paperwork so we can detect the first with a mutation anomaly.
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawned;

        // Ensure the first suspect has at least one mutation anomaly so the tutorial always has material.
        if (NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 1;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();
        // Restore biological notebook so Day 3 can manage it normally.
        _biologicalNotebook?.SetVisible(true);
        // Clear any tutorial markers that may still be active if the day ended mid-tutorial.
        TutorialMarkerManager.Instance?.UnmarkAll();
        SuspectController.OnPaperworkSpawned  -= OnPaperworkSpawned;
        ExamNotebook.OnAnyNotebookPageFiled   -= OnNotebookPageFiled;
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        SuspectController.OnPaperworkSpawned  -= OnPaperworkSpawned;
        ExamNotebook.OnAnyNotebookPageFiled   -= OnNotebookPageFiled;
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Suspect arrival — fire tutorial once on the first suspect with a mutation anomaly
    // -------------------------------------------------------------------------

    private void OnPaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (_mutationTutorialFired) return;
        if (!NetworkManager.Singleton.IsServer) return;
        _mutationTutorialFired = true;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        StartCoroutine(MutationExamTutorialSequence());
    }

    // -------------------------------------------------------------------------
    // Tutorial sequence
    // -------------------------------------------------------------------------

    private IEnumerator MutationExamTutorialSequence()
    {
        yield return new WaitForSeconds(4f);

        yield return ShowAndWait("A new type of anomaly has appeared in the field — physical mutations.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mutations are catalogued separately. Use the Mutation Exam notebook to record them.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Pick up the Mutation Exam notebook and mark every mutation you find on this subject.");

        _mutationNotebook?.SetVisible(true);
        _mutationNotebook?.SetInteractableNetworked(true);
        if (_mutationNotebook != null)
            ShowMutationNotebookMarker(true);

        // Wait for the player to pick up the mutation notebook.
        yield return new WaitUntil(() => _mutationNotebook != null && _mutationNotebook.IsHeld);

        if (_mutationNotebook != null)
            ShowMutationNotebookMarker(false);

        yield return MutationCheckBeat();
    }

    // -------------------------------------------------------------------------
    // Checkbox beat
    // -------------------------------------------------------------------------

    private IEnumerator MutationCheckBeat()
    {
        // Reset the static flag before subscribing so early interaction is captured.
        ChecklistItem.AnyBoxChecked = false;

        bool anyBoxChecked = false;
        // OnAnyCheckboxChecked fires on all clients via ExamNotebook's NetworkVariable callback.
        System.Action<ExamNotebook> onChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += onChecked;

        yield return ShowAndWait("Tick the boxes for every mutation you can identify.");

        // Guard: player may have ticked a box during the dialogue above.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ExamNotebook.OnAnyCheckboxChecked -= onChecked;

        yield return MutationFileIntoBeat();
    }

    // -------------------------------------------------------------------------
    // File notebook into folder beat
    // -------------------------------------------------------------------------

    private IEnumerator MutationFileIntoBeat()
    {
        // Reset early-action flag and subscribe before dialogue to avoid missed events.
        _notebookPageFiled = false;
        ExamNotebook.AnyPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnNotebookPageFiled;

        yield return ShowAndWait("Good. Now interact with the folder while holding the notebook to file your mutation findings.");

        // Guard: player may have filed during the preceding dialogue.
        if (ExamNotebook.AnyPageFiled)
            _notebookPageFiled = true;

        yield return new WaitUntil(() => _notebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnNotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Mutations are catalogued separately from documentation anomalies. Each notebook type covers a different threat profile.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Proceed with the remaining subjects. Stay vigilant.");
    }

    private void OnNotebookPageFiled()
    {
        if (this == null) return;
        _notebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Networked Marker helpers
    // -------------------------------------------------------------------------

    /// <summary>Shows or hides the tutorial marker above the mutation notebook on all clients.</summary>
    private void ShowMutationNotebookMarker(bool show)
    {
        if (_mutationNotebook == null) return;
        NetworkObject netObj = _mutationNotebook.GetComponent<NetworkObject>();
        if (netObj == null) return;
        if (show) MegaphoneDialogueManager.Instance?.ShowMarkerSynced(netObj);
        else      MegaphoneDialogueManager.Instance?.HideMarkerSynced(netObj);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows a megaphone bark on all clients and waits until it finishes speaking.
    /// Must only be called from server-side coroutines.
    /// </summary>
    private IEnumerator ShowAndWait(string line)
    {
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);
        MegaphoneDialogueManager.Instance.ShowDialogueSynced(line);
        yield return null;
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeakingSynced);
    }
}
