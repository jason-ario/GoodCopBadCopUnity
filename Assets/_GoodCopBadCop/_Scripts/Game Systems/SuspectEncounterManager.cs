using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Tracks first encounters with named suspects and automatically triggers their
/// <see cref="SuspectData.introDialogue"/> the first time they arrive at the booth window.
///
/// Encounter state is persisted across sessions via <see cref="PlayerPrefs"/>, keyed by
/// each suspect's Unity asset name (e.g. <c>"Encountered_Ivan"</c>).
///
/// This manager is called server-side from <see cref="SuspectController.SayEntryDialogue"/>
/// immediately before the normal entry-bark path. When <see cref="TryInterceptForIntroDialogue"/>
/// returns <c>true</c> the caller must skip both the generic bark and the paperwork hand-off;
/// this manager drives the scripted intro, then — if the suspect gives paperwork — calls
/// <see cref="SuspectController.SpawnPaperwork"/> after the dialogue completes and fires
/// <see cref="OnFirstEncounterDialogueComplete"/>.
/// </summary>
public class SuspectEncounterManager : MonoBehaviour
{
    public static SuspectEncounterManager Instance { get; private set; }

    private const string PrefPrefix = "Encountered_";

    /// <summary>
    /// Fired on the server immediately after a first-encounter intro dialogue finishes.
    /// Carries the <see cref="SuspectData"/> of the suspect whose intro just completed.
    /// </summary>
    public static event Action<SuspectData> OnFirstEncounterDialogueComplete;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // -------------------------------------------------------------------------
    // Encounter Tracking
    // -------------------------------------------------------------------------

    /// <summary>Returns <c>true</c> if the player has already encountered this suspect before.</summary>
    public static bool HasEncountered(SuspectData data)
    {
        if (data == null || string.IsNullOrEmpty(data.name)) return false;
        return PlayerPrefs.GetInt(PrefPrefix + data.name, 0) != 0;
    }

    private static void MarkEncountered(SuspectData data)
    {
        if (data == null || string.IsNullOrEmpty(data.name)) return;
        PlayerPrefs.SetInt(PrefPrefix + data.name, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Marks a suspect as already encountered without playing their intro dialogue.
    /// Used for scripted appearances (e.g. Day 1's too-far-gone tutorial suspect) that
    /// borrow a suspect from the general pool but must skip straight to the normal
    /// entry bark + paperwork flow instead of that suspect's authored intro monologue.
    /// </summary>
    public static void MarkEncounteredWithoutIntro(SuspectData data) => MarkEncountered(data);

    /// <summary>Clears the encounter record for one specific suspect. Debug use only.</summary>
    public static void ResetEncounter(SuspectData data)
    {
        if (data == null) return;
        PlayerPrefs.DeleteKey(PrefPrefix + data.name);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Clears all encounter records for the given suspects. Debug use only.
    /// Pass the full <see cref="SuspectDatabase"/> contents to ensure every key is cleared.
    /// </summary>
    public static void ResetEncounters(SuspectData[] suspects)
    {
        if (suspects == null) return;
        foreach (var s in suspects)
        {
            if (s != null && !string.IsNullOrEmpty(s.name))
                PlayerPrefs.DeleteKey(PrefPrefix + s.name);
        }
        PlayerPrefs.Save();
        Debug.Log("[SuspectEncounterManager] All encounter records reset.");
    }

    // -------------------------------------------------------------------------
    // Intro Dialogue Intercept
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from <see cref="SuspectController.SayEntryDialogue"/> when a suspect arrives at the window.
    /// Returns <c>true</c> if an intro dialogue was queued — the caller must then suppress
    /// both the generic entry bark and the paperwork hand-off; this manager handles both.
    /// Only has an effect on the server.
    /// </summary>
    public bool TryInterceptForIntroDialogue(SuspectCharacter suspect)
    {
        if (suspect == null || suspect.Data == null) return false;
        if (suspect.Data.introDialogue == null) return false;
        if (HasEncountered(suspect.Data)) return false;

        // Mark immediately so a re-entrant call cannot double-trigger.
        MarkEncountered(suspect.Data);

        StartCoroutine(PlayIntroDialogue(suspect));
        return true;
    }

    private IEnumerator PlayIntroDialogue(SuspectCharacter suspect)
    {
        if (suspect == null) yield break;

        SuspectData data = suspect.Data;

        // Capture and clear the no-paperwork flag before the async gap.
        bool givesPaperwork = data.GivesPaperwork && !SuspectController.ForceNextSuspectNoPaperwork;
        SuspectController.ForceNextSuspectNoPaperwork = false;

        // Natural settle beat — suspect finishes their rotation and faces the player.
        yield return new WaitForSeconds(1.0f);

        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning("[SuspectEncounterManager] ScriptedDialogueRunner not found — skipping intro dialogue.");
            if (givesPaperwork) SuspectController.Instance?.SpawnPaperwork();
            OnFirstEncounterDialogueComplete?.Invoke(data);
            yield break;
        }

        bool done = false;
        ScriptedDialogueRunner.Instance.PlayDialogue(suspect, data.introDialogue, () => done = true);
        yield return new WaitUntil(() => done);

        // Spawn paperwork now that the intro has finished, then notify any listeners.
        if (givesPaperwork)
            SuspectController.Instance?.SpawnPaperwork();

        OnFirstEncounterDialogueComplete?.Invoke(data);
        Debug.Log($"[SuspectEncounterManager] First-encounter intro complete for '{data.name}'.");
    }
}
