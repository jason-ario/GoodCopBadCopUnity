using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Serialization;

/// <summary>
/// Networked delivery-truck cutscene, driven the same way as <see cref="BusCutsceneController"/>:
/// the server runs the authoritative timing and calls <see cref="SortMailTask.TriggerTask"/> at
/// the right moment, while every client (including the host) independently plays back the same
/// scripted movement/audio sequence via a ClientRpc so it renders identically for everyone.
///
/// Sequence, once <see cref="BeginDeliverySequence"/> is called on the server:
///   1. Truck activates at _pointA (engine idle audio, visual shown) and the delivery crate
///      appears mounted on its roof via a <see cref="ParentConstraint"/>.
///   2. Drives from _pointA to _pointB (drive audio, with a rev delay before it starts moving) —
///      the crate rides along on the roof for the whole trip. _pointB sits in front of the
///      checkpoint gate.
///   3. On arrival at _pointB, the truck stops and waits — a "shipment is waiting at the gate"
///      alert is shown (see <see cref="SortMailTask.NotifyShipmentWaitingAtGate"/>) and the truck
///      idles until a player opens the gate via <see cref="CheckpointGateController"/> (e.g. by
///      pressing the gate button). Once open, it waits _gateOpenWaitDuration more for the open
///      animation to finish before continuing.
///   4. Drives from _pointB through the (now open) gate to _pointC — the crate still rides along.
///   5. On arrival at _pointC, the roof constraint is released and the crate tumbles down to its
///      resting spot on the ground. Once it settles, the server spawns the mail delivery via
///      <see cref="SortMailTask.TriggerTask"/>.
///   6. Idles at _pointC for the remainder of _idleDurationAtDestination.
///   7. Drives backwards from _pointC all the way to _pointA (drive audio again, same facing
///      direction — a reverse, not a turn-around) — the crate stays behind. Shortly after the
///      truck passes back through the checkpoint gate on this leg, the gate automatically closes
///      (_gateCloseDelayAfterPassing after passing it).
///   8. Deactivates (visual hidden, audio stopped) until the next delivery day.
///
/// Both the truck visual and the delivery crate start inactive/hidden by default and are only
/// switched on for the duration of the sequence, so they cost nothing while idle.
///
/// Requires a NetworkObject component on this GameObject (place as a scene object, never
/// despawned — it is reused every delivery day).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class DeliveryTruckController : NetworkBehaviour
{
    private enum DriveLeg
    {
        ToPointB,
        ToPointC,
        Back
    }

    [Header("References")]
    [Tooltip("Root of the truck's visual model — toggled on/off to 'activate'/'deactivate' the truck.")]
    [SerializeField] private GameObject truckVisual;
    [SerializeField] private AudioSource truckAudioSource;
    [SerializeField] private MachineShake machineShake;
    [Tooltip("The checkpoint gate. The truck waits at pointB until a player opens it (see CheckpointGateController), then closes it shortly after passing back through on the return leg.")]
    [SerializeField] private CheckpointGateController checkpointGate;

    [Header("Waypoints")]
    [Tooltip("Parked / starting position the truck drives from and returns to.")]
    [SerializeField] private Transform pointA;
    [Tooltip("Stop position just before the checkpoint gate — the truck pauses here while the gate opens.")]
    [SerializeField] private Transform pointB;
    [Tooltip("Final delivery position beyond the checkpoint gate — the crate is dropped once the truck arrives here.")]
    [SerializeField] private Transform pointC;

    [Header("Delivery Crate")]
    [Tooltip("The crate that rides on the truck's roof until arrival, then tumbles down to its resting spot. Starts hidden and only appears once the truck activates.")]
    [SerializeField] private Transform deliveryCrate;
    [Tooltip("ParentConstraint on the crate, sourced from crateMountPoint, used to pin it to the roof while driving.")]
    [SerializeField] private ParentConstraint crateParentConstraint;
    [Tooltip("Transform on the truck's roof the crate is mounted to while riding along.")]
    [SerializeField] private Transform crateMountPoint;
    [Tooltip("How long the crate takes to tumble from the roof down to its resting spot once released.")]
    [SerializeField] private float crateTumbleDuration = 1.2f;
    [Tooltip("How high the crate arcs above a straight line while tumbling down.")]
    [SerializeField] private float crateTumbleArcHeight = 0.5f;
    [Tooltip("Total degrees the crate spins around a random axis while tumbling down.")]
    [SerializeField] private float crateTumbleSpinDegrees = 540f;

    [Header("Audio")]
    [Tooltip("Looping idle/engine-running clip played while parked at either point.")]
    [SerializeField] private AudioClip idleClip;
    [Tooltip("Looping driving clip played while the truck is moving between points.")]
    [SerializeField] private AudioClip driveClip;
    [Tooltip("Delay after the drive clip starts before the truck actually begins moving, so the engine can rev first.")]
    [SerializeField] private float driveRevDelay = 1.5f;
    [Tooltip("One-shot sound played when the crate lands after tumbling off the truck's roof.")]
    [SerializeField] private AudioClip crateDropSfxClip;
    [Tooltip("Volume for crateDropSfxClip.")]
    [SerializeField] private float crateDropSfxVolume = 1f;

    [Header("Timing")]
    [Tooltip("How long the truck idles at pointC after dropping the crate before driving back — packages are spawned as soon as the crate settles.")]
    [FormerlySerializedAs("idleDurationAtDestination")]
    [SerializeField] private float idleDurationAtDestination = 8f;
    [Tooltip("How long the truck waits at pointB after the gate opens for the open animation to finish before continuing to pointC.")]
    [SerializeField] private float gateOpenWaitDuration = 3f;
    [Tooltip("How long after the truck passes back through the checkpoint gate (on the pointC-to-pointA return leg) before the gate closes.")]
    [SerializeField] private float gateCloseDelayAfterPassing = 1f;

    [Header("Movement")]
    [Tooltip("Duration of the pointA -> pointB leg.")]
    [FormerlySerializedAs("driveToDuration")]
    [SerializeField] private float driveToPointBDuration = 6f;
    [Tooltip("Duration of the pointB -> pointC leg (through the checkpoint gate).")]
    [SerializeField] private float driveToPointCDuration = 6f;
    [Tooltip("Duration of the pointC -> pointA return leg.")]
    [SerializeField] private float driveBackDuration = 10f;
    [Tooltip("Shapes acceleration/deceleration over each leg of the drive. X = normalised time (0-1), Y = position lerp fraction (0-1).")]
    [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Peak MachineShake.positionStrength reached while driving at full speed.")]
    [SerializeField] private float peakShakeStrength = 0.05f;

    private bool _sequenceRunning;
    private Vector3 _crateRestPosition;
    private Quaternion _crateRestRotation;
    private Sequence _driveSequence;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Start parked and hidden — the truck only appears once a delivery begins.
        if (pointA != null)
        {
            transform.position = pointA.position;
            transform.rotation = pointA.rotation;
        }
        SetVisualActive(false);

        // Remember where the crate belongs once it lands, then hide it until the truck
        // spawns it on the roof — no reason to render/collide with it while idle.
        if (deliveryCrate != null)
        {
            _crateRestPosition = deliveryCrate.position;
            _crateRestRotation = deliveryCrate.rotation;
            deliveryCrate.gameObject.SetActive(false);
        }
        if (crateParentConstraint != null)
            crateParentConstraint.constraintActive = false;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    // -------------------------------------------------------------------------
    // Server
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. Starts the full activate → drive-to-B → spawn packages → idle → drive-back →
    /// deactivate sequence. Safe to call even if a previous sequence is still finishing — the call
    /// is ignored while one is already running.
    /// </summary>
    public void BeginDeliverySequence()
    {
        if (!IsServer) return;
        if (_sequenceRunning)
        {
            Debug.LogWarning("[DeliveryTruckController] BeginDeliverySequence called while a sequence is already running — ignored.");
            return;
        }
        if (pointA == null || pointB == null || pointC == null)
        {
            Debug.LogError("[DeliveryTruckController] pointA/pointB/pointC not assigned — cannot run delivery sequence.");
            return;
        }
        if (checkpointGate == null)
        {
            Debug.LogWarning("[DeliveryTruckController] checkpointGate not assigned — the truck will skip the gate wait/close steps.");
        }

        StartCoroutine(ServerSequence());
    }

    private IEnumerator ServerSequence()
    {
        _sequenceRunning = true;

        ActivateClientRpc();
        BeginDriveClientRpc(DriveLeg.ToPointB);
        yield return new WaitForSeconds(driveRevDelay + driveToPointBDuration);

        // Truck has stopped at pointB, right in front of the checkpoint gate — alert players
        // that a shipment is waiting, then wait for a player to actually open the gate (e.g. by
        // pressing the gate button) before driving through.
        if (checkpointGate != null)
        {
            SortMailTask.Instance?.NotifyShipmentWaitingAtGate();

            if (!checkpointGate.IsOpen)
                yield return StartCoroutine(WaitForGateOpen());

            yield return new WaitForSeconds(gateOpenWaitDuration);
        }

        BeginDriveClientRpc(DriveLeg.ToPointC);
        yield return new WaitForSeconds(driveRevDelay + driveToPointCDuration);

        // Truck has arrived at pointC — release the crate and let it tumble down onto its resting spot.
        ReleaseCrateClientRpc();
        yield return new WaitForSeconds(crateTumbleDuration);

        // Crate has settled — this is the moment the mail delivery spawns.
        SortMailTask.Instance?.TriggerTask();

        yield return new WaitForSeconds(idleDurationAtDestination);

        BeginDriveClientRpc(DriveLeg.Back);
        if (checkpointGate != null)
            StartCoroutine(CloseGateAfterPassing());

        yield return new WaitForSeconds(driveRevDelay + driveBackDuration);

        DeactivateClientRpc();

        _sequenceRunning = false;
    }

    /// <summary>
    /// Server-only. Waits until a player opens <see cref="checkpointGate"/>.
    /// </summary>
    private IEnumerator WaitForGateOpen()
    {
        while (checkpointGate != null && !checkpointGate.IsOpen)
            yield return null;
    }

    /// <summary>
    /// Server-only. Waits until the truck has driven far enough along the pointC -> pointA leg to
    /// have passed the checkpoint gate, then waits <see cref="gateCloseDelayAfterPassing"/> more
    /// before closing it. Timing is derived from <see cref="speedCurve"/> so it lines up with the
    /// actual DOTween movement played back on clients.
    /// </summary>
    private IEnumerator CloseGateAfterPassing()
    {
        if (pointA == null || pointC == null || checkpointGate == null) yield break;

        float fraction = GetFractionAlongLine(pointC.position, pointA.position, checkpointGate.transform.position);
        float normalizedTimeAtGate = InverseEvaluateCurve(speedCurve, fraction);
        float timeUntilPass = driveRevDelay + normalizedTimeAtGate * driveBackDuration;

        yield return new WaitForSeconds(timeUntilPass);
        yield return new WaitForSeconds(gateCloseDelayAfterPassing);

        checkpointGate.RequestClose();
    }

    // -------------------------------------------------------------------------
    // Clients
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void ActivateClientRpc()
    {
        if (pointA != null)
        {
            transform.position = pointA.position;
            transform.rotation = pointA.rotation;
        }
        SetVisualActive(true);
        PlayLoopingClip(idleClip);
        MountCrateOnRoof();
    }

    [ClientRpc]
    private void BeginDriveClientRpc(DriveLeg leg)
    {
        PlayDriveSequence(leg);
    }

    [ClientRpc]
    private void ReleaseCrateClientRpc()
    {
        StartCoroutine(TumbleCrateDown());
    }

    [ClientRpc]
    private void DeactivateClientRpc()
    {
        _driveSequence?.Kill();
        if (truckAudioSource != null)
            truckAudioSource.Stop();
        SetVisualActive(false);
    }

    /// <summary>
    /// Drives from the current position to the target waypoint for the given leg using DOTween,
    /// after a rev delay. Runs identically on every client (called via <see cref="BeginDriveClientRpc"/>).
    /// </summary>
    private void PlayDriveSequence(DriveLeg leg)
    {
        Transform to;
        float duration;
        switch (leg)
        {
            case DriveLeg.ToPointB:
                to = pointB;
                duration = driveToPointBDuration;
                break;
            case DriveLeg.ToPointC:
                to = pointC;
                duration = driveToPointCDuration;
                break;
            default:
                to = pointA;
                duration = driveBackDuration;
                break;
        }

        if (to == null) return;

        PlayLoopingClip(driveClip);

        float baseShakeStrength = machineShake != null ? machineShake.positionStrength : 0f;

        _driveSequence?.Kill();
        _driveSequence = DOTween.Sequence();
        _driveSequence.AppendInterval(driveRevDelay);
        _driveSequence.Append(transform.DOMove(to.position, duration).SetEase(speedCurve));
        _driveSequence.Join(transform.DORotateQuaternion(to.rotation, duration).SetEase(speedCurve));

        if (machineShake != null)
        {
            _driveSequence.Join(
                DOTween.To(() => machineShake.positionStrength, v => machineShake.positionStrength = v, peakShakeStrength, duration)
                    .SetEase(speedCurve));
        }

        _driveSequence.OnComplete(() =>
        {
            if (machineShake != null)
                machineShake.positionStrength = baseShakeStrength;

            // Back to idle audio once parked at either end.
            PlayLoopingClip(idleClip);
        });
    }

    /// <summary>
    /// Spawns the crate on the truck's roof and pins it there with a zero-offset ParentConstraint
    /// so it rides along for the whole drive to pointB.
    /// </summary>
    private void MountCrateOnRoof()
    {
        if (deliveryCrate == null || crateMountPoint == null) return;

        deliveryCrate.SetPositionAndRotation(crateMountPoint.position, crateMountPoint.rotation);
        deliveryCrate.gameObject.SetActive(true);

        if (crateParentConstraint == null) return;

        var source = new ConstraintSource { sourceTransform = crateMountPoint, weight = 1f };
        if (crateParentConstraint.sourceCount == 0)
            crateParentConstraint.AddSource(source);
        else
            crateParentConstraint.SetSource(0, source);

        crateParentConstraint.translationAtRest = Vector3.zero;
        crateParentConstraint.rotationAtRest = Vector3.zero;
        crateParentConstraint.SetTranslationOffset(0, Vector3.zero);
        crateParentConstraint.SetRotationOffset(0, Vector3.zero);
        crateParentConstraint.weight = 1f;
        crateParentConstraint.constraintActive = true;
    }

    /// <summary>
    /// Releases the crate from the roof constraint and tumbles it down to its resting spot on
    /// the ground, spinning around a random axis along the way.
    /// </summary>
    private IEnumerator TumbleCrateDown()
    {
        if (deliveryCrate == null) yield break;

        if (crateParentConstraint != null)
            crateParentConstraint.constraintActive = false;

        Vector3 startPos = deliveryCrate.position;
        Quaternion startRot = deliveryCrate.rotation;
        Vector3 spinAxis = Random.onUnitSphere;

        float elapsed = 0f;
        while (elapsed < crateTumbleDuration)
        {
            float t = elapsed / crateTumbleDuration;
            float arc = Mathf.Sin(t * Mathf.PI) * crateTumbleArcHeight;

            deliveryCrate.position = Vector3.Lerp(startPos, _crateRestPosition, t) + Vector3.up * arc;
            deliveryCrate.rotation = Quaternion.Slerp(startRot, _crateRestRotation, t) * Quaternion.AngleAxis(crateTumbleSpinDegrees * t, spinAxis);

            elapsed += Time.deltaTime;
            yield return null;
        }

        deliveryCrate.SetPositionAndRotation(_crateRestPosition, _crateRestRotation);

        if (crateDropSfxClip != null)
            AudioSource.PlayClipAtPoint(crateDropSfxClip, _crateRestPosition, crateDropSfxVolume);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetVisualActive(bool active)
    {
        if (truckVisual != null)
            truckVisual.SetActive(active);
    }

    private void PlayLoopingClip(AudioClip clip)
    {
        if (truckAudioSource == null || clip == null) return;

        truckAudioSource.loop = true;
        truckAudioSource.clip = clip;
        truckAudioSource.Play();
    }

    /// <summary>
    /// Returns how far along the line from <paramref name="from"/> to <paramref name="to"/> the
    /// closest point to <paramref name="point"/> lies, as a 0-1 fraction (clamped).
    /// </summary>
    private static float GetFractionAlongLine(Vector3 from, Vector3 to, Vector3 point)
    {
        Vector3 line = to - from;
        float sqrLength = line.sqrMagnitude;
        if (sqrLength < 0.0001f) return 0f;

        float t = Vector3.Dot(point - from, line) / sqrLength;
        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// Numerically inverts a monotonically increasing 0-1 AnimationCurve: given a target output
    /// value, returns the normalised time (0-1) at which the curve reaches it. Used to line up
    /// real-world position fractions (e.g. "where the gate sits along the drive") with the
    /// eased/normalised time used by <see cref="speedCurve"/>.
    /// </summary>
    private static float InverseEvaluateCurve(AnimationCurve curve, float targetValue)
    {
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 24; i++)
        {
            float mid = (low + high) * 0.5f;
            if (curve.Evaluate(mid) < targetValue)
                low = mid;
            else
                high = mid;
        }
        return (low + high) * 0.5f;
    }

    // -------------------------------------------------------------------------
    // Editor helpers
    // -------------------------------------------------------------------------

    private void Reset()
    {
        speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        var truck = transform.Find("Truck") ?? transform.Find("Niva");
        if (truck == null) return;

        truckVisual      = truck.gameObject;
        truckAudioSource = truck.GetComponent<AudioSource>();
        machineShake      = truck.GetComponent<MachineShake>();
        crateMountPoint   = truck.Find("Crate roof spawn pos");
    }
}
