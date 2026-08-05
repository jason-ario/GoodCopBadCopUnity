using System.Collections;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative procedural cutscene: a giant Ocho is glimpsed standing at the end of
/// the Day 2 "Follow the Trail" (dead animal) blood trail. Armed only while that specific
/// task is live — call <see cref="TriggerTask"/> from <see cref="Day_02.ActivateDay2TrailEvent"/>,
/// right when the "Follow the trail" HUD task goes active, and <see cref="DebugReset"/> from
/// <see cref="Day_02.DayDeactivated"/> so the cutscene never lingers armed into another day.
///
/// This is intentionally the same pattern as <see cref="OchoEatingVladCutscene"/>, trimmed down
/// (no Vlad pieces, no eating loop) — Ocho just idles until a player gets close, plays his
/// "stop and look" animation for a few seconds, then bounds away through a series of waypoints
/// and disappears into the woods:
///   1. Once a player comes within <see cref="_detectionRadius"/>, Ocho's FIMSpace
///      <see cref="FLookAnimator"/> ("Look Animator") switches on and looks at the nearest
///      player while the "StopAndLook" Animator trigger plays his stop-and-look animation.
///   2. He holds that pose for <see cref="_lookHoldDuration"/> seconds.
///   3. He DOTween-DOJumps through <see cref="_jumpWaypoints"/> in order — playing
///      <see cref="_jumpSound"/> on every takeoff and <see cref="_landSound"/> on every
///      landing — then, right as he vanishes, plays <see cref="_disappearStinger"/> and
///      deactivates.
///
/// The mutant pack players actually fight at this location is spawned separately by
/// <see cref="FollowTrailThreat"/>'s configured <c>PackSpawner</c>/<c>PackSize</c> — this
/// component only drives the background Ocho sighting, it never attacks or takes damage.
///
/// Scene setup:
///   - Place this component on the "---Ocho In Woods Cutscene" root GameObject.
///   - Requires a NetworkObject on this GameObject (in-scene placed — no prefab
///     registration needed, Netcode spawns scene-placed NetworkObjects automatically).
///   - Assign <see cref="_ochoAnimator"/> to "Ocho Mutant/Ocho Final"'s Animator
///     (controller: "Ocho In Woods Cutscene", triggers: StopAndLook, Idle).
///   - Assign <see cref="_lookAnimator"/> to the same object's attached FLookAnimator.
///   - Assign <see cref="_jumpWaypoints"/> in traversal order (e.g. children "GameObject",
///     "GameObject (1)", "GameObject (2)", "GameObject (3)").
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class OchoInWoodsCutscene : NetworkBehaviour
{
    public static OchoInWoodsCutscene Instance { get; private set; }

    // ── Inspector — Ocho ─────────────────────────────────────────────────────

    [Header("Ocho")]
    [Tooltip("Ocho's Animator (controller: 'Ocho In Woods Cutscene'). Drives StopAndLook / Idle triggers.")]
    [SerializeField] private Animator _ochoAnimator;

    [Tooltip("Ocho's root Transform, moved during the jump sequence. Defaults to the Animator's Transform if left empty.")]
    [SerializeField] private Transform _ochoRoot;

    [Tooltip("GameObject deactivated once the jump sequence finishes. Defaults to the Animator's GameObject if left empty.")]
    [SerializeField] private GameObject _ochoGameObjectToDeactivate;

    [SerializeField] private string _stopAndLookTrigger = "StopAndLook";
    [SerializeField] private string _idleTrigger = "Idle";

    [Header("Look Animator")]
    [Tooltip("The FIMSpace FLookAnimator attached to Ocho. Its look target is set dynamically " +
             "to the local player and toggled on/off procedurally instead of hand-rotating any bone.")]
    [SerializeField] private FLookAnimator _lookAnimator;

    [Tooltip("Transition time (seconds) used when enabling/disabling the look animator.")]
    [SerializeField] private float _lookTransitionTime = 0.4f;

    // ── Inspector — Audio ────────────────────────────────────────────────────

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Played once, right when Ocho stops and looks toward the player.")]
    [SerializeField] private AudioClip _stopAndLookClip;

    [Tooltip("Played on every takeoff during the jump sequence.")]
    [SerializeField] private AudioClip _jumpSound;

    [Tooltip("Played on every landing during the jump sequence.")]
    [SerializeField] private AudioClip _landSound;

    [Tooltip("Horror stinger played the instant Ocho vanishes into the woods, at the end of the jump sequence.")]
    [SerializeField] private AudioClip _disappearStinger;

    // ── Inspector — Proximity ────────────────────────────────────────────────

    [Header("Proximity")]
    [Tooltip("Distance at which a player triggers Ocho to stop and look, then flee.")]
    [SerializeField] private float _detectionRadius = 15f;

    [Tooltip("How frequently (seconds) the server checks player distance while armed.")]
    [SerializeField] private float _tickInterval = 0.25f;

    // ── Inspector — Look Timing ──────────────────────────────────────────────

    [Header("Look Timing")]
    [Tooltip("How long Ocho holds his stop-and-look pose before bounding off into the woods.")]
    [SerializeField] private float _lookHoldDuration = 4f;

    [Tooltip("Buffer after the Idle trigger fires, before the jump sequence starts (lets the blend settle).")]
    [SerializeField] private float _postLookDelay = 0.35f;

    // ── Inspector — Jump Sequence ─────────────────────────────────────────────

    [Header("Jump Sequence")]
    [Tooltip("Waypoints Ocho jumps through, in order, before he disappears into the woods.")]
    [SerializeField] private Transform[] _jumpWaypoints;

    [SerializeField] private float _turnDuration = 0.2f;
    [SerializeField] private float _jumpDuration = 0.6f;
    [SerializeField] private float _jumpPower = 3f;
    [SerializeField] private float _waypointPause = 0.05f;
    [SerializeField] private float _postJumpDelay = 0.3f;

    // ── Runtime state ────────────────────────────────────────────────────────

    private bool _triggered;
    private Coroutine _monitorRoutine;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[OchoInWoodsCutscene] Duplicate instance detected — destroying self.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (_ochoRoot == null && _ochoAnimator != null)
            _ochoRoot = _ochoAnimator.transform;
        if (_ochoGameObjectToDeactivate == null && _ochoAnimator != null)
            _ochoGameObjectToDeactivate = _ochoAnimator.gameObject;

        // Ensure Ocho starts hidden regardless of the GameObject's saved active state in the
        // scene — he must stay deactivated until TriggerTask() arms and shows him (right when
        // the Day 2 "Follow the Trail" task goes active), and hidden again once the sighting
        // sequence completes or DebugReset runs.
        LocalDeactivateOcho();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Arms the cutscene: Ocho becomes visible and begins watching for a nearby player.
    /// SERVER ONLY. Safe to call once per activation of the Day 2 "Follow the Trail" task —
    /// repeated calls are ignored while already armed/running.
    /// Call from Day_02.ActivateDay2TrailEvent().
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;
        if (_triggered) return;
        _triggered = true;

        ShowOchoClientRpc();
        _monitorRoutine = StartCoroutine(MonitorProximity());

        Debug.Log("[OchoInWoodsCutscene] Armed — watching for player proximity.", this);
    }

    /// <summary>
    /// Disarms and hides the cutscene so it can be re-armed. SERVER ONLY. Call from
    /// Day_02.DayDeactivated() so the cutscene never lingers armed/visible into another day.
    /// </summary>
    public void DebugReset()
    {
        if (!IsServer) return;

        if (_monitorRoutine != null)
        {
            StopCoroutine(_monitorRoutine);
            _monitorRoutine = null;
        }

        _triggered = false;
        HideOchoClientRpc();
    }

    // ── Server loop ──────────────────────────────────────────────────────────

    private IEnumerator MonitorProximity()
    {
        // One-frame delay so everything is fully initialised before the first check.
        yield return null;

        while (true)
        {
            Transform nearest = FindNearestPlayer();
            if (nearest != null && _ochoRoot != null)
            {
                float sqrDist = (nearest.position - _ochoRoot.position).sqrMagnitude;
                if (sqrDist <= _detectionRadius * _detectionRadius)
                    break;
            }

            yield return new WaitForSeconds(_tickInterval);
        }

        _monitorRoutine = null;
        yield return StartCoroutine(RunCutsceneSequence());
    }

    private IEnumerator RunCutsceneSequence()
    {
        PlayStopAndLookClientRpc();
        SetLookingClientRpc(true);
        yield return new WaitForSeconds(_lookHoldDuration);

        SetLookingClientRpc(false);
        PlayIdleClientRpc();
        yield return new WaitForSeconds(_postLookDelay);

        PlayJumpSequenceAndVanishClientRpc();
    }

    // ── Client RPCs (also execute locally on the host) ───────────────────────

    [ClientRpc]
    private void ShowOchoClientRpc() => LocalShowOcho();

    [ClientRpc]
    private void HideOchoClientRpc() => LocalDeactivateOcho();

    [ClientRpc]
    private void PlayStopAndLookClientRpc() => LocalPlayStopAndLook();

    [ClientRpc]
    private void SetLookingClientRpc(bool value) => LocalSetLooking(value);

    [ClientRpc]
    private void PlayIdleClientRpc() => LocalPlayIdle();

    [ClientRpc]
    private void PlayJumpSequenceAndVanishClientRpc() => LocalPlayJumpSequenceAndVanish();

    // ── Local (client-visual) implementations ────────────────────────────────

    private void LocalShowOcho()
    {
        if (_ochoGameObjectToDeactivate != null)
            _ochoGameObjectToDeactivate.SetActive(true);
    }

    private void LocalPlayStopAndLook()
    {
        if (_ochoAnimator != null && !string.IsNullOrEmpty(_stopAndLookTrigger))
            _ochoAnimator.SetTrigger(_stopAndLookTrigger);

        if (_audioSource != null && _stopAndLookClip != null)
            _audioSource.PlayOneShot(_stopAndLookClip);
    }

    private void LocalPlayIdle()
    {
        if (_ochoAnimator != null && !string.IsNullOrEmpty(_idleTrigger))
            _ochoAnimator.SetTrigger(_idleTrigger);
    }

    private void LocalPlayJumpSequenceAndVanish()
    {
        if (_ochoRoot == null || _jumpWaypoints == null || _jumpWaypoints.Length == 0)
        {
            PlayDisappearStinger();
            LocalDeactivateOcho();
            return;
        }

        Sequence seq = DOTween.Sequence();

        for (int i = 0; i < _jumpWaypoints.Length; i++)
        {
            Transform waypoint = _jumpWaypoints[i];
            if (waypoint == null) continue;

            bool isLastWaypoint = i == _jumpWaypoints.Length - 1;
            Vector3 destination = waypoint.position;
            Transform ocho = _ochoRoot;

            seq.Append(ocho.DORotate(FacingEuler(ocho.position, destination), _turnDuration));
            seq.AppendCallback(PlayJumpSound);
            seq.Append(ocho.DOJump(destination, _jumpPower, 1, _jumpDuration, false));
            seq.AppendCallback(PlayLandSound);

            if (isLastWaypoint)
                seq.AppendCallback(PlayDisappearStinger);

            if (_waypointPause > 0f)
                seq.AppendInterval(_waypointPause);
        }

        seq.AppendInterval(_postJumpDelay);
        seq.AppendCallback(LocalDeactivateOcho);

        seq.Play();
    }

    private void PlayJumpSound()
    {
        if (_jumpSound != null && _audioSource != null)
            _audioSource.PlayOneShot(_jumpSound);
    }

    private void PlayLandSound()
    {
        if (_landSound != null && _audioSource != null)
            _audioSource.PlayOneShot(_landSound);
    }

    private void PlayDisappearStinger()
    {
        if (_disappearStinger != null && _audioSource != null)
            _audioSource.PlayOneShot(_disappearStinger);
    }

    private void LocalDeactivateOcho()
    {
        if (_ochoGameObjectToDeactivate != null)
            _ochoGameObjectToDeactivate.SetActive(false);
    }

    // ── Look Animator ────────────────────────────────────────────────────────

    /// <summary>
    /// Dynamically points the attached FLookAnimator at the local player and switches its
    /// look weight on, or switches it back off. Each client sets its own local player as
    /// the target — a reasonable approximation for a background NPC's gaze in co-op,
    /// without needing extra network state.
    /// </summary>
    private void LocalSetLooking(bool value)
    {
        if (_lookAnimator == null) return;

        if (value)
        {
            Transform target = FindLocalLookTarget();
            if (target != null)
                _lookAnimator.SetLookTarget(target);

            _lookAnimator.SwitchLooking(true, _lookTransitionTime);
        }
        else
        {
            _lookAnimator.SwitchLooking(false, _lookTransitionTime);
        }
    }

    private Transform FindLocalLookTarget() =>
        PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>SERVER ONLY — uses the authoritative connected-clients list.</summary>
    private Transform FindNearestPlayer()
    {
        Transform nearest = null;
        float nearestSqrDist = float.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            float sqrDist = (client.PlayerObject.transform.position - _ochoRoot.position).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = client.PlayerObject.transform;
            }
        }

        return nearest;
    }

    private static Vector3 FacingEuler(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return Vector3.zero;
        return Quaternion.LookRotation(direction).eulerAngles;
    }
}
