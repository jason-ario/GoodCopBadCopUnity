using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Tracks first encounters with named suspects and starts their authored
/// <see cref="SuspectData.introDialogue"/> either automatically on arrival (when
/// <see cref="ScriptedDialogue.isForced"/> is true) or when a player deliberately interacts
/// with the suspect at the booth (when it is false).
///
/// Encounter state is persisted per campaign save slot via <see cref="SaveDataManager"/>
/// (see <see cref="SaveSlot.EncounteredSuspectNames"/>), keyed by each suspect's Unity asset
/// name (e.g. <c>"Ivan"</c>). Forced intros suppress the normal entry bark and paperwork
/// hand-off, then hand off paperwork themselves once the dialogue completes — matching the
/// original always-forced behaviour. Optional intros leave the normal entry bark and paperwork
/// untouched; interacting starts the intro as a separate, skippable conversation.
/// </summary>
public class SuspectEncounterManager : MonoBehaviour
{
    public static SuspectEncounterManager Instance { get; private set; }

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

    /// <summary>
    /// Returns <c>true</c> if the player has already encountered this suspect before, for the
    /// currently active save slot. Falls back to <c>false</c> (never encountered) if there is
    /// no active slot, e.g. outside of a loaded campaign.
    /// </summary>
    public static bool HasEncountered(SuspectData data)
    {
        if (data == null || string.IsNullOrEmpty(data.name)) return false;
        if (SaveDataManager.Instance == null) return false;
        return SaveDataManager.Instance.HasEncounteredSuspect(data.name);
    }

    private static void MarkEncountered(SuspectData data)
    {
        if (data == null || string.IsNullOrEmpty(data.name)) return;
        if (SaveDataManager.Instance == null)
        {
            Debug.LogWarning($"[SuspectEncounterManager] No SaveDataManager instance — '{data.name}' encounter could not be persisted.");
            return;
        }

        SaveDataManager.Instance.MarkSuspectEncountered(data.name);
    }

    /// <summary>
    /// Marks a suspect as already encountered without playing their intro dialogue.
    /// Used for scripted appearances that borrow a suspect from the general pool but must skip
    /// their authored intro conversation.
    /// </summary>
    public static void MarkEncounteredWithoutIntro(SuspectData data) => MarkEncountered(data);

    /// <summary>Clears the encounter record for one specific suspect, in the active save slot. Debug use only.</summary>
    public static void ResetEncounter(SuspectData data)
    {
        if (data == null || SaveDataManager.Instance == null) return;
        SaveDataManager.Instance.ResetEncounteredSuspect(data.name);
    }

    /// <summary>
    /// Clears all encounter records, in the active save slot, for the given suspects. Debug use only.
    /// Pass the full <see cref="SuspectDatabase"/> contents to ensure every key is cleared.
    /// </summary>
    public static void ResetEncounters(SuspectData[] suspects)
    {
        if (suspects == null || SaveDataManager.Instance == null) return;
        SaveDataManager.Instance.ResetAllEncounteredSuspects();
        Debug.Log("[SuspectEncounterManager] All encounter records reset.");
    }

    // -------------------------------------------------------------------------
    // Forced intro intercept — called from SuspectController.SayEntryDialogue on arrival
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from <see cref="SuspectController.SayEntryDialogue"/> when a suspect arrives at
    /// the window. Returns <c>true</c> — and starts the intro immediately, pulling in every
    /// connected player — only when the suspect has an unplayed intro authored with
    /// <see cref="ScriptedDialogue.isForced"/> set. The caller must then suppress both the
    /// generic entry bark and the paperwork hand-off; this manager hands off paperwork itself
    /// once the dialogue completes. Only has an effect on the server.
    /// </summary>
    public bool TryInterceptForcedIntroDialogue(SuspectCharacter suspect)
    {
        if (suspect == null || suspect.Data == null) return false;
        if (suspect.Data.introDialogue == null || !suspect.Data.introDialogue.isForced) return false;
        if (HasEncountered(suspect.Data)) return false;

        // Mark immediately so a re-entrant call cannot double-trigger.
        MarkEncountered(suspect.Data);

        StartCoroutine(PlayForcedIntroDialogue(suspect));
        return true;
    }

