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
/// </summary>
public class Day_02 : DayBase
{
    [Header("Day 2 Tutorial")]
    [Tooltip("The Mutation Exam notebook — hidden until the tutorial beat.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Tooltip("Tutorial arrow displayed above the mutation exam notebook. Starts inactive.")]
    [SerializeField] private GameObject _notebookArrow;

    // Whether the mutation notebook tutorial beat has already fired this shift.
    private bool _mutationTutorialFired;

    // Persistent flags for early-action guards (mirrors Day_01 pattern).
    private bool _notebookPageFiled;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated(); // Calls AnomalyManager.ApplyUnlocksFromSave()

        // Unlock documentation + mutation anomalies and persist immediately.
        AnomalyManager.Instance.UnlockMutationAndDocumentation();

        // Hide the mutation notebook until the tutorial beat spawns it in.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);
        if (_notebookArrow != null) _notebookArrow.SetActive(false);

        _mutationTutorialFired = false;

        // Listen for each suspect's paperwork so we can detect the first with a mutation anomaly.
        SuspectController.OnPaperworkSpawned += OnPaperworkSpawned;

        // Ensure the first suspect has at least one mutation anomaly so the tutorial always has material.
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
    // Suspect arrival — fire tutorial once on the first suspect with a mutation anomaly
    // -------------------------------------------------------------------------

    private void OnPaperworkSpawned(IDCard idCard, PickableObject appForm)
    {
        if (_mutationTutorialFired) return;
        _mutationTutorialFired = true;

        // Unsubscribe immediately — we only need to trigger the tutorial once.
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
        if (_notebookArrow != null) _notebookArrow.SetActive(true);

        // Wait for the player to pick up the mutation notebook.
        yield return new WaitUntil(() => _mutationNotebook != null && _mutationNotebook.IsHeld);

        if (_notebookArrow != null) _notebookArrow.SetActive(false);

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
        ChecklistItem.OnAnyBoxChecked += OnAnyBoxCheckedLocal;

        yield return ShowAndWait("Tick the boxes for every mutation you can identify.");

        // Guard: player may have ticked a box during the dialogue above.
        if (ChecklistItem.AnyBoxChecked)
            anyBoxChecked = true;

        yield return new WaitUntil(() => anyBoxChecked);

        ChecklistItem.OnAnyBoxChecked -= OnAnyBoxCheckedLocal;

        yield return MutationFileIntoBeat();

        void OnAnyBoxCheckedLocal() => anyBoxChecked = true;
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
