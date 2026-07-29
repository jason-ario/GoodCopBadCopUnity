using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to the Day 2 booth-encounter Ocho prefab (the clown-faced "Nunya Business" suspect,
/// not the distant tree-line sighting <c>OchoWatcherBehaviour</c> used later the same day).
///
/// Ocho antagonizes the player about a previous kill (via <see cref="SuspectData.introDialogue"/>
/// on his own <see cref="SuspectData"/> asset), hands over paperwork with an obviously fake name
/// and ID (and, if <c>_useDrawingForEntryReason</c> is on, a hand-drawn doodle in place of a
/// written entry reason, see <see cref="ApplicationLetter.SetEntryReasonDrawing"/>), and rejects
/// every verdict outright. The rejection plays out in a fixed sequence, regardless of stamp:
///   1. He takes the folder and immediately delivers a stamp-specific mocking reaction line
///      while still standing at the booth. The verdict never actually processes (no payout
///      switch runs, see <see cref="SuspectController.ExecuteVerdict"/>).
///   2. He vanishes and reappears standing behind the player inside the booth.
///   3. The encounter waits for a player to actually turn around and spot him there (or times
///      out); the moment he's spotted a stinger plays and he vanishes again.
///   - Red stamp (Kill) specifically: once he vanishes after the stinger, the booth's power
///     cuts out and he cackles from the dark, leaving the player to go fix the breaker at
///     <see cref="ElectricPanelController"/> before the switch button will let them summon the
///     next suspect (already gated by <see cref="SwitchButton"/>'s powerOn check). Once power is
///     restored the megaphone plays a couple of dismissive barks and the next suspect can be
///     called. Pass/Quarantine skip the blackout entirely, Ocho just leaves.
///
/// All timing/sequencing runs server-only; physical vanish/reappear/stinger/laugh beats are
/// broadcast to all clients via ClientRpc so both co-op players see them. The turn-around spot
/// check runs locally on each client against that client's own local player camera and reports
/// back to the server via ServerRpc the moment it succeeds.
/// </summary>
public class OchoBoothEncounter : NetworkBehaviour
{
    [Header("Ocho Booth Encounter — Fake Paperwork")]
    [Tooltip("If true, subscribes to SuspectController.OnApplicationFormSpawned while active and " +
             "swaps the entry-reason text on Ocho's application form for a hand-drawn doodle child " +
             "(authored directly on the Application Letter prefab). Turn off if the drawing hasn't " +
             "been added yet and you'd rather fall back to the entryReasons text on his SuspectData.")]
    [SerializeField] private bool _useDrawingForEntryReason = true;

    [Header("Ocho Booth Encounter — Rejected Verdict")]
    [Tooltip("Line Ocho delivers after reappearing, when the player used the green (Pass) stamp.")]
    [SerializeField] private string _passRejectedLine = "Passed? Ha! You really think paperwork like that gets to just walk through?";

    [Tooltip("Line Ocho delivers after reappearing, when the player used the yellow (Quarantine) stamp.")]
    [SerializeField] private string _quarantineRejectedLine = "Quarantine? Adorable. Like a cage was ever going to hold me.";

    [Tooltip("Line Ocho delivers after reappearing, when the player used the red (Kill) stamp.")]
    [SerializeField] private string _killRejectedLine = "Ahh, interesting choice! I bet you would love to kill again wouldn't you?";

    [Tooltip("Seconds between Ocho taking the folder and vanishing.")]
    [SerializeField] private float _takeFolderDelay = 0.6f;

    [Tooltip("Seconds the reaction line stays up before the encounter continues.")]
    [SerializeField] private float _postLineDelay = 2.5f;

    [Header("Ocho Booth Encounter — Vanish & Reappear (every verdict)")]
    [Tooltip("World-space point behind the player's usual position in the booth. Ocho teleports here.")]
    [SerializeField] private Transform _reappearPoint;

    [Tooltip("Seconds Ocho stays vanished (hidden) before reappearing behind the player.")]
    [SerializeField] private float _vanishToReappearDelay = 1.5f;

    [Tooltip("Seconds Ocho waits after reappearing, standing still, before delivering his reaction line.")]
    [SerializeField] private float _reappearToLineDelay = 1f;

    [Header("Ocho Booth Encounter — Turn-Around Reveal")]
    [Tooltip("Stinger clip played the instant a player turns and actually spots Ocho standing behind them.")]
    [SerializeField] private AudioClip _stingerClip;

    [Tooltip("Dot product between the local player's camera forward and the direction to Ocho required to " +
             "count as 'looking at him'. Higher = a narrower cone directly in front of the camera.")]
    [SerializeField] private float _spotDotThreshold = 0.7f;

    [Tooltip("Seconds between visibility checks on each client while waiting for a player to turn around.")]
    [SerializeField] private float _spotCheckInterval = 0.15f;

    [Tooltip("Maximum seconds to wait for a player to spot Ocho before giving up and having him vanish anyway.")]
    [SerializeField] private float _spotTimeoutSeconds = 12f;

    [Tooltip("Seconds between the stinger playing and the sequence continuing (power outage / despawn).")]
    [SerializeField] private float _stingerToVanishDelay = 0.4f;

    [Header("Ocho Booth Encounter — Power Outage (Red Stamp / Kill only)")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _demonicLaughClip;

    [Tooltip("Transform the tutorial arrow points at while the power is out (usually the Electrical Panel).")]
    [SerializeField] private Transform _electricalPanelMarker;

    [Header("Ocho Booth Encounter — Megaphone Barks (after power restored)")]
    [SerializeField] private string _powerRestoredBark1 = "Did you guys lose power?";
    [SerializeField] private string _powerRestoredBark2 = "Don't worry, it's Georgia, happens all the time.";
    [SerializeField] private float _barkGap = 2.5f;
    [SerializeField] private float _finalDelayBeforeNextSuspect = 1.5f;

    private SuspectCharacter _self;
    private bool _handled;
    private bool _spotted;
    private Coroutine _monitorCoroutine;
    private ApplicationLetter _spawnedApplicationLetter;

    private void Awake()
    {
        _self = GetComponent<SuspectCharacter>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _useDrawingForEntryReason)
            SuspectController.OnApplicationFormSpawned += HandleApplicationFormSpawned;
    }

    public override void OnNetworkDespawn()
    {
        SuspectController.OnApplicationFormSpawned -= HandleApplicationFormSpawned;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Fires for every suspect's application form — Ocho is the only active suspect at the booth
    /// while this component is alive, so the first (and only) call is always his own paperwork.
    /// </summary>
    private void HandleApplicationFormSpawned(ApplicationLetter applicationLetter)
    {
        SuspectController.OnApplicationFormSpawned -= HandleApplicationFormSpawned;
        _spawnedApplicationLetter = applicationLetter;
        applicationLetter.SetEntryReasonDrawing(true);
    }

    /// <summary>
    /// Injects scene-specific references that can't be baked into the prefab asset (Ocho is
    /// instantiated at runtime via <see cref="SuspectController.SpawnScriptedSuspect"/>, so any
    /// Transform living in the scene — the reappear point behind the player, the electrical
    /// panel marker — must be supplied by the spawner). Call immediately after spawning, e.g.
    /// from <c>Day_02.ArmOchoBoothEncounter</c>'s intercept.
    /// </summary>
    public void ConfigureSceneReferences(Transform reappearPoint, Transform electricalPanelMarker)
    {
        if (reappearPoint != null) _reappearPoint = reappearPoint;
        if (electricalPanelMarker != null) _electricalPanelMarker = electricalPanelMarker;
    }

    /// <summary>
    /// Called by <see cref="SuspectController.ExecuteVerdict"/> instead of the normal
    /// Pass/Quarantine/Kill switch. Server-only. Single-fire per encounter.
    /// </summary>
    public void HandleVerdictAttempt(FolderController folder)
    {
        if (!IsServer) return;
        if (_handled) return;
        _handled = true;

        StampContainer.StampType attemptedStamp = folder != null ? folder.StampType : StampContainer.StampType.Pass;
        StartCoroutine(VerdictRejectedSequence(attemptedStamp));
    }

    private IEnumerator VerdictRejectedSequence(StampContainer.StampType attemptedStamp)
    {
        SuspectController controller = SuspectController.Instance;

        _self.animator?.SetTrigger("Give");
        yield return new WaitForSeconds(_takeFolderDelay);

        controller.CleanupSpawnedFolderForRejectedVerdict();
        controller.SetCanInteract(false);

        // Ocho reacts to the verdict first, while still standing at the booth, before any of
        // the vanish/reappear theatrics kick in.
        string reactionLine = GetReactionLine(attemptedStamp);
        if (DialogueManager.Instance != null && !string.IsNullOrEmpty(reactionLine))
            DialogueManager.Instance.SayDialogue(_self, reactionLine);

        yield return new WaitForSeconds(_postLineDelay);

        // Now he vanishes and reappears behind the player, regardless of the verdict.
        VanishClientRpc();
        yield return new WaitForSeconds(_vanishToReappearDelay);

        if (_reappearPoint != null)
        {
            transform.position = _reappearPoint.position;
            transform.rotation = _reappearPoint.rotation;
        }

        ReappearClientRpc();
        yield return new WaitForSeconds(_reappearToLineDelay);

        // Wait until a player actually turns around and spots him standing behind them
        // (or the timeout elapses), then he startles them with a stinger and vanishes.
        yield return WaitForPlayerToSpotOcho();

        PlayStingerAndVanishClientRpc();
        yield return new WaitForSeconds(_stingerToVanishDelay);

        if (attemptedStamp != StampContainer.StampType.Kill)
        {
            // Quarantine and Pass also fail to process Ocho, but there's no blackout theatrics.
            controller.DespawnSuspectWithoutVerdict(_self);
            ShiftManager.Instance.SetNextSuspectReady();
            yield break;
        }

        RedStampSequence(controller);
    }

    private string GetReactionLine(StampContainer.StampType stamp)
    {
        switch (stamp)
        {
            case StampContainer.StampType.Kill: return _killRejectedLine;
            case StampContainer.StampType.Quarantine: return _quarantineRejectedLine;
            default: return _passRejectedLine;
        }
    }

    /// <summary>
    /// Server-only wait: blocks until any client reports (via <see cref="ReportSpottedServerRpc"/>)
    /// that its local player turned and spotted Ocho, or until <see cref="_spotTimeoutSeconds"/>
    /// elapses, whichever comes first.
    /// </summary>
    private IEnumerator WaitForPlayerToSpotOcho()
    {
        _spotted = false;
        float elapsed = 0f;
        while (!_spotted && elapsed < _spotTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportSpottedServerRpc()
    {
        _spotted = true;
    }

    private void RedStampSequence(SuspectController controller)
    {
        // Cut the power — the switch button (SwitchButton.PowerOff, already wired to this
        // ElectricObject in the scene) can't be readied again until the panel puzzle is solved.
        ElectricityController.Instance?.PowerOff();
        PlayDemonicLaughInDarkClientRpc();

        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients += OnPowerRestored;

        ShowElectricalPanelTutorialClientRpc();

        // Ocho is already gone (vanished after the stinger) — the encounter's visual beat is
        // complete, he just disappears into the dark for good.
        controller.DespawnSuspectWithoutVerdict(_self);
    }

    /// <summary>
    /// Fired locally on every client (host and clients alike) via
    /// <see cref="ElectricityController.OnPowerRestoredAllClients"/>. Server-only side effects
    /// (megaphone barks, advancing the shift) are guarded; local UI cleanup runs everywhere.
    /// </summary>
    private void OnPowerRestored()
    {
        if (ElectricityController.Instance != null)
            ElectricityController.Instance.OnPowerRestoredAllClients -= OnPowerRestored;

        if (TutorialMarkerManager.Instance != null && _electricalPanelMarker != null)
            TutorialMarkerManager.Instance.Unmark(_electricalPanelMarker);

        TutorialOverlay.Instance?.Close();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        StartCoroutine(PowerRestoredBarkSequence());
    }

    private IEnumerator PowerRestoredBarkSequence()
    {
        MegaphoneDialogueManager.Instance?.ShowDialogueSynced(_powerRestoredBark1);
        yield return new WaitForSeconds(_barkGap);

        MegaphoneDialogueManager.Instance?.ShowDialogueSynced(_powerRestoredBark2);
        yield return new WaitForSeconds(_finalDelayBeforeNextSuspect);

        ShiftManager.Instance.SetNextSuspectReady();
    }

    // ── Client-visible effects ──────────────────────────────────────────────

    [ClientRpc]
    private void VanishClientRpc()
    {
        StopMonitoring();
        SetVisible(false);
    }

    [ClientRpc]
    private void ReappearClientRpc()
    {
        SetVisible(true);
        StopMonitoring();
        _monitorCoroutine = StartCoroutine(MonitorForLocalPlayerSpot());
    }

    [ClientRpc]
    private void PlayStingerAndVanishClientRpc()
    {
        StopMonitoring();

        if (_audioSource != null && _stingerClip != null)
            _audioSource.PlayOneShot(_stingerClip);

        SetVisible(false);
    }

    [ClientRpc]
    private void PlayDemonicLaughInDarkClientRpc()
    {
        if (_audioSource != null && _demonicLaughClip != null)
            _audioSource.PlayOneShot(_demonicLaughClip);
    }

    [ClientRpc]
    private void ShowElectricalPanelTutorialClientRpc()
    {
        if (TutorialMarkerManager.Instance != null && _electricalPanelMarker != null)
            TutorialMarkerManager.Instance.Mark(_electricalPanelMarker);

        TutorialOverlay.Instance?.ShowElectricalPanelTutorial();
    }

    private void SetVisible(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
    }

    private void StopMonitoring()
    {
        if (_monitorCoroutine == null) return;
        StopCoroutine(_monitorCoroutine);
        _monitorCoroutine = null;
    }

    /// <summary>
    /// Runs locally on every client while Ocho is visible behind the player. Checks this
    /// client's own local player camera (<see cref="PlayerInstance.Instance"/>) against Ocho's
    /// position — a narrow forward cone plus an unobstructed raycast counts as "spotted". The
    /// first client to detect this reports it to the server via <see cref="ReportSpottedServerRpc"/>.
    /// Stopped externally by <see cref="StopMonitoring"/> once the server moves on (either because
    /// a client reported a spot, or the wait timed out).
    /// </summary>
    private IEnumerator MonitorForLocalPlayerSpot()
    {
        while (true)
        {
            Camera cam = Camera.main;
            if (PlayerInstance.Instance != null && cam != null)
            {
                Vector3 toOcho = transform.position - cam.transform.position;
                float dist = toOcho.magnitude;

                if (dist > 0.05f)
                {
                    Vector3 dir = toOcho / dist;
                    float dot = Vector3.Dot(cam.transform.forward, dir);

                    if (dot >= _spotDotThreshold &&
                        (!Physics.Raycast(cam.transform.position, dir, out RaycastHit hit, dist) ||
                         hit.transform.IsChildOf(transform)))
                    {
                        ReportSpottedServerRpc();
                        yield break;
                    }
                }
            }

            yield return new WaitForSeconds(_spotCheckInterval);
        }
    }
}
