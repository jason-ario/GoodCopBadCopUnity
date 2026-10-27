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
/// hand-off, then hand off paperwork themselves once the dialogue completes. Participants may
/// leave a forced intro via Back; once everyone has left it is abandoned and restarts from the
/// top when a player talks to the suspect again (a partner still in it keeps it running and can
/// be joined mid-way). Optional intros leave the normal entry bark and paperwork
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
        if (!HasUnplayedForcedIntro(suspect)) return false;
        if (_forcedIntroGivesPaperwork.ContainsKey(suspect)) return false; // already intercepted

        // Mark immediately so a re-entrant call cannot double-trigger (same persistence as
        // before: the intro counts as encountered once it has started). Restarting after an
        // early exit is tracked in memory below, not through the save slot.
        MarkEncountered(suspect.Data);

        PruneDestroyedSuspects();

        // Capture and clear the no-paperwork flag now — it is a one-shot for this arrival.
        bool givesPaperwork = suspect.Data.GivesPaperwork && !SuspectController.ForceNextSuspectNoPaperwork;
        SuspectController.ForceNextSuspectNoPaperwork = false;
        _forcedIntroGivesPaperwork[suspect] = givesPaperwork;

        // No talk prompt / interaction until the intro itself is running.
        suspect.SetForcedIntroStateServer(SuspectCharacter.ForcedIntroState.Pending);

        StartCoroutine(PlayForcedIntroDialogue(suspect));
        return true;
    }

    /// <summary>
    /// True when <paramref name="suspect"/> has an authored forced intro that has not been
    /// played in the active save slot. Server-only authority (reads the host's save slot).
    /// </summary>
    public static bool HasUnplayedForcedIntro(SuspectCharacter suspect) =>
        suspect != null && suspect.Data != null &&
        suspect.Data.introDialogue != null && suspect.Data.introDialogue.isForced &&
        !HasEncountered(suspect.Data);

    // Server-only: suspects whose forced intro was intercepted this visit and has not completed
    // yet, mapped to whether they hand over paperwork once it does.
    private readonly System.Collections.Generic.Dictionary<SuspectCharacter, bool> _forcedIntroGivesPaperwork = new();

    private void PruneDestroyedSuspects()
    {
        var dead = new System.Collections.Generic.List<SuspectCharacter>();
        foreach (var key in _forcedIntroGivesPaperwork.Keys)
            if (key == null) dead.Add(key);
        foreach (var key in dead) _forcedIntroGivesPaperwork.Remove(key);
    }

    private IEnumerator PlayForcedIntroDialogue(SuspectCharacter suspect)
    {
        if (suspect == null) yield break;

        SuspectData data = suspect.Data;

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
            CompleteForcedIntro(suspect, data);
            yield break;
        }

        // Arrival: pull in every player within range (inside-booth players always), exactly as
        // before — but they may now leave via Back like a world conversation.
        StartForcedIntroRun(suspect, ulong.MaxValue);
    }

    /// <summary>
    /// Starts (or restarts from the top) a forced intro. <paramref name="initiatingClientId"/> is
    /// <c>ulong.MaxValue</c> on arrival (proximity seeding pulls everyone nearby in) or the client
    /// who re-talked to the suspect after everyone left (only they are pulled in; a partner joins
    /// the in-progress conversation by interacting). Either way participants can back out; when
    /// all of them have, the sequence is abandoned and the suspect waits for a re-talk.
    /// </summary>
    private void StartForcedIntroRun(SuspectCharacter suspect, ulong initiatingClientId)
    {
        SuspectData data = suspect.Data;
        bool isRestart = initiatingClientId != ulong.MaxValue;

        suspect.SetForcedIntroStateServer(SuspectCharacter.ForcedIntroState.None);

        ScriptedDialogueRunner.Instance.PlayDialogue(
            suspect,
            data.introDialogue,
            onComplete: () => CompleteForcedIntro(suspect, data),
            initialParticipantClientId: initiatingClientId,
            joinByInteractionOnly: isRestart,
            allowParticipantExit: true,
            hideNonParticipantUI: !isRestart,
            onAbandoned: () => OnForcedIntroAbandoned(suspect, data),
            allowExitFromForcedDialogue: true);
    }

    private void OnForcedIntroAbandoned(SuspectCharacter suspect, SuspectData data)
    {
        if (suspect == null) return;
        if (suspect.IsVerdictClosed || suspect.IsMutantInteractionClosed) return;

        suspect.SetForcedIntroStateServer(SuspectCharacter.ForcedIntroState.AwaitingRestart);
        Debug.Log($"[SuspectEncounterManager] Forced intro for '{data.name}' abandoned — restarts from the top when a player talks to them.");
    }

    private void CompleteForcedIntro(SuspectCharacter suspect, SuspectData data)
    {
        bool givesPaperwork = false;
        if (suspect != null && _forcedIntroGivesPaperwork.TryGetValue(suspect, out bool gives))
            givesPaperwork = gives;
        if (suspect != null) _forcedIntroGivesPaperwork.Remove(suspect);

        if (suspect == null)
        {
            Debug.LogWarning($"[SuspectEncounterManager] Forced intro: '{data.name}' was destroyed before GivePaperwork could be called.");
            OnFirstEncounterDialogueComplete?.Invoke(data);
            return;
        }

        suspect.SetForcedIntroStateServer(SuspectCharacter.ForcedIntroState.None);

        // Hand off paperwork now that the intro has finished (plays the "Give" animation
        // before the documents spawn, matching the normal SuspectController.SayEntryDialogue
        // path), then notify any listeners.
        if (givesPaperwork)
            suspect.GivePaperwork();

        OnFirstEncounterDialogueComplete?.Invoke(data);
        Debug.Log($"[SuspectEncounterManager] Forced first-encounter intro complete for '{data.name}'.");
    }

    /// <summary>
    /// Server-only. Restarts an abandoned forced intro from the beginning for the player who
    /// talked to the suspect. If the intro is already running again (the other player restarted
    /// it first), the requester joins that in-progress conversation instead. Returns false when
    /// there is nothing to restart or join.
    /// </summary>
    public bool TryRestartForcedIntro(SuspectCharacter suspect, ulong initiatingClientId)
    {
        if (suspect == null || suspect.Data == null || suspect.Data.introDialogue == null) return false;
        if (!_forcedIntroGivesPaperwork.ContainsKey(suspect)) return false;
        if (suspect.IsVerdictClosed || suspect.IsMutantInteractionClosed) return false;

        var runner = ScriptedDialogueRunner.Instance;
        if (runner == null) return false;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(initiatingClientId)) return false;

        // Lost a race: the other player already restarted it — join rather than restart.
        if (runner.TryJoinActiveDialogueServer(suspect, initiatingClientId))
            return true;

        if (suspect.CurrentForcedIntroState != SuspectCharacter.ForcedIntroState.AwaitingRestart) return false;
        if (runner.IsServerSequenceActive) return false; // another scripted sequence owns the runner

        StartForcedIntroRun(suspect, initiatingClientId);
        Debug.Log($"[SuspectEncounterManager] Forced intro for '{suspect.Data.name}' restarted by client {initiatingClientId}.");
        return true;
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
