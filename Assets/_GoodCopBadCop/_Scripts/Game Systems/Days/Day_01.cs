using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 1 scripted opening sequence.
///
/// Flow:
///   1. Day activates — all stamps, drawer, and notebooks are immediately unlocked.
///      Mutation and biological notebooks are hidden until Day 2/3 introduce them.
///   2. Intro cutscene ends → <see cref="ShiftManager.OnDayStart"/> fires →
///      <see cref="OnDayStarted"/> runs server-side.
///   3. After a 7-second hold, the rolling shutter opens automatically and is
///      locked open via <see cref="ShutterController.ShutterLockedOpen"/>.
///   4. The shift starts automatically (no switch press required on Day 1).
///      <see cref="SuspectController.InterceptNextSuspectSpawn"/> is armed so the
///      first suspect slot calls <see cref="SuspectController.SpawnScriptedSuspect"/>
///      with Vlad's prefab.
///   5. Vlad walks to the booth window without handing over any documents.
///   6. Once Vlad arrives, a <see cref="ScriptedDialogue"/> sequence plays through
///      <see cref="ScriptedDialogueRunner"/>. The suspect camera is active for the
///      entire duration of the conversation.
/// </summary>
public class Day_01 : DayBase
{
    [Header("Day 1 — Booth")]
    [Tooltip("The booth drawer the player can open freely on Day 1.")]
    [SerializeField] private Drawer _drawer;

    [Tooltip("The green ink stamp station.")]
    [SerializeField] private InkStamp _greenStampSlot;

    [Tooltip("The yellow ink stamp station.")]
    [SerializeField] private InkStamp _yellowStampSlot;

    [Tooltip("The red ink stamp station.")]
    [SerializeField] private InkStamp _redStampSlot;

    [Tooltip("The Documentation exam notebook.")]
    [SerializeField] private ExamNotebook _examNotebook;

    [Header("Day 1 — Vlad Arrival")]
    [Tooltip("Vlad's SuspectCharacter prefab — spawned as the first visitor on Day 1 via SuspectController.")]
    [SerializeField] private SuspectCharacter _vladPrefab;

    [Tooltip("Seconds after OnDayStart fires before the shutter opens and Vlad is triggered.")]
    [SerializeField] private float _shutterOpenDelay = 7f;

    [Header("Day 1 — Vlad Dialogue")]
    [Tooltip("Scripted dialogue sequence to play once Vlad arrives at the booth window.")]
    [SerializeField] private ScriptedDialogue _vladDialogue;

    [Tooltip("Seconds after Vlad arrives at the window before his dialogue sequence begins. " +
             "Should exceed the 0.5 s rotation so he is fully facing the player.")]
    [SerializeField] private float _vladDialogueStartDelay = 1.2f;

