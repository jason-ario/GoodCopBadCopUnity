using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 4 — the morning after Vlad's death (see <see cref="OchoEatingVladCutscene"/> on Day 3).
/// Vlad has no barks or dialogue of any kind on Day 4 or beyond — he's gone.
///
/// Right after the player clocks in, a new, unfamiliar voice cuts in over the megaphone
/// announcing itself as Vlad's replacement, sent by "head office" to keep a closer eye on
/// the booth going forward. The voice is cold and bureaucratic, brushing off Vlad's death as
/// a non-event ("he was a contractor... convenient, while he lasted") and making it clear the
/// player is now being watched more closely. The player is never told outright, but this is
/// <see cref="OchoBoothEncounter">Ocho</see> impersonating a government official.
/// </summary>
public class Day_04 : DayBase
{
    public static Day_04 Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TimecardMachine.OnClockInServer -= OnPlayerClockedInServer;
    }

    // -------------------------------------------------------------------------
    // Inspector — New Voice Announcement
    // -------------------------------------------------------------------------

    [Header("Day 4 — New Voice Announcement")]
    [Tooltip("Scripted dialogue played over the megaphone right after the player clocks in on " +
             "Day 4 — the new, unfamiliar voice announcing itself as Vlad's replacement.")]
    [SerializeField] private ScriptedDialogue _newVoiceAnnouncementDialogue;

    [Tooltip("Subtitle speaker name for the new voice, overriding the default 'Megaphone' name. " +
             "Kept intentionally vague/anonymous since the player never learns who this really is.")]
    [SerializeField] private string _newVoiceSpeakerName = "???";

    [Tooltip("Subtitle name colour for the new voice — deliberately cold/clinical, distinct from " +
             "the warm orange used for Vlad's usual megaphone lines.")]
    [SerializeField] private Color _newVoiceSpeakerColor = new Color(0.6f, 0.85f, 0.75f);

    // -------------------------------------------------------------------------
    // DayBase Lifecycle
    // -------------------------------------------------------------------------

    public override void DayActivated()
    {
        base.DayActivated();

        // Arm the server-side reaction to the player's clock-in punch. Mirrors the pattern
        // used by Day_01's tutorial opening sequence, but here it announces the new megaphone
        // voice instead of a tutorial beat.
        TimecardMachine.OnClockInServer += OnPlayerClockedInServer;
    }

    public override void DayDeactivated()
    {
        base.DayDeactivated();
        TimecardMachine.OnClockInServer -= OnPlayerClockedInServer;
    }

    public override void ShiftEnded()        => base.ShiftEnded();
    public override void NightPhaseStarted() => base.NightPhaseStarted();
    public override void DayCompleted()      => base.DayCompleted();

    // -------------------------------------------------------------------------
    // New Voice Announcement — server-only
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired on the server by <see cref="TimecardMachine.OnClockInServer"/> the instant the
    /// player punches in for Day 4. Self-unsubscribes, then plays the new voice's megaphone
    /// announcement using an alternate voice so it's audibly distinct from Vlad's usual bark.
    /// </summary>
    private void OnPlayerClockedInServer()
    {
        TimecardMachine.OnClockInServer -= OnPlayerClockedInServer;

        if (this == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        if (_newVoiceAnnouncementDialogue == null)
        {
            Debug.LogWarning("[Day_04] _newVoiceAnnouncementDialogue is not assigned — skipping new voice announcement.");
            return;
        }

        if (ScriptedDialogueRunner.Instance == null)
        {
            Debug.LogWarning("[Day_04] ScriptedDialogueRunner.Instance is null — skipping new voice announcement.");
            return;
        }

        // unlocked: true — player keeps free movement while the announcement plays, matching
        // the feel of an overhead PA cutting in rather than a face-to-face conversation.
        ScriptedDialogueRunner.Instance.PlayMegaphoneDialogue(
            _newVoiceAnnouncementDialogue,
            onComplete: null,
            unlocked: true,
            speakerNameOverride: _newVoiceSpeakerName,
            speakerColorOverride: _newVoiceSpeakerColor,
            useAlternateVoice: true);

        Debug.Log("[Day_04] New voice megaphone announcement started.");
    }

    // -------------------------------------------------------------------------
    // Debug
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces the new voice announcement to play immediately, bypassing the clock-in gate.
    /// Server-only. Intended for <see cref="DebugConsole"/> use when testing without clocking in.
    /// </summary>
    public void DebugTriggerNewVoiceAnnouncement()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        TimecardMachine.OnClockInServer -= OnPlayerClockedInServer;
        OnPlayerClockedInServer();
    }
}
