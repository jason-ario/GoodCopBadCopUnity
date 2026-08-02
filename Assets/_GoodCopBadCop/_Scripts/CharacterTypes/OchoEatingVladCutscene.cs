using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// Server-authoritative procedural cutscene: Mutant Ocho is discovered on the checkpoint
/// roof eating Vlad's remains. Armed on Day 3 — call <see cref="TriggerTask"/> from
/// <see cref="Day_03.DayActivated"/>, right as the player exits the bunker (matches the
/// pattern used by <c>TakeOutTrashTask</c>/<c>CleanBoothMessTask</c> for Day 3 pre-shift tasks).
///
/// Sequence, once a player comes within <see cref="_detectionRadius"/> of Ocho:
///   1. Ocho stops eating: the eating-loop sound is cut (the "StopAndLook" Animator
///      trigger/animation is disabled — see <see cref="LocalPlayStopAndLook"/>).
///   2. Immediately after, he starts looking at the nearest player by dynamically setting
///      the attached FIMSpace <see cref="FLookAnimator"/>'s look target and switching its
///      look weight on (see <see cref="LocalSetLooking"/>), and holds that look for
///      <see cref="_lookHoldDuration"/>.
///   3. He drops the held Vlad pieces. For each entry in <see cref="_vladPieceRoots"/>, a
///      pre-spawned networked "physics double" (see <see cref="_networkedVladPiecePrefabs"/>)
///      takes over: its <see cref="ParentConstraint"/> (which tracked the held visual piece
///      every frame) is switched off, the original held visual piece is hidden, and the
///      double's Rigidbody goes non-kinematic on the server — its NetworkRigidbody +
///      NetworkTransform then replicate the physical fall to every client. Then the
///      Animator's "Idle" trigger plays his idle animation.
///   4. He DOTween-DOJumps through <see cref="_jumpWaypoints"/> in order, then disappears
///      (the Ocho GameObject is deactivated).
///
/// Networked Vlad pieces (spawn/track/drop):
///   - As soon as <see cref="TriggerTask"/> is called, the server instantiates and
///     network-spawns one <see cref="_networkedVladPiecePrefabs"/> instance per
///     <see cref="_vladPieceRoots"/> entry (index-paired). Each spawned double must have
///     NetworkObject + NetworkTransform + <c>Unity.Netcode.Components.NetworkRigidbody</c> +
///     Rigidbody, and be registered in the project's NetworkPrefabsList
///     ("Assets/DefaultNetworkPrefabs.asset") — see "Vlad Torso Networked.prefab" /
///     "Vlad Head Networked.prefab" for the reference setup (single rigid body each; the
///     torso's original per-bone ragdoll rigidbodies/joints were stripped since NGO cannot
///     replicate a multi-body ragdoll — it falls as one rigid chunk instead of flailing limbs).
///   - Every client (via <see cref="SetupNetworkedPieceClientRpc"/>) adds/enables a
///     <see cref="ParentConstraint"/> on its local copy of the double, sourced from the
///     corresponding <see cref="_vladPieceRoots"/> entry, and disables the double's renderers,
///     colliders, and NetworkTransform while held — the constraint alone keeps it glued to
///     the (already client-synced) held visual piece, at zero bandwidth cost, exactly like the
///     hand-follow pattern in <c>PlayerPickupController</c>/<c>PickableObject</c>.
///   - On drop (<see cref="DropNetworkedVladPieces"/>), the constraint is disabled, the
///     double's renderers/colliders/NetworkTransform are re-enabled on every client, the
///     original held visual piece is hidden, and only the server flips the double's
///     Rigidbody to non-kinematic — NetworkRigidbody's AutoUpdateKinematicState keeps every
///     other client's copy kinematic and driven by the server's replicated transform.
///
/// Scene setup:
///   - Place this component on the "---Ocho eating vlad cutscene" root GameObject.
///   - Requires a NetworkObject on this GameObject (in-scene placed — no prefab
///     registration needed, Netcode spawns scene-placed NetworkObjects automatically).
///   - Assign <see cref="_ochoAnimator"/> to "Ocho Final (1)"'s Animator
///     (controller: "Ocho Eating Cutscene", triggers: StopAndLook, Idle).
///   - Assign <see cref="_lookAnimator"/> to "Ocho Final (1)"'s attached FLookAnimator
///     ("Look Animator 2") component.
///   - Assign <see cref="_vladPieceRoots"/> pairwise with <see cref="_networkedVladPiecePrefabs"/>,
///     e.g. index 0: root = "Vlad Torso", prefab = "Vlad Torso Networked.prefab";
///     index 1: root = "Vlad Head", prefab = "Vlad Head Networked.prefab".
///   - Assign <see cref="_jumpWaypoints"/> in traversal order (e.g. children "2", "3", "4", "5").
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class OchoEatingVladCutscene : NetworkBehaviour
{
    public static OchoEatingVladCutscene Instance { get; private set; }

    // ── Inspector — Ocho ─────────────────────────────────────────────────────

    [Header("Ocho")]
    [Tooltip("Ocho's Animator (controller: 'Ocho Eating Cutscene'). Drives StopAndLook / Idle triggers.")]
    [SerializeField] private Animator _ochoAnimator;

    [Tooltip("Ocho's root Transform, moved during the jump sequence. Defaults to the Animator's Transform if left empty.")]
    [SerializeField] private Transform _ochoRoot;

    [Tooltip("GameObject deactivated once the jump sequence finishes. Defaults to the Animator's GameObject if left empty.")]
    [SerializeField] private GameObject _ochoGameObjectToDeactivate;

    [SerializeField] private string _stopAndLookTrigger = "StopAndLook";
    [SerializeField] private string _idleTrigger = "Idle";

    [Header("Look Animator")]
    [Tooltip("The FIMSpace FLookAnimator ('Look Animator 2') attached to Ocho. Its look " +
             "target is set dynamically to the local player and toggled on/off procedurally " +
             "instead of hand-rotating any bone.")]
    [SerializeField] private FLookAnimator _lookAnimator;

    [Tooltip("Transition time (seconds) used when enabling/disabling the look animator.")]
    [SerializeField] private float _lookTransitionTime = 0.4f;

    // ── Inspector — Audio ────────────────────────────────────────────────────

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _eatingLoopClip;
    [SerializeField] private AudioClip _stopEatingClip;
    [SerializeField] private AudioClip _jumpClip;

    // ── Inspector — Proximity ────────────────────────────────────────────────

    [Header("Proximity")]
    [Tooltip("Distance at which a player triggers Ocho to stop eating and look.")]
    [SerializeField] private float _detectionRadius = 15f;

    [Tooltip("How frequently (seconds) the server checks player distance while armed.")]
    [SerializeField] private float _tickInterval = 0.25f;

    // ── Inspector — Look Timing ──────────────────────────────────────────────

    [Header("Look Timing")]
    [Tooltip("How long Ocho holds his look-at-player pose before dropping Vlad's pieces.")]
    [SerializeField] private float _lookHoldDuration = 2f;

    [Tooltip("Buffer after dropping the pieces / triggering Idle, before the jump sequence starts (lets the Idle blend settle).")]
    [SerializeField] private float _postDropDelay = 0.35f;

    // ── Inspector — Vlad Pieces ──────────────────────────────────────────────

    [Header("Vlad Pieces (parallel arrays — index i of each list is one piece)")]
    [Tooltip("The piece's held/visual root Transform (e.g. 'Vlad Torso', 'Vlad Head'). Hidden " +
             "on drop once its networked double takes over.")]
    [SerializeField] private Transform[] _vladPieceRoots;

    [Tooltip("Networked physics-double prefab for each piece, index-paired with " +
             "_vladPieceRoots (e.g. 'Vlad Torso Networked.prefab', 'Vlad Head Networked.prefab'). " +
             "Must have NetworkObject + NetworkTransform + NetworkRigidbody + Rigidbody at the " +
             "root, and be registered in Assets/DefaultNetworkPrefabs.asset.")]
    [SerializeField] private GameObject[] _networkedVladPiecePrefabs;

    // ── Inspector — Jump Sequence ─────────────────────────────────────────────

    [Header("Jump Sequence")]
    [Tooltip("Waypoints Ocho jumps through, in order, before he disappears.")]
    [SerializeField] private Transform[] _jumpWaypoints;

    [SerializeField] private float _turnDuration = 0.2f;
    [SerializeField] private float _jumpDuration = 0.6f;
    [SerializeField] private float _jumpPower = 3f;
    [SerializeField] private float _waypointPause = 0.05f;
    [SerializeField] private float _postJumpDelay = 0.3f;

    // ── Runtime state ────────────────────────────────────────────────────────

    private bool _triggered;
    private Coroutine _monitorRoutine;

    // Index-paired with _vladPieceRoots / _networkedVladPiecePrefabs.
    private NetworkObject[] _spawnedPieceNetObjs;
    private ParentConstraint[] _spawnedPieceConstraints;
    private Rigidbody[] _spawnedPieceRigidbodies;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[OchoEatingVladCutscene] Duplicate instance detected — destroying self.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        if (_ochoRoot == null && _ochoAnimator != null)
            _ochoRoot = _ochoAnimator.transform;
        if (_ochoGameObjectToDeactivate == null && _ochoAnimator != null)
            _ochoGameObjectToDeactivate = _ochoAnimator.gameObject;

        int pieceCount = _vladPieceRoots != null ? _vladPieceRoots.Length : 0;
        _spawnedPieceNetObjs = new NetworkObject[pieceCount];
        _spawnedPieceConstraints = new ParentConstraint[pieceCount];
        _spawnedPieceRigidbodies = new Rigidbody[pieceCount];
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Arms the cutscene for the current day: Ocho starts his eating loop and begins
    /// watching for a nearby player. SERVER ONLY. Safe to call once per day — repeated
    /// calls are ignored while already armed/running.
    /// Call from Day_03.DayActivated().
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;
        if (_triggered) return;
        _triggered = true;

        if (_ochoGameObjectToDeactivate != null)
            _ochoGameObjectToDeactivate.SetActive(true);

        SpawnNetworkedVladPieces();
        PlayEatingLoopClientRpc();
        _monitorRoutine = StartCoroutine(MonitorProximity());

        Debug.Log("[OchoEatingVladCutscene] Armed — watching for player proximity.", this);
    }

    /// <summary>Resets the cutscene so it can be re-armed (debug/testing only). SERVER ONLY.</summary>
    public void DebugReset()
    {
        if (!IsServer) return;

        if (_monitorRoutine != null)
        {
            StopCoroutine(_monitorRoutine);
            _monitorRoutine = null;
        }

        _triggered = false;
        LocalSetLooking(false);
        DespawnNetworkedVladPieces();

        if (_vladPieceRoots != null)
        {
            foreach (Transform piece in _vladPieceRoots)
            {
                if (piece != null)
                    piece.gameObject.SetActive(true);
            }
        }
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
        // Animation-driven "StopAndLook" trigger is disabled — enabling the FLookAnimator
        // directly (below) reads better than the canned turn animation. LocalPlayStopAndLook
        // still cuts the eating-loop audio; only its Animator.SetTrigger call is skipped now.
        PlayStopAndLookClientRpc();
        SetLookingClientRpc(true);
        yield return new WaitForSeconds(_lookHoldDuration);

        SetLookingClientRpc(false);
        DropVladPiecesAndIdleClientRpc();
        yield return new WaitForSeconds(_postDropDelay);

        PlayJumpSequenceAndVanishClientRpc();
    }

    // ── Client RPCs (also execute locally on the host) ───────────────────────

    [ClientRpc]
    private void PlayEatingLoopClientRpc() => LocalPlayEatingLoop();

    [ClientRpc]
    private void PlayStopAndLookClientRpc() => LocalPlayStopAndLook();

    [ClientRpc]
    private void SetLookingClientRpc(bool value) => LocalSetLooking(value);

    [ClientRpc]
    private void DropVladPiecesAndIdleClientRpc() => LocalDropVladPiecesAndIdle();

    [ClientRpc]
    private void PlayJumpSequenceAndVanishClientRpc() => LocalPlayJumpSequenceAndVanish();

    /// <summary>
    /// Runs on every client right after the server spawns a networked piece double.
    /// Adds/enables a ParentConstraint sourced from the corresponding held visual piece and
    /// hides the double's renderers/colliders/NetworkTransform until <see cref="DropNetworkedVladPieces"/>.
    /// </summary>
    [ClientRpc]
    private void SetupNetworkedPieceClientRpc(NetworkObjectReference pieceRef, int pieceIndex)
    {
        if (!pieceRef.TryGet(out NetworkObject netObj)) return;
        if (_vladPieceRoots == null || pieceIndex < 0 || pieceIndex >= _vladPieceRoots.Length) return;

        Transform target = _vladPieceRoots[pieceIndex];
        if (target == null) return;

        GameObject pieceGO = netObj.gameObject;

        ParentConstraint constraint = pieceGO.GetComponent<ParentConstraint>();
        if (constraint == null)
            constraint = pieceGO.AddComponent<ParentConstraint>();

        constraint.SetSources(new List<ConstraintSource> { new ConstraintSource { sourceTransform = target, weight = 1f } });
        constraint.SetTranslationOffset(0, Vector3.zero);
        constraint.SetRotationOffset(0, Vector3.zero);
        constraint.weight = 1f;
        constraint.constraintActive = true;

        // ParentConstraint alone drives the held position on every client — disable
        // NetworkTransform so it doesn't fight/interpolate against the constraint.
        NetworkTransform nt = pieceGO.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        SetPieceVisible(pieceGO, false);

        _spawnedPieceConstraints[pieceIndex] = constraint;
        _spawnedPieceRigidbodies[pieceIndex] = pieceGO.GetComponent<Rigidbody>();
    }

    // ── Local (client-visual) implementations ────────────────────────────────

    private void LocalPlayEatingLoop()
    {
        if (_audioSource == null || _eatingLoopClip == null) return;
        _audioSource.loop = true;
        _audioSource.clip = _eatingLoopClip;
        _audioSource.Play();
    }

    private void LocalPlayStopAndLook()
    {
        if (_audioSource != null)
        {
            _audioSource.loop = false;
            _audioSource.Stop();
            if (_stopEatingClip != null)
                _audioSource.PlayOneShot(_stopEatingClip);
        }

        // Disabled: the "StopAndLook" Animator trigger is no longer fired — enabling the
        // FLookAnimator directly (see RunCutsceneSequence -> SetLookingClientRpc) looks better
        // than the canned turn animation. Kept commented for easy revert.
        // if (_ochoAnimator != null && !string.IsNullOrEmpty(_stopAndLookTrigger))
        //     _ochoAnimator.SetTrigger(_stopAndLookTrigger);
    }

    private void LocalDropVladPiecesAndIdle()
    {
        DropNetworkedVladPieces();

        if (_ochoAnimator != null && !string.IsNullOrEmpty(_idleTrigger))
            _ochoAnimator.SetTrigger(_idleTrigger);
    }

    private void LocalPlayJumpSequenceAndVanish()
    {
        if (_ochoRoot == null || _jumpWaypoints == null || _jumpWaypoints.Length == 0)
        {
            DeactivateOcho();
            return;
        }

        Sequence seq = DOTween.Sequence();

        foreach (Transform waypoint in _jumpWaypoints)
        {
            if (waypoint == null) continue;

            Vector3 destination = waypoint.position;
            Transform ocho = _ochoRoot;

            seq.Append(ocho.DORotate(FacingEuler(ocho.position, destination), _turnDuration));
            seq.AppendCallback(() =>
            {
                if (_jumpClip != null && _audioSource != null)
                    _audioSource.PlayOneShot(_jumpClip);
            });
            seq.Append(ocho.DOJump(destination, _jumpPower, 1, _jumpDuration, false));

            if (_waypointPause > 0f)
                seq.AppendInterval(_waypointPause);
        }

        seq.AppendInterval(_postJumpDelay);
        seq.AppendCallback(DeactivateOcho);

        seq.Play();
    }

    private void DeactivateOcho()
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

    /// <summary>
    /// SERVER ONLY — instantiates and network-spawns one physics-double per
    /// <see cref="_vladPieceRoots"/> entry, positioned/rotated to match its held visual piece,
    /// then tells every client (<see cref="SetupNetworkedPieceClientRpc"/>) to start tracking
    /// it locally via ParentConstraint.
    /// </summary>
    private void SpawnNetworkedVladPieces()
    {
        if (!IsServer) return;
        if (_vladPieceRoots == null || _networkedVladPiecePrefabs == null) return;

        int count = Mathf.Min(_vladPieceRoots.Length, _networkedVladPiecePrefabs.Length);
        for (int i = 0; i < count; i++)
        {
            Transform target = _vladPieceRoots[i];
            GameObject prefab = _networkedVladPiecePrefabs[i];
            if (target == null || prefab == null) continue;

            GameObject instance = Instantiate(prefab, target.position, target.rotation);
            NetworkObject netObj = instance.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogWarning($"[OchoEatingVladCutscene] Networked piece prefab '{prefab.name}' has no NetworkObject — skipping.", this);
                Destroy(instance);
                continue;
            }

            netObj.Spawn(true);
            _spawnedPieceNetObjs[i] = netObj;

            SetupNetworkedPieceClientRpc(new NetworkObjectReference(netObj), i);
        }
    }

    /// <summary>
    /// Runs on every client (including the drop step being broadcast via ClientRpc elsewhere):
    /// switches off each piece double's ParentConstraint, hides the held visual piece, and
    /// reveals the double. Only the server flips the double's Rigidbody to non-kinematic —
    /// NetworkRigidbody's AutoUpdateKinematicState keeps every other client's copy kinematic
    /// and driven by the server's replicated NetworkTransform.
    /// </summary>
    private void DropNetworkedVladPieces()
    {
        if (_vladPieceRoots == null) return;

        for (int i = 0; i < _vladPieceRoots.Length; i++)
        {
            if (_vladPieceRoots[i] != null)
                _vladPieceRoots[i].gameObject.SetActive(false);

            ParentConstraint constraint = _spawnedPieceConstraints != null && i < _spawnedPieceConstraints.Length
                ? _spawnedPieceConstraints[i]
                : null;
            if (constraint == null) continue;

            constraint.constraintActive = false;
            SetPieceVisible(constraint.gameObject, true);

            NetworkTransform nt = constraint.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = true;

            Rigidbody rb = _spawnedPieceRigidbodies != null && i < _spawnedPieceRigidbodies.Length
                ? _spawnedPieceRigidbodies[i]
                : null;
            if (IsServer && rb != null)
                rb.isKinematic = false;
        }
    }

    /// <summary>SERVER ONLY — despawns any spawned piece doubles (debug/testing only).</summary>
    private void DespawnNetworkedVladPieces()
    {
        if (!IsServer || _spawnedPieceNetObjs == null) return;

        for (int i = 0; i < _spawnedPieceNetObjs.Length; i++)
        {
            NetworkObject netObj = _spawnedPieceNetObjs[i];
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(true);

            _spawnedPieceNetObjs[i] = null;
            _spawnedPieceConstraints[i] = null;
            _spawnedPieceRigidbodies[i] = null;
        }
    }

    private static void SetPieceVisible(GameObject go, bool visible)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            c.enabled = visible;
    }

    private static Vector3 FacingEuler(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
        return Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles;
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_ochoRoot == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_ochoRoot.position, _detectionRadius);

        if (_jumpWaypoints == null) return;

        Gizmos.color = Color.cyan;
        Vector3 prev = _ochoRoot.position;
        foreach (Transform wp in _jumpWaypoints)
        {
            if (wp == null) continue;
            Gizmos.DrawSphere(wp.position, 0.2f);
            Gizmos.DrawLine(prev, wp.position);
            prev = wp.position;
        }
    }
#endif
}
