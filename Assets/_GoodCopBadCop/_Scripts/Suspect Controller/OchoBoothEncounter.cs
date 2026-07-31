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
///   - Every verdict: once he vanishes after the stinger, the booth's power cuts out and he
///     cackles from the dark, leaving the player to go fix the breaker at
///     <see cref="ElectricPanelController"/> before the switch button will let them summon the
///     next suspect (already gated by <see cref="SwitchButton"/>'s powerOn check). Once power is
///     restored the megaphone plays a couple of dismissive barks and the next suspect can be
///     called.
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
    [SerializeField] private float _spotDotThreshold = 0.5f;

    [Tooltip("Maximum distance (world units) from the local player's camera at which Ocho can still be spotted. " +
             "Should comfortably cover the booth interior around the reappear point.")]
    [SerializeField] private float _spotMaxDistance = 15f;

    [Tooltip("Logs the local spot-check state (camera found, distance, dot) to the console every check " +
             "while waiting for the player to turn around. Turn off once the reveal is confirmed working.")]
    [SerializeField] private bool _logSpotChecks = true;

    [Tooltip("Seconds between visibility checks on each client while waiting for a player to turn around.")]
    [SerializeField] private float _spotCheckInterval = 0.15f;

    [Tooltip("Maximum seconds to wait for a player to spot Ocho before giving up and having him vanish anyway.")]
    [SerializeField] private float _spotTimeoutSeconds = 12f;

    [Header("Ocho Booth Encounter — Jumpscare Zoom")]
    [Tooltip("Seconds the zoom-in takes, from wherever Ocho is standing to right next to the player, " +
             "before he vanishes and the stinger plays.")]
    [SerializeField] private float _jumpscareZoomDuration = 0.18f;

    [Tooltip("Vertical offset (world units, negative = below) from the local player's own root position that " +
             "Ocho zooms toward. E.g. -0.3 zooms him to a point 0.3 units below the player's root.")]
    [SerializeField] private float _jumpscareTargetHeightOffset = -0.3f;

    [Tooltip("Fallback distance in front of the camera Ocho zooms to if no local PlayerInstance can be found " +
             "to compute the player-root-relative target (should not normally happen in play).")]
    [SerializeField] private float _jumpscareCloseDistance = 0.75f;

    [Tooltip("Scale multiplier applied to Ocho's root at the peak of the jumpscare for extra punch (1 = no change).")]
    [SerializeField] private float _jumpscareScaleMultiplier = 1.15f;

    [Header("Ocho Booth Encounter — Power Outage")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _demonicLaughClip;

    [Tooltip("Transform the tutorial arrow points at while the power is out (usually the Electrical Panel).")]
    [SerializeField] private Transform _electricalPanelMarker;

    [Tooltip("The Electrical Panel's own Interactable (e.g. ElectricPanelController), force-highlighted " +
             "while the power outage tutorial is showing so the panel visually stands out, not just the " +
             "arrow above it. Cleared the moment power is restored.")]
    [SerializeField] private Interactable _electricalPanelInteractable;

    [Header("Ocho Booth Encounter — Megaphone Barks (after power restored)")]
    [SerializeField] private string _powerRestoredBark1 = "Did you guys lose power?";
    [SerializeField] private string _powerRestoredBark2 = "Don't worry, it's Georgia, happens all the time.";
    [SerializeField] private float _barkGap = 2.5f;
    [SerializeField] private float _finalDelayBeforeNextSuspect = 1.5f;

    private SuspectCharacter _self;
    private Collider _ochoCollider;
    private bool _handled;
    private bool _spotted;
    private Coroutine _monitorCoroutine;
    private ApplicationLetter _spawnedApplicationLetter;

    private void Awake()
    {
        _self = GetComponent<SuspectCharacter>();
        _ochoCollider = GetComponent<Collider>();
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
    public void ConfigureSceneReferences(Transform reappearPoint, Transform electricalPanelMarker, Interactable electricalPanelInteractable = null)
    {
        if (reappearPoint != null) _reappearPoint = reappearPoint;
        if (electricalPanelMarker != null) _electricalPanelMarker = electricalPanelMarker;
        if (electricalPanelInteractable != null) _electricalPanelInteractable = electricalPanelInteractable;
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

        // Same ambient screen-glitch/film-grain beat used when a fully-mutated suspect is
        // presenting at the booth (see GlitchController) — kicks in as Ocho starts his
        // reaction and carries through the vanish/reappear/jumpscare/power-outage beats.
        // Clears automatically once he's despawned at the end of RedStampSequence.
        _self.TriggerUncannyGlitchPresence();

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

        // The stinger fires immediately, but the visual vanish (see JumpscareZoomAndVanish) only
        // completes once the client-local zoom-in animation finishes — wait exactly that long so
        // the power outage lands the instant he actually disappears, not some arbitrary delay later.
        yield return new WaitForSeconds(_jumpscareZoomDuration);

        Debug.Log($"[OchoBoothEncounter] Verdict was {attemptedStamp} — running the power outage sequence.");
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
    /// elapses, whichever comes first. <see cref="_spotted"/> is deliberately NOT reset here — a
    /// client can (and often does) report a spot before this method even starts running, e.g. when
    /// the player was already looking straight at the reappear point the instant Ocho becomes
    /// visible; resetting the flag here would silently discard that already-received report and
    /// leave the server waiting forever for a second report that will never come, since the
    /// client's monitor coroutine already exited after its first successful report.
    /// </summary>
    private IEnumerator WaitForPlayerToSpotOcho()
    {
        Debug.Log($"[OchoBoothEncounter] Server: WaitForPlayerToSpotOcho started (spotted so far: {_spotted}), waiting for spot report or timeout.");

        float elapsed = 0f;
        while (!_spotted && elapsed < _spotTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.Log(_spotted
            ? $"[OchoBoothEncounter] Server: player reported spotting Ocho (elapsed={elapsed:F1}s)."
            : $"[OchoBoothEncounter] Server: spot wait timed out after {elapsed:F1}s — continuing anyway.");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportSpottedServerRpc()
    {
        Debug.Log($"[OchoBoothEncounter] ReportSpottedServerRpc received on server (IsServer={IsServer}). Setting _spotted=true.");
        _spotted = true;
    }

    private void RedStampSequence(SuspectController controller)
    {
        Debug.Log($"[OchoBoothEncounter] RedStampSequence: ElectricityController.Instance={(ElectricityController.Instance != null ? "found" : "NULL")}.");

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

        _electricalPanelInteractable?.SetForceHighlight(false);

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
        Debug.Log("[OchoBoothEncounter] ReappearClientRpc received on this client — starting spot monitor.");
        SetVisible(true);
        StopMonitoring();
        _monitorCoroutine = StartCoroutine(MonitorForLocalPlayerSpot());
    }

    [ClientRpc]
    private void PlayStingerAndVanishClientRpc()
    {
        Debug.Log($"[OchoBoothEncounter] PlayStingerAndVanishClientRpc received — " +
                  $"audioSource={(_audioSource != null ? _audioSource.name : "NULL")}, " +
                  $"clip={(_stingerClip != null ? _stingerClip.name : "NULL")}.");

        StopMonitoring();
        StartCoroutine(JumpscareZoomAndVanish());
    }

    /// <summary>
    /// Client-local only: rapidly drives Ocho's actual root transform (not just his visual mesh)
    /// from wherever he's standing to right next to the player for a cheap jumpscare punch, then
    /// plays the stinger and hides him. Moving the root — rather than a child mesh/rig transform —
    /// avoids fighting the Animator's own root motion on the rig (which was fighting a child-only
    /// move and causing the legs to appear stuck/sliding). The root's <see cref="NetworkTransform"/>
    /// is temporarily disabled on this client only for the duration of the animation so its
    /// authoritative sync doesn't snap Ocho back to his last-replicated position mid-zoom — this is
    /// a purely client-visual gag, the server's own copy of his transform never actually changes.
    /// His own <see cref="Collider"/> is also disabled for the same duration so he doesn't physically
    /// shove/collide with the player while zooming through their position. The stinger plays through
    /// <see cref="SFXController"/> rather than Ocho's own <see cref="AudioSource"/> (so it keeps
    /// playing even after he vanishes and is despawned moments later), and fires immediately as the
    /// zoom starts — the instant the player is confirmed to have spotted him — rather than waiting
    /// for the punch-in to finish.
    /// Falls back to a plain stinger + hide if no camera is found (e.g. dedicated server).
    /// </summary>
    private IEnumerator JumpscareZoomAndVanish()
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
            SFXController.Instance?.Play(_stingerClip);
            SetVisible(false);
            yield break;
        }

        Unity.Netcode.Components.NetworkTransform netTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

        Vector3 originalPos = transform.position;
        Quaternion originalRot = transform.rotation;
        Vector3 originalScale = transform.localScale;

        // Zoom toward a point just below the local player's own root position (their feet/pivot),
        // rather than a fixed distance in front of the camera — this way the jumpscare always
        // lands "on" the player regardless of where they're standing or how they're oriented.
        Vector3 targetPos = PlayerInstance.Instance != null
            ? PlayerInstance.Instance.transform.position + new Vector3(0f, _jumpscareTargetHeightOffset, 0f)
            : cam.transform.position + cam.transform.forward * _jumpscareCloseDistance;

        Vector3 lookDir = cam.transform.position - targetPos;
        lookDir.y = 0f;
        Quaternion targetRot = lookDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
            : originalRot;
        Vector3 targetScale = originalScale * _jumpscareScaleMultiplier;

        if (netTransform != null) netTransform.enabled = false;
        if (_ochoCollider != null) _ochoCollider.enabled = false;

        // Fire the stinger the instant the player spots him — right as the zoom starts — rather
        // than after he's already zipped over. The jumpscare "hit" needs to land the moment he's
        // seen, not half a beat later once the punch-in animation has finished playing out.
        SFXController.Instance?.Play(_stingerClip);

        float t = 0f;
        while (t < _jumpscareZoomDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / _jumpscareZoomDuration);
            float eased = 1f - Mathf.Pow(1f - p, 3f); // fast punch-in

            transform.position = Vector3.Lerp(originalPos, targetPos, eased);
            transform.rotation = Quaternion.Slerp(originalRot, targetRot, eased);
            transform.localScale = Vector3.Lerp(originalScale, targetScale, eased);

            yield return null;
        }

        SetVisible(false);

        // Restore his real transform so nothing is left offset once he's despawned (or in case
        // despawn is delayed for any reason).
        transform.position = originalPos;
        transform.rotation = originalRot;
        transform.localScale = originalScale;

        if (netTransform != null) netTransform.enabled = true;
        if (_ochoCollider != null) _ochoCollider.enabled = true;
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

        _electricalPanelInteractable?.SetForceHighlight(true);

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
    /// position, a narrow forward cone counts as "spotted" (deliberately no line-of-sight
    /// raycast: the booth interior is cluttered with interaction/trigger colliders that would
    /// otherwise block the check almost every time even when the player is looking straight at
    /// him). The first client to detect this reports it to the server via
    /// <see cref="ReportSpottedServerRpc"/>. Stopped externally by <see cref="StopMonitoring"/>
    /// once the server moves on (either because a client reported a spot, or the wait timed out).
    /// </summary>
    private IEnumerator MonitorForLocalPlayerSpot()
    {
        Debug.Log("[OchoBoothEncounter] Spot monitor started on this client.");

        while (true)
        {
            Camera cam = Camera.main;

            if (PlayerInstance.Instance == null)
            {
                if (_logSpotChecks) Debug.Log("[OchoBoothEncounter] Spot check: no local PlayerInstance yet.");
            }
            else if (cam == null)
            {
                if (_logSpotChecks) Debug.Log("[OchoBoothEncounter] Spot check: Camera.main is null.");
            }
            else
            {
                Vector3 toOcho = transform.position - cam.transform.position;
                float dist = toOcho.magnitude;

                if (dist <= 0.05f)
                {
                    if (_logSpotChecks) Debug.Log($"[OchoBoothEncounter] Spot check: distance too small ({dist:F2}).");
                }
                else if (dist > _spotMaxDistance)
                {
                    if (_logSpotChecks) Debug.Log($"[OchoBoothEncounter] Spot check: out of range (dist={dist:F1}, max={_spotMaxDistance}).");
                }
                else
                {
                    Vector3 dir = toOcho / dist;
                    float dot = Vector3.Dot(cam.transform.forward, dir);

                    if (_logSpotChecks) Debug.Log($"[OchoBoothEncounter] Spot check: dist={dist:F1}, dot={dot:F2} (need >= {_spotDotThreshold}).");

                    if (dot >= _spotDotThreshold)
                    {
                        Debug.Log("[OchoBoothEncounter] Player spotted Ocho — reporting to server.");
                        ReportSpottedServerRpc();
                        yield break;
                    }
                }
            }

            yield return new WaitForSeconds(_spotCheckInterval);
        }
    }
}
