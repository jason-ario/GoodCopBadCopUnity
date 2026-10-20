using System;
using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Rare booth jumpscare: a copy of the player character (Giorgi) with the corrupted face walks up
/// to the window like a normal suspect, says nothing, hands over nothing, stares for a few seconds,
/// then glides straight through the window (smashing the glass) and through the booth while a
/// giggle and a stinger play, and fades out of existence.
///
/// Deliberately NOT a <see cref="SuspectCharacter"/>: he can't be talked to, stamped, shot, given
/// anomalies, or counted as a processed suspect. He is spawned and sequenced by
/// <see cref="GiorgiPhantomVisitController"/>, which splices him into the lineup through
/// <see cref="SuspectController.InterceptNextSuspectSpawn"/> and refunds the slot afterwards.
///
/// Networking: all sequencing is server-only. Position replicates through the root
/// <see cref="Unity.Netcode.Components.NetworkTransform"/>, the walk/idle pose through
/// <see cref="_pose"/>, and the one-shot beats (stinger, giggle, flicker, fade) through ClientRpcs.
/// The glass break goes through <see cref="BreakableGlassController.ForceSmash"/>, which writes
/// the replicated glass state, so every peer (and late joiners) see the window broken.
/// </summary>
public class GiorgiPhantomEncounter : NetworkBehaviour
{
    private enum Pose : byte { Idle = 0, Walking = 1, Phasing = 2 }

    [Header("Visuals")]
    [Tooltip("Animator on the Giorgi model (NPC.controller + Giorgi humanoid avatar). Uses the 'Walking' bool.")]
    [SerializeField] private Animator _animator;

    [Tooltip("Giorgi's normal face material. Any renderer slot using it is swapped for the creepy face on spawn.")]
    [SerializeField] private Material _normalFaceMaterial;

    [Tooltip("The creepy face material swapped in for the normal face.")]
    [SerializeField] private Material _creepyFaceMaterial;

    [Header("Walk-in")]
    [Tooltip("Walk speed to the booth window (units/second). Normal suspects use 1.5.")]
    [SerializeField] private float _walkSpeed = 1.5f;

    [Tooltip("Seconds to turn to the stand position's rotation after arriving (normal suspects use 0.5).")]
    [SerializeField] private float _arrivalTurnDuration = 0.5f;

    [Header("Stare")]
    [Tooltip("Seconds he stands silently at the window (once a player is inside and the shutter is open) before phasing.")]
    [SerializeField] private float _stareDuration = 3f;

    [Tooltip("If true, he waits at the window until a player is inside the booth and the shutter is open, " +
             "exactly like a normal suspect waiting to give their entry line.")]
    [SerializeField] private bool _waitForBoothReady = true;

    [Header("Phase Through")]
    [Tooltip("Glide speed while phasing through the window and booth (units/second).")]
    [SerializeField] private float _phaseSpeed = 15.6f;

    [Tooltip("Total distance (units) he glides along his local +Z from the window before vanishing. " +
             "Must carry him through the whole booth and out the far wall.")]
    [SerializeField] private float _phaseDistance = 14f;

    [Header("Booth Lights")]
    [Tooltip("Flicker the booth lights (BoothFlickeringLightsController) while he phases through.")]
    [SerializeField] private bool _flickerBoothLights = true;

    [Tooltip("Extra seconds the booth lights keep flickering after he has vanished.")]
    [SerializeField] private float _lightFlickerTail = 0.8f;

    [Tooltip("Play the booth's flicker buzz sound with the light flicker.")]
    [SerializeField] private bool _lightFlickerSound = true;

    [Tooltip("Randomly flicker his renderers while phasing, for a glitchy 'not really there' look.")]
    [SerializeField] private bool _flickerWhilePhasing = false;

    [Tooltip("Random interval range (seconds) between flicker toggles.")]
    [SerializeField] private Vector2 _flickerInterval = new Vector2(0.04f, 0.16f);

    [Tooltip("Chance (0-1) each flicker tick hides him for one interval.")]
    [Range(0f, 1f)]
    [SerializeField] private float _flickerHideChance = 0.3f;

