using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A timecard machine the player interacts with to clock in at the start of a shift
/// and clock out at the end. Shared by every player in the session — either cop can punch
/// the card, and the punch resolves once for the whole team.
///
/// Clock-in: armed by <see cref="EnableClockIn"/> (server). Fires
/// <see cref="OnClockInAllClients"/> on every client when the punch lands.
///
/// Clock-out: armed by <see cref="EnableClockOut"/> (server) when ShiftManager signals
/// all suspects and post-shift tasks are done. Fires <see cref="OnClockOutServer"/> on the
/// server then triggers EndShift after a short delay.
///
/// STATE OWNERSHIP: the armed/punched state lives in server-written
/// <see cref="NetworkVariable{T}"/>s, NOT in plain bools set by ClientRpc. This is what makes
/// the machine behave correctly in multiplayer:
/// <list type="bullet">
/// <item>A ClientRpc only reaches clients connected at the moment it is sent, so a late joiner
/// used to spawn with both flags false — unable to punch the card at all, and with no
/// objective row telling them to. NetworkVariables are part of the spawn payload, so a late
/// joiner inherits the exact current state.</item>
/// <item>NetworkVariables only raise <c>OnValueChanged</c> on an actual value CHANGE, so the
/// server re-arming clock-out (which <see cref="ShiftManager.TryEnableClockOut"/> can attempt
/// many times per day) can no longer stack duplicate "Clock out for the day" rows on the task
/// list the way a re-sent ClientRpc did.</item>
/// <item>Both players read the same authoritative flag, so whichever one punches, every peer
/// (including the one who didn't) resolves the clock-out identically.</item>
/// </list>
/// The remaining ClientRpcs carry ONLY one-shot feedback (punch audio/animation) and the
/// static events, which are inherently "at the moment it happened" and meaningless to replay
/// for someone who joined afterwards.
/// </summary>
public class TimecardMachine : Interactable
{
    /// <summary>
    /// Fired on the server the moment a clock-out punch is accepted.
    /// Currently unused — day-specific post-shift reactions (e.g. Day 3's power outage) are
    /// driven off <see cref="ShiftManager.OnLastSuspectProcessed"/> (Dusk) instead, so they
    /// happen the instant the last suspect is processed rather than waiting for the player to
    /// walk over and clock out. Kept available for any future reaction that should genuinely
    /// wait for the clock-out punch itself.
    /// </summary>
    public static event Action OnClockOutServer;

    /// <summary>
    /// Fired on ALL clients the moment a clock-in punch is accepted.
    /// Subscribe in Day_01 (or any day) to drive tutorial task progression that
    /// should react immediately once the player clocks in.
    /// </summary>
    public static event Action OnClockInAllClients;

    /// <summary>
    /// Fired on the server the moment a clock-in punch is accepted — before the punch
    /// is broadcast to clients. Subscribe to drive server-authoritative day sequencing
    /// (e.g. Day 1 opening the shutter and summoning Vlad) that must run reliably even
    /// on a dedicated server with no local client.
    /// </summary>
    public static event Action OnClockInServer;

    /// <summary>
    /// Fired on ALL clients the moment a clock-out punch is accepted.
    /// Subscribe to drive client-side reactions such as UI notifications.
    /// </summary>
    public static event Action OnClockOutAllClients;

    /// <summary>
    /// Raised on every peer whenever the networked clocked-out flag CHANGES — and, critically,
    /// once at spawn on a late joiner so it inherits the current value too. The bool is the new
    /// value of <see cref="HasClockedOutThisCycle"/>.
    ///
    /// Use this instead of <see cref="OnClockOutAllClients"/> for anything that must be correct
    /// for a player who joined after the punch (e.g. <see cref="BunkBedInteractable"/> deciding
    /// whether sleeping is allowed). <c>OnClockOutAllClients</c> is a fire-once "it happened
    /// right now" notification and is simply never delivered to a late joiner; this one is
    /// state-driven, so it always converges.
    /// </summary>
    public static event Action<bool> OnClockedOutStateChanged;

    /// <summary>
    /// The spawned machine, so the static <see cref="HasClockedOutThisCycle"/> can read the
    /// networked flag without every caller needing a reference.
    /// </summary>
    private static TimecardMachine _instance;

