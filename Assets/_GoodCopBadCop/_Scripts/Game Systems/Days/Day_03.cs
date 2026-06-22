using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 — biological exam tutorial shift.
///
/// Unlocks biological anomalies alongside the existing documentation and mutation sets.
/// Waits for the first suspect that arrives, then guides the player through picking up
/// the Biological Exam notebook, ticking the checklist, and filing the page. Remaining
/// suspects are unscripted.
///
/// All tutorial coroutines run server-only. Megaphone barks are broadcast to all
/// clients via MegaphoneDialogueManager.ShowDialogueSynced. Object-pickup gates use
/// the synced IsHeld NetworkVariable so either player's action advances the tutorial.
/// </summary>
public class Day_03 : DayBase
{
    [Header("Day 3 Tutorial")]
    [Tooltip("The Biological Exam notebook — hidden until the tutorial beat.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    // Whether the biological notebook tutorial beat has already fired this shift.
    private bool _biologicalTutorialFired;

    // Persistent flag for early-action guard (mirrors Day_01/Day_02 pattern).
    private bool _notebookPageFiled;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Hide the biological notebook until the tutorial beat spawns it in.
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        _biologicalTutorialFired = false;

        // Listen for the first suspect's paperwork to kick off the tutorial.
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawned;

        // Ensure the first suspect has at least one biological anomaly so the tutorial always has material.
        if (NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 1;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();
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
    // Suspect arrival — fire tutorial on first paperwork spawn
    // -------------------------------------------------------------------------

    private void OnPaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (_biologicalTutorialFired) return;
        if (!NetworkManager.Singleton.IsServer) return;
        _biologicalTutorialFired = true;
        SuspectController.OnPaperworkSpawned -= OnPaperworkSpawned;
        StartCoroutine(BiologicalExamTutorialSequence());
    }

    // -------------------------------------------------------------------------
    // Tutorial sequence
    // -------------------------------------------------------------------------

    private IEnumerator BiologicalExamTutorialSequence()
    {
        yield return new WaitForSeconds(4f);

        yield return ShowAndWait("A new threat profile — biological anomalies. Elevated radiation, abnormal temperature, and other physiological red flags.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("These require a dedicated instrument. Use the Biological Exam notebook to record your findings.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Pick up the Biological Exam notebook and check this subject thoroughly.");

        _biologicalNotebook?.SetVisible(true);
        _biologicalNotebook?.SetInteractableNetworked(true);
        if (_biologicalNotebook != null)
            ShowBiologicalNotebookMarker(true);

        // Wait for the player to pick up the biological notebook.
        yield return new WaitUntil(() => _biologicalNotebook != null && _biologicalNotebook.IsHeld);

        if (_biologicalNotebook != null)
            ShowBiologicalNotebookMarker(false);

        yield return BiologicalCheckBeat();
    }

    // -------------------------------------------------------------------------
    // Checkbox beat
    // -------------------------------------------------------------------------

    private IEnumerator BiologicalCheckBeat()
    {
        // Reset the static flag before subscribing so early interaction is captured.
        ChecklistItem.AnyBoxChecked = false;

        bool anyBoxChecked = false;
        // OnAnyCheckboxChecked fires on all clients via ExamNotebook's NetworkVariable callback.
        System.Action<ExamNotebook> onChecked = _ => anyBoxChecked = true;
        ExamNotebook.OnAnyCheckboxChecked += onChecked;

        yield return ShowAndWait("Mark every biological anomaly you detect.");

        // Guard: player may have ticked a box during the dialogue above.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ExamNotebook.OnAnyCheckboxChecked -= onChecked;

        yield return BiologicalFileIntoBeat();
    }

    // -------------------------------------------------------------------------
    // File notebook into folder beat
    // -------------------------------------------------------------------------

    private IEnumerator BiologicalFileIntoBeat()
    {
        // Reset early-action flag and subscribe before dialogue to avoid missed events.
        _notebookPageFiled = false;
        ExamNotebook.AnyPageFiled = false;
        ExamNotebook.OnAnyNotebookPageFiled += OnNotebookPageFiled;

        yield return ShowAndWait("File your biological findings into the folder.");

        // Guard: player may have filed during the preceding dialogue.
        if (ExamNotebook.AnyPageFiled)
            _notebookPageFiled = true;

        yield return new WaitUntil(() => _notebookPageFiled);

        ExamNotebook.OnAnyNotebookPageFiled -= OnNotebookPageFiled;

        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("Documentation, mutation, biological — each notebook captures a distinct layer of threat assessment.");
        yield return new WaitForSeconds(1f);
        yield return ShowAndWait("You now have the full standard toolkit. Use it well.");
    }

    private void OnNotebookPageFiled()
    {
        if (this == null) return;
        _notebookPageFiled = true;
    }

    // -------------------------------------------------------------------------
    // Networked Marker helpers
    // -------------------------------------------------------------------------

    /// <summary>Shows or hides the tutorial marker above the biological notebook on all clients.</summary>
    private void ShowBiologicalNotebookMarker(bool show)
    {
        if (_biologicalNotebook == null) return;
        NetworkObject netObj = _biologicalNotebook.GetComponent<NetworkObject>();
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