    [Header("Audio")]
    [Tooltip("Spatial AudioSource on the phantom used for the giggle (follows him through the booth).")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Creepy giggle played as he phases through.")]
    [SerializeField] private AudioClip _giggleClip;

    [Tooltip("2D stinger played the instant he starts phasing.")]
    [SerializeField] private AudioClip _stingerClip;

    [SerializeField] private float _stingerVolume = 1f;

    [Tooltip("Max seconds the (hidden) object is kept alive after vanishing so the giggle can finish.")]
    [SerializeField] private float _maxGiggleTail = 4f;

    private readonly NetworkVariable<byte> _pose = new NetworkVariable<byte>((byte)Pose.Idle);

    private static readonly int WalkingHash = Animator.StringToHash("Walking");

    private Renderer[] _renderers;
    private Coroutine _flickerRoutine;
    private Action _onFinished;
    private bool _started;
    private bool _finished;

    /// <summary>True while any phantom is alive (server and clients). Used to avoid double-arming.</summary>
    public static bool IsActive { get; private set; }

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        DisableNavigation();
        ApplyCreepyFace();
    }

    /// <summary>
    /// He's moved purely by straight-line transform writes. Make sure nothing else can steer or
    /// snap him: no NavMeshAgent/Obstacle, no root motion, no physics pushing him around.
    /// </summary>
    private void DisableNavigation()
    {
        foreach (NavMeshAgent agent in GetComponentsInChildren<NavMeshAgent>(true))
            agent.enabled = false;
        foreach (NavMeshObstacle obstacle in GetComponentsInChildren<NavMeshObstacle>(true))
            obstacle.enabled = false;
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
            body.isKinematic = true;
        if (_animator != null) _animator.applyRootMotion = false;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        IsActive = true;
        _pose.OnValueChanged += HandlePoseChanged;
        ApplyPose((Pose)_pose.Value);
    }

    public override void OnNetworkDespawn()
    {
        _pose.OnValueChanged -= HandlePoseChanged;
        IsActive = false;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only. Starts the full sequence. <paramref name="onFinished"/> runs on the server right
    /// before the phantom despawns itself (use it to refund the lineup slot and re-arm the switch).
    /// </summary>
    public void BeginEncounter(Transform standPoint, Transform boothInteriorPoint, Action onFinished)
    {
        if (!IsServer || _started) return;
        _started = true;
        _onFinished = onFinished;
        StartCoroutine(EncounterSequence(standPoint, boothInteriorPoint));
    }

    // ── Server sequence ─────────────────────────────────────────────────────

    private IEnumerator EncounterSequence(Transform standPoint, Transform boothInteriorPoint)
    {
        if (standPoint == null)
        {
            Debug.LogError("[GiorgiPhantom] No stand point — aborting encounter.");
            Finish();
            yield break;
        }

        // 1. Walk up like a normal suspect (straight DOTween leg, same as SuspectCharacter.WalkTo).
        Vector3 destination = standPoint.position;
        Vector3 dir = destination - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);

        SetPose(Pose.Walking);
        float walkDuration = FlatDistance(transform.position, destination) / Mathf.Max(0.01f, _walkSpeed);
        yield return transform.DOMove(destination, walkDuration).SetEase(Ease.Linear).SetLink(gameObject).WaitForCompletion();

        SetPose(Pose.Idle);
        yield return transform.DORotateQuaternion(standPoint.rotation, _arrivalTurnDuration).SetLink(gameObject).WaitForCompletion();

        // 2. Wait for an audience, exactly like a suspect waiting to say their entry line.
        if (_waitForBoothReady)
        {
            SuspectController controller = SuspectController.Instance;
            while (controller != null && !controller.IsBoothReadyForSuspect())
                yield return new WaitForSeconds(0.5f);
        }

        // 3. Silent stare. No line, no paperwork.
        yield return new WaitForSeconds(_stareDuration);

        // 4. Phase straight through the window and the booth.
        yield return PhaseThrough(boothInteriorPoint);

        // 5. Let the giggle tail out while hidden, then clean up.
        float tail = _giggleClip != null ? Mathf.Min(_giggleClip.length, _maxGiggleTail) : 0f;
        if (tail > 0f) yield return new WaitForSeconds(tail);

        Finish();
    }