    /// <summary>
    /// Ground-truth, always-current record of whether the team has punched the clock-out card
    /// for the current shift cycle. Unlike subscribing to <see cref="OnClockOutAllClients"/>
    /// (a fire-once event that a late subscriber can simply miss — e.g. an object that
    /// (re)spawns or resubscribes after the punch already landed), this can always be read
    /// directly to answer "has the team clocked out yet?" with no risk of desync.
    ///
    /// Backed by a server-written <see cref="NetworkVariable{T}"/> rather than a local static
    /// bool, so it is correct on EVERY peer — including a player who joined after the punch
    /// landed and therefore never received <see cref="PunchCardClientRpc"/>.
    /// </summary>
    public static bool HasClockedOutThisCycle =>
        _instance != null && _instance.IsSpawned && _instance._clockedOutThisCycle.Value;

    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clockOutSound;
    [SerializeField] private Animator _animator;

    [Header("Small Light")]
    [Tooltip("Animator on the Small Light child — drives the 'Ready' bool for the blink animation.")]
    [SerializeField] private Animator _lightAnimator;
    [Tooltip("World-space AudioSource to play the fanfare through when the machine becomes ready.")]
    [SerializeField] private AudioSource _fanfareSource;
    [Tooltip("Fanfare clip played when the timecard machine is primed for clock-out.")]
    [SerializeField] private AudioClip _fanfareClip;

    [Header("Interact Text")]
    [Tooltip("Text shown on the reticle while the machine is primed for clock-in.")]
    [SerializeField] private string _clockInInteractText = "Clock In";

    [Header("Tutorial Objective List")]
    [Tooltip("Text shown in the tutorial objective overlay while clock-in is available. " +
             "Day 1 drives its own scripted tutorial objectives, so this is skipped on Day 1.")]
    [SerializeField] private string _clockInObjectiveText = "Clock in for the day";
    [Tooltip("Text shown in the tutorial objective overlay while clock-out is available. " +
             "Day 1 drives its own scripted tutorial objectives, so this is skipped on Day 1.")]
    [SerializeField] private string _clockOutObjectiveText = "Clock out for the day";
    [Tooltip("Seconds the completed objective row stays visible (struck through) before it is removed.")]
    [SerializeField] private float _objectiveCompletedLingerDuration = 1f;

    /// <summary>Seconds to wait after the punch animation fires before triggering the end-of-shift.</summary>
    [SerializeField] private float _punchToReportDelay = 1.5f;

    private static readonly int PunchTrigger = Animator.StringToHash("Punch");
    private static readonly int ReadyBool    = Animator.StringToHash("Ready");

    /// <summary>
    /// Server-owned: true while the machine will accept a clock-in punch from any player.
    /// Read directly by <see cref="Interact"/> on every peer, so a late joiner can punch too.
    /// </summary>
    private readonly NetworkVariable<bool> _clockInArmed = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Server-owned: true while the machine will accept a clock-out punch from any player.
    /// Because this only raises OnValueChanged on a real transition, repeated
    /// <see cref="EnableClockOut"/> calls are idempotent and cannot duplicate the objective row.
    /// </summary>
    private readonly NetworkVariable<bool> _clockOutArmed = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Server-owned backing flag for <see cref="HasClockedOutThisCycle"/>.</summary>
    private readonly NetworkVariable<bool> _clockedOutThisCycle = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Tracks the tutorial objective overlay rows added while each punch is armed. Day 1 drives
    // its own scripted objective sequence for clock-in/out (see Day_01.ShowClockOutTask and its
    // arrow-based clock-in tutorial), so these are only added on days other than Day 1.
    // A non-null slot is also the per-player "a row already exists" guard that stops the list
    // from ever showing the same objective twice — see AddObjectiveIfNotDay1.
    private TutorialObjectiveItem _clockInObjective;
    private TutorialObjectiveItem _clockOutObjective;

    // Cached from the Inspector-assigned interactText so we can restore it after clock-in.
    private string _clockOutInteractText;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _instance = this;
        _clockOutInteractText = interactText;

        _clockInArmed.OnValueChanged  += OnClockInArmedChanged;
        _clockOutArmed.OnValueChanged += OnClockOutArmedChanged;
        _clockedOutThisCycle.OnValueChanged += OnClockedOutChanged;

