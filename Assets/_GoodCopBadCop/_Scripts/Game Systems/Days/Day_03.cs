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
/// </summary>
public class Day_03 : DayBase
{
    [Header("Day 3 Tutorial")]
    [Tooltip("The Biological Exam notebook — hidden until the tutorial beat.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    [Tooltip("Tutorial arrow displayed above the biological exam notebook. Starts inactive.")]
    [SerializeField] private GameObject _notebookArrow;

    // Whether the biological notebook tutorial beat has already fired this shift.
    private bool _biologicalTutorialFired;

    // Persistent flag for early-action guard (mirrors Day_01/Day_02 pattern).
    private bool _notebookPageFiled;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated(); // Calls AnomalyManager.ApplyUnlocksFromSave()

        // Unlock documentation, mutation, and biological anomalies and persist immediately.
        AnomalyManager.Instance.UnlockBiologicalMutationAndDocumentation();

        // Hide the biological notebook until the tutorial beat spawns it in.
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);
        if (_notebookArrow != null) _notebookArrow.SetActive(false);

        _biologicalTutorialFired = false;

        // Listen for the first suspect's paperwork to kick off the tutorial.
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawned;

        // Ensure the first suspect has at least one biological anomaly so the tutorial always has material.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            SuspectController.ForceNextSuspectAnomalyCount = 1;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();
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
        _biologicalTutorialFired = true;

        // Unsubscribe immediately — we only need the first suspect.
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
        if (_notebookArrow != null) _notebookArrow.SetActive(true);

        // Wait for the player to pick up the biological notebook.
        yield return new WaitUntil(() => _biologicalNotebook != null && _biologicalNotebook.IsHeld);

        if (_notebookArrow != null) _notebookArrow.SetActive(false);

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
        ChecklistItem.OnAnyBoxChecked += OnAnyBoxCheckedLocal;

        yield return ShowAndWait("Mark every biological anomaly you detect.");

        // Guard: player may have ticked a box during the dialogue above.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ChecklistItem.OnAnyBoxChecked -= OnAnyBoxCheckedLocal;

        yield return BiologicalFileIntoBeat();

        void OnAnyBoxCheckedLocal() => anyBoxChecked = true;
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
    // Helper
    // -------------------------------------------------------------------------

    /// <summary>Shows a megaphone bark and waits until it finishes speaking.</summary>
    private IEnumerator ShowAndWait(string line)
    {
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeaking);
        MegaphoneDialogueManager.Instance.ShowDialogue(line);
        yield return new WaitUntil(() => !MegaphoneDialogueManager.Instance.IsSpeaking);
    }
}