    [Header("Other Day Notebooks — Hidden During Day 1")]
    [Tooltip("The Mutation Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _mutationNotebook;

    [Tooltip("The Biological Exam Notebook — hidden for the entirety of Day 1.")]
    [SerializeField] private ExamNotebook _biologicalNotebook;

    // Guards against OnDayStarted running more than once if OnDayStart fires twice.
    private bool _dayStartedFired = false;

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        _dayStartedFired = false;

        // Unlock everything up front — no tutorial gating on Day 1 anymore.
        _drawer?.SetLocked(false);
        _greenStampSlot?.SetSlotInteractable(true);
        _yellowStampSlot?.SetSlotInteractable(true);
        _redStampSlot?.SetSlotInteractable(true);
        _examNotebook?.SetInteractableNetworked(true);

        // Hide notebooks that aren't introduced until later days.
        _mutationNotebook?.SetVisible(false);
        _mutationNotebook?.SetInteractableNetworked(false);
        _biologicalNotebook?.SetVisible(false);
        _biologicalNotebook?.SetInteractableNetworked(false);

        ShiftManager.Instance.OnDayStart += OnDayStarted;
        SuspectController.OnSuspectArrived += OnVladArrivedAtWindow;

        Debug.Log("[Day_01] DayActivated.");
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();

        // Restore notebooks so Day 2+ can manage them normally.
        _mutationNotebook?.SetVisible(true);
        _biologicalNotebook?.SetVisible(true);

        // Release the shutter lock so subsequent days can control it freely.
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;

        StopAllCoroutines();

        Debug.Log("[Day_01] DayDeactivated.");
    }

    private void OnDestroy()
    {
        if (ShutterController.Instance != null)
            ShutterController.Instance.ShutterLockedOpen = false;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // Day 1 Opening Sequence
    // -------------------------------------------------------------------------

    private void OnDayStarted()
    {
        if (this == null) return;
        if (!NetworkManager.Singleton.IsServer) return;
        if (_dayStartedFired) return;
        _dayStartedFired = true;

        StartCoroutine(Day1OpeningSequence());
    }

    /// <summary>
    /// Server-side coroutine. Waits <see cref="_shutterOpenDelay"/> seconds, then:
    ///   - Opens and locks the rolling shutter.
    ///   - Arms the Vlad intercept on the first suspect spawn slot (no paperwork, no entry line).
    ///   - Auto-starts the shift (bypassing the switch button for this scripted day).
    ///   - Overrides the first suspect arrival interval to fire immediately so Vlad
    ///     begins walking as soon as the shift's window sequence completes.
    /// </summary>
    private IEnumerator Day1OpeningSequence()
    {
        yield return new WaitForSeconds(_shutterOpenDelay);

        // Open and lock the shutter — it must stay open while Vlad is at the window.
        ShutterController.Instance.OpenShutter();
        ShutterController.Instance.ShutterLockedOpen = true;

        // Arm the Vlad intercept so the first suspect slot sends him to the window.
        // ForceNextSuspectNoPaperwork suppresses document hand-off for this appearance only.
        // ForceNextSuspectSkipEntryDialogue suppresses the generic entry line so the scripted
        // sequence via ScriptedDialogueRunner takes full control of the conversation.
        SuspectController.ForceNextSuspectNoPaperwork = true;
        SuspectController.ForceNextSuspectSkipEntryDialogue = true;
        SuspectController.InterceptNextSuspectSpawn = () => SuspectController.Instance.SpawnScriptedSuspect(_vladPrefab);

        // Fire immediately once the shift's opening window sequence finishes.
        ShiftManager.OverrideFirstArrivalInterval = new UnityEngine.Vector2(0f, 0f);

        // Start the shift automatically — no switch press required on Day 1.
        ShiftManager.Instance.TryStartShift();

        Debug.Log("[Day_01] Shutter opened and Vlad intercept armed — shift auto-started.");
    }

    // -------------------------------------------------------------------------
    // Vlad Scripted Dialogue
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires on all clients when any suspect arrives. Checks if it is the first suspect
    /// (index 0 = Vlad), then — server-only — starts the scripted dialogue sequence after
    /// a short delay that allows Vlad to finish his arrival rotation.
    /// </summary>
    private void OnVladArrivedAtWindow(int index)
    {
        if (index != 0) return; // Only react to the very first suspect on Day 1.

        SuspectController.OnSuspectArrived -= OnVladArrivedAtWindow;

        if (!NetworkManager.Singleton.IsServer) return;

        if (_vladDialogue == null)
        {
            Debug.LogWarning("[Day_01] _vladDialogue is not assigned — skipping scripted dialogue.");
            return;
        }

        StartCoroutine(WaitAndStartVladDialogue());
    }

    private IEnumerator WaitAndStartVladDialogue()
    {
        // Allow Vlad to finish his 0.5 s rotation and settle at the window.
        yield return new WaitForSeconds(_vladDialogueStartDelay);

        SuspectCharacter vlad = SuspectController.Instance?.CurrentSuspect;
        if (vlad == null)
        {
            Debug.LogWarning("[Day_01] CurrentSuspect is null when trying to start Vlad's scripted dialogue.");
            yield break;
        }

        ScriptedDialogueRunner.Instance.PlayDialogue(vlad, _vladDialogue, OnVladDialogueComplete);
    }

    /// <summary>Called on the server once Vlad's scripted dialogue finishes.</summary>
    private void OnVladDialogueComplete()
    {
        Debug.Log("[Day_01] Vlad scripted dialogue complete.");
        // TODO: dismiss Vlad, transition to regular suspect queue, etc.
    }
}

// end of file