        // LATE JOINERS: the NetworkVariables above arrive already populated with the current
        // shift's authoritative state, so replay it into this client's local presentation
        // (reticle text, ready light, objective row) right now. `playFeedback: false` because
        // the fanfare/punch are one-shot moments that already happened — a player joining
        // mid-shift should inherit the state, not hear the stinger for it again.
        ApplyClockInArmed(_clockInArmed.Value, playFeedback: false);
        ApplyClockOutArmed(_clockOutArmed.Value, playFeedback: false);

        // Announce the inherited clocked-out state so state-driven listeners (the bunk bed's
        // "can I sleep?" gate) are correct on a peer that joined AFTER the punch landed and
        // therefore never received OnClockOutAllClients.
        OnClockedOutStateChanged?.Invoke(_clockedOutThisCycle.Value);

        // Days other than Day 1 have no scripted opening sequence, so nothing else ever
        // arms clock-in for them — arm it here the instant the shift is reset and ready.
        // Day 1 keeps driving its own EnableClockIn() call from Day_01.OnDayStarted (its
        // tutorial arrow / shutter sequence needs to be wired up first); this handler simply
        // no-ops on Day 1 so the two paths never conflict.
        //
        // Subscribed here (OnNetworkSpawn) rather than Awake: Awake() execution order across
        // different components is not guaranteed, so if this ran before ShiftManager.Awake()
        // set ShiftManager.Instance, the subscription would silently never happen — leaving
        // clock-in permanently unable to re-arm itself past Day 1 for that whole session.
        // OnNetworkSpawn always runs after every scene object's Awake() has completed, so
        // ShiftManager.Instance is guaranteed to be ready here.
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady += OnShiftReady;
    }

    public override void OnNetworkDespawn()
    {
        _clockInArmed.OnValueChanged  -= OnClockInArmedChanged;
        _clockOutArmed.OnValueChanged -= OnClockOutArmedChanged;
        _clockedOutThisCycle.OnValueChanged -= OnClockedOutChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady -= OnShiftReady;

        if (_instance == this)
            _instance = null;

        base.OnNetworkDespawn();
    }

    private void OnShiftReady()
    {
        Debug.Log($"[TimecardMachine] OnShiftReady fired. CurrentDay={ShiftManager.Instance?.CurrentDay ?? -1}.");

        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        EnableClockIn();
    }

    public override void Interact(PlayerInteractionController player)
    {
        // Clock-out takes priority. In the normal flow the two flags are mutually
        // exclusive, but if a stray EnableClockIn() (e.g. ShiftManager.OnShiftReady arming
        // the *next* day) ever lands while clock-out is still armed for the *current* day,
        // checking clock-in first would silently reinterpret the player's clock-OUT tap as a
        // clock-in — re-running TryStartShift() and re-populating the suspect lineup. Clock-out
        // must win that race since it always represents finishing the day already in progress.
        //
        // Both branches test the networked flag rather than a locally-cached bool, so EVERY
        // player (host, remote client, late joiner) agrees on whether the machine is punchable.
        if (_clockOutArmed.Value)
        {
            base.Interact(player);

            if (IsServer)
                HandleClockOut();
            else
                RequestClockOutServerRpc();
            return;
        }

        if (!_clockInArmed.Value) return;

        base.Interact(player);

        if (IsServer)
            HandleClockIn();
        else
            RequestClockInServerRpc();
    }

    // ── Clock-In ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the server (e.g. Day_01) when it is time for the player to clock in.
    /// Arms the clock-in interaction for every player, now and for anyone who joins later.
    /// Safe to call repeatedly — writing the same value raises no change notification.
    /// </summary>
    public void EnableClockIn()
    {
        if (!IsServer || !IsSpawned) return;
        _clockInArmed.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestClockInServerRpc() => HandleClockIn();

    /// <summary>
    /// Server-side resolution of a clock-in punch. The <see cref="_clockInArmed"/> test is the
    /// race guard: if both cops tap the machine on the same frame, the first request clears the
    /// flag synchronously here so the second one is dropped instead of clocking in twice.
    /// </summary>
    private void HandleClockIn()
    {
        if (!IsServer || !_clockInArmed.Value) return;

        _clockInArmed.Value = false;
        OnClockInServer?.Invoke();
        PunchClockInClientRpc();

        // Day 1 drives its own scripted opening (shutter, Vlad reveal) and calls
        // ShiftManager.TryStartShift() itself once that sequence is ready — see
        // Day_01.OnPlayerClockedIn / Day1OpeningSequence. Every other day starts the
        // shift the instant the clock-in punch lands.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay != 1)
            ShiftManager.Instance.TryStartShift();
    }

    private void OnClockInArmedChanged(bool previous, bool current)
    {
        if (previous == current) return;
        ApplyClockInArmed(current, playFeedback: true);
    }

    /// <summary>
    /// Mirrors the networked clock-in armed state into this client's local presentation.
    /// Idempotent: safe to call with the same value repeatedly, and safe to call at spawn time
    /// for a late joiner (see <see cref="OnNetworkSpawn"/>).
    /// </summary>
    private void ApplyClockInArmed(bool armed, bool playFeedback)
    {
        interactText = armed ? _clockInInteractText : _clockOutInteractText;
        SetLightReady(armed || _clockOutArmed.Value);

        if (armed)
            AddObjectiveIfNotDay1(ref _clockInObjective, _clockInObjectiveText);
        else
            CompleteObjective(ref _clockInObjective);
    }

    /// <summary>
    /// One-shot clock-in feedback for the players who were connected when the punch landed.
    /// All persistent state (armed flags, reticle text, objective row) is handled by
    /// <see cref="_clockInArmed"/>'s change handler so late joiners stay correct.
    /// </summary>
    [ClientRpc]
    private void PunchClockInClientRpc()
    {
        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);

        OnClockInAllClients?.Invoke();
    }

    // ── Clock-Out ─────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestClockOutServerRpc()
    {
        HandleClockOut();
    }

    /// <summary>
    /// Server-side resolution of a clock-out punch, whichever player triggered it. Clearing
    /// <see cref="_clockOutArmed"/> before doing anything else is what makes this safe when
    /// both cops slap the machine simultaneously: the second request finds it already false and
    /// returns, so EndShift is only ever scheduled once.
    /// </summary>
    private void HandleClockOut()
    {
        if (!IsServer || !_clockOutArmed.Value) return;

        _clockOutArmed.Value = false;

        // Ground truth for the whole session, replicated to everyone — including anyone who
        // joins after this point (see HasClockedOutThisCycle). Set before the event/RPC so
        // anything reacting to the punch can safely read it.
        _clockedOutThisCycle.Value = true;
        SaveDataManager.Instance?.SaveCurrentWorkdayState();

        OnClockOutServer?.Invoke();
        PunchCardClientRpc();
        StartCoroutine(EndShiftAfterDelay(_punchToReportDelay));
    }

    private IEnumerator EndShiftAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShiftManager.Instance.EndShift();
    }

    /// <summary>
    /// Called by ShiftManager on the server when all suspects and post-shift tasks are done.
    /// Arms clock-out for every player — including future late joiners — and triggers the
    /// fanfare and ready light. Safe to call repeatedly: writing the same value raises no
    /// change notification, so the "Clock out for the day" objective row can never be added
    /// twice (which is exactly what the old re-sent ClientRpc did).
    /// </summary>
    public void EnableClockOut()
    {
        if (!IsServer || !IsSpawned) return;

        // Never re-arm after the team has already punched out this cycle — a stray
        // RecheckClockOutGate() landing after the punch would otherwise light the machine back
        // up and hand out a second clock-out objective while the shift is already ending.
        if (_clockedOutThisCycle.Value) return;

        _clockOutArmed.Value = true;
    }

    /// <summary>True when the machine is currently ready to accept a clock-out punch.</summary>
    public bool IsClockOutArmed => _clockOutArmed.Value;

    /// <summary>True when the machine is currently ready to accept a clock-in punch.</summary>
    public bool IsClockInArmed => _clockInArmed.Value;

    /// <summary>
    /// Rehydrates persistent punch state on the host before NGO distributes it to all clients.
    /// Presentation is driven from the NetworkVariables, so late joiners and existing peers share
    /// the same restored result.
    /// </summary>
    public void RestoreWorkdayState(bool clockInArmed, bool clockOutArmed, bool clockedOut)
    {
        if (!IsServer || !IsSpawned) return;
        _clockInArmed.Value = clockInArmed;
        _clockOutArmed.Value = clockOutArmed;
        _clockedOutThisCycle.Value = clockedOut;
    }

    /// <summary>
    /// Resets the machine to its default inactive state. Call at the start of each shift.
    /// Runs on every peer for local presentation cleanup; the authoritative flags are only
    /// written by the server, which replicates the clean state to everyone automatically.
    /// </summary>
    public void Reset()
    {
        SetLightReady(false);
        CompleteObjective(ref _clockInObjective);
        CompleteObjective(ref _clockOutObjective);

        if (!string.IsNullOrEmpty(_clockOutInteractText))
            interactText = _clockOutInteractText;

        if (!IsSpawned || !IsServer) return;

        _clockInArmed.Value        = false;
        _clockOutArmed.Value       = false;
        _clockedOutThisCycle.Value = false;
    }

    private void OnClockOutArmedChanged(bool previous, bool current)
    {
        if (previous == current) return;
        ApplyClockOutArmed(current, playFeedback: true);
    }

    /// <summary>
    /// Relays the replicated clocked-out flag to every peer, whichever player punched the card.
    /// This is the sync point that guarantees a clock-out "registers for both players".
    /// </summary>
    private void OnClockedOutChanged(bool previous, bool current)
    {
        if (previous == current) return;

        Debug.Log($"[TimecardMachine] Clocked-out state -> {current} (replicated to this peer).");
        OnClockedOutStateChanged?.Invoke(current);
    }

    /// <summary>
    /// Mirrors the networked clock-out armed state into this client's local presentation.
    /// Because it is driven off a value CHANGE (or a one-time spawn replay), the objective row
    /// is added exactly once per player per shift and removed exactly once.
    /// </summary>
    private void ApplyClockOutArmed(bool armed, bool playFeedback)
    {
        SetLightReady(armed || _clockInArmed.Value);

        if (armed)
        {
            AddObjectiveIfNotDay1(ref _clockOutObjective, _clockOutObjectiveText);

            if (playFeedback && _fanfareSource != null && _fanfareClip != null)
                _fanfareSource.PlayOneShot(_fanfareClip);

            return;
        }

        // Disarmed — either the punch landed or the shift was reset. Either way this player's
        // row is done, on the punching client and the watching one alike.
        CompleteObjective(ref _clockOutObjective);

        if (playFeedback && _fanfareSource != null)
            _fanfareSource.Stop();
    }

    /// <summary>
    /// One-shot clock-out feedback for the players who were connected when the punch landed.
    /// All persistent state (armed flag, clocked-out flag, objective row) is handled by the
    /// NetworkVariables so both players — and late joiners — agree on the outcome.
    /// </summary>
    [ClientRpc]
    private void PunchCardClientRpc()
    {
        if (_audioSource != null && _clockOutSound != null)
            _audioSource.PlayOneShot(_clockOutSound);

        if (_animator != null)
            _animator.SetTrigger(PunchTrigger);

        OnClockOutAllClients?.Invoke();
    }

    /// <summary>Sets the 'Ready' bool on the small light animator to drive the blink animation.</summary>
    private void SetLightReady(bool ready)
    {
        if (_lightAnimator != null)
            _lightAnimator.SetBool(ReadyBool, ready);
    }

    /// <summary>
    /// Adds a row to the tutorial objective overlay, unless:
    /// <list type="bullet">
    /// <item><paramref name="slot"/> already holds a row — one objective per player, never a
    /// stack of identical "Clock out for the day" lines no matter how many times the server
    /// re-arms the machine.</item>
    /// <item>the current day is Day 1 — Day 1 drives its own scripted objective sequence for
    /// clock-in/out (see <c>Day_01.ShowClockOutTask</c> and its arrow-based clock-in tutorial),
    /// so adding a row here would duplicate it.</item>
    /// </list>
    /// </summary>
    private static void AddObjectiveIfNotDay1(ref TutorialObjectiveItem slot, string text)
    {
        if (slot != null) return;

        int day = ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : -1;
        Debug.Log($"[TimecardMachine] AddObjectiveIfNotDay1(\"{text}\") -- CurrentDay={day}, " +
                  $"TutorialObjectiveList.Instance={(TutorialObjectiveList.Instance != null)}.");

        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        slot = TutorialObjectiveList.Instance?.AddObjective(text);
    }

    /// <summary>Marks the given objective row complete and removes it shortly after, if one is tracked.</summary>
    private void CompleteObjective(ref TutorialObjectiveItem slot)
    {
        if (slot == null) return;
        TutorialObjectiveList.Instance?.CompleteAndRemoveObjective(slot, _objectiveCompletedLingerDuration);
        slot = null;
    }
}