    private IEnumerator PlayForcedIntroDialogue(SuspectCharacter suspect)
    {
        if (suspect == null) yield break;

        SuspectData data = suspect.Data;

        // Capture and clear the no-paperwork flag before the async gap.
        bool givesPaperwork = data.GivesPaperwork && !SuspectController.ForceNextSuspectNoPaperwork;
        SuspectController.ForceNextSuspectNoPaperwork = false;

        // Natural settle beat — suspect finishes their rotation and faces the player.
        yield return new WaitForSeconds(1.0f);

        if (suspect == null)
        {
            Debug.LogWarning($"[SuspectEncounterManager] PlayForcedIntroDialogue: '{data.name}' was destroyed during the settle beat — aborting before dialogue starts.");
            yield break;
        }

        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning("[SuspectEncounterManager] ScriptedDialogueRunner not found — skipping intro dialogue.");
            if (givesPaperwork) suspect.GivePaperwork();
            OnFirstEncounterDialogueComplete?.Invoke(data);
            yield break;
        }

        bool done = false;
        ScriptedDialogueRunner.Instance.PlayDialogue(suspect, data.introDialogue, () => done = true);
        yield return new WaitUntil(() => done);

        if (suspect == null)
        {
            Debug.LogWarning($"[SuspectEncounterManager] PlayForcedIntroDialogue: '{data.name}' was destroyed before GivePaperwork could be called.");
            OnFirstEncounterDialogueComplete?.Invoke(data);
            yield break;
        }

        // Hand off paperwork now that the intro has finished (plays the "Give" animation
        // before the documents spawn, matching the normal SuspectController.SayEntryDialogue
        // path), then notify any listeners.
        if (givesPaperwork)
            suspect.GivePaperwork();

        OnFirstEncounterDialogueComplete?.Invoke(data);
        Debug.Log($"[SuspectEncounterManager] Forced first-encounter intro complete for '{data.name}'.");
    }

    // -------------------------------------------------------------------------
    // Optional first-encounter intro — started by SuspectCharacter.Interact
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the current booth suspect's first-encounter intro for the player who interacted
    /// with them. Only starts a dialogue authored with <see cref="ScriptedDialogue.isForced"/>
    /// left false — forced intros are handled by <see cref="TryInterceptForcedIntroDialogue"/>
    /// on arrival instead. Must be called on the server. The initiating player is the only
    /// initial participant; another player can join explicitly by interacting with the same
    /// suspect.
    /// </summary>
    public bool TryStartIntroDialogue(SuspectCharacter suspect, ulong initiatingClientId)
    {
        if (suspect == null || suspect.Data == null) return false;
        if (suspect.Data.introDialogue == null || suspect.Data.introDialogue.isForced) return false;
        if (HasEncountered(suspect.Data)) return false;
        if (SuspectController.Instance == null || SuspectController.Instance.CurrentSuspect != suspect)
            return false;
        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning("[SuspectEncounterManager] ScriptedDialogueRunner not found — cannot start intro dialogue.");
            return false;
        }
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(initiatingClientId))
            return false;

        // Mark before starting the coroutine so two near-simultaneous interaction requests
        // cannot start the same intro twice.
        MarkEncountered(suspect.Data);
        SuspectData data = suspect.Data;

        ScriptedDialogueRunner.Instance.PlayDialogue(
            suspect,
            data.introDialogue,
            () =>
            {
                OnFirstEncounterDialogueComplete?.Invoke(data);
                Debug.Log($"[SuspectEncounterManager] First-encounter intro complete for '{data.name}'.");
            },
            initialParticipantClientId: initiatingClientId,
            joinByInteractionOnly: true,
            allowParticipantExit: true,
            hideNonParticipantUI: false);

        return true;
    }
}