    private IEnumerator PhaseThrough(Transform boothInteriorPoint)
    {
        Vector3 start = transform.position;

        // Strictly linear: keep his current facing (the stand point's rotation) and slide along his
        // own local +Z for a fixed distance that carries him out the far side of the booth.
        // No steering toward points, no NavMesh, no slowdown.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        float minDistance = boothInteriorPoint != null
            ? Vector3.Dot(boothInteriorPoint.position - start, forward) + 1f
            : 1f;
        float totalDistance = Mathf.Max(_phaseDistance, minDistance);
        Vector3 end = start + forward * totalDistance;
        float duration = totalDistance / Mathf.Max(0.01f, _phaseSpeed);

        // Where along the path the glass sits, so it shatters the moment he reaches it.
        BreakableGlassController glass = BreakableGlassController.Instance;
        float glassDistance = glass != null
            ? Mathf.Clamp(Vector3.Dot(glass.transform.position - start, forward), 0f, totalDistance)
            : 0f;
        bool glassBroken = glass == null || glass.IsSmashed;

        SetPose(Pose.Phasing);
        BeginPhaseClientRpc(duration);

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, end, p);

            if (!glassBroken && p * totalDistance >= glassDistance)
            {
                glassBroken = true;
                glass.ForceSmash();
            }

            yield return null;
        }

        transform.position = end;
        if (!glassBroken && glass != null) glass.ForceSmash();

        // Instant disappearance on the far side — no fade, no blink.
        VanishClientRpc();
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;

        try { _onFinished?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
        _onFinished = null;

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    private void SetPose(Pose pose)
    {
        _pose.Value = (byte)pose;
        ApplyPose(pose); // OnValueChanged doesn't fire on the server for its own write in every NGO version.
    }

    // ── Client presentation ─────────────────────────────────────────────────

    private void HandlePoseChanged(byte previous, byte current) => ApplyPose((Pose)current);

    private void ApplyPose(Pose pose)
    {
        if (_animator == null) return;
        // Only the walk-in uses the walk cycle; he stays in idle while phasing through the booth.
        _animator.SetBool(WalkingHash, pose == Pose.Walking);
        _animator.speed = 1f;
    }

    [ClientRpc]
    private void BeginPhaseClientRpc(float phaseDuration)
    {
        if (_stingerClip != null)
            SFXController.Instance?.Play(_stingerClip, _stingerVolume);

        if (_flickerBoothLights)
        {
            BoothFlickeringLightsController lights = FindFirstObjectByType<BoothFlickeringLightsController>();
            if (lights != null)
                lights.PlayScriptedBurst(phaseDuration + _lightFlickerTail, _lightFlickerSound);
        }

        if (_giggleClip != null && _audioSource != null)
        {
            _audioSource.clip = _giggleClip;
            _audioSource.Play();
        }

        if (_flickerWhilePhasing)
        {
            if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
            _flickerRoutine = StartCoroutine(FlickerRoutine());
        }
    }

    [ClientRpc]
    private void VanishClientRpc()
    {
        if (_flickerRoutine != null)
        {
            StopCoroutine(_flickerRoutine);
            _flickerRoutine = null;
        }
        SetVisible(false);
    }

    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            bool hide = UnityEngine.Random.value < _flickerHideChance;
            SetVisible(!hide);
            yield return new WaitForSeconds(UnityEngine.Random.Range(_flickerInterval.x, _flickerInterval.y));
        }
    }

    private void SetVisible(bool visible)
    {
        if (_renderers == null) return;
        foreach (Renderer r in _renderers)
            if (r != null) r.enabled = visible;
    }

    /// <summary>
    /// Local, every peer: swaps every renderer slot using the normal face material for the creepy
    /// one. Done at runtime (in addition to whatever is baked into the prefab) so the swap survives
    /// model reimports or the prefab being rebuilt from the FBX.
    /// </summary>
    private void ApplyCreepyFace()
    {
        if (_creepyFaceMaterial == null || _normalFaceMaterial == null || _renderers == null) return;

        foreach (Renderer r in _renderers)
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == _normalFaceMaterial)
                {
                    mats[i] = _creepyFaceMaterial;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
