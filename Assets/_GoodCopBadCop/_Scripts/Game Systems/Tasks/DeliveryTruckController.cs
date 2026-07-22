using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;

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
///      the crate rides along on the roof for the whole trip.
///   3. On arrival, the roof constraint is released and the crate tumbles down to its resting
///      spot on the ground. Once it settles, the server spawns the mail delivery via
///      <see cref="SortMailTask.TriggerTask"/>.
///   4. Idles at _pointB for the remainder of _idleDurationAtDestination.
///   5. Drives back from _pointB to _pointA (drive audio again) — the crate stays behind.
///   6. Deactivates (visual hidden, audio stopped) until the next delivery day.
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
    [Header("References")]
    [Tooltip("Root of the truck's visual model — toggled on/off to 'activate'/'deactivate' the truck.")]
    [SerializeField] private GameObject truckVisual;
    [SerializeField] private AudioSource truckAudioSource;
    [SerializeField] private MachineShake machineShake;

    [Header("Waypoints")]
    [Tooltip("Parked / starting position the truck drives from and returns to.")]
    [SerializeField] private Transform pointA;
    [Tooltip("Delivery position the truck drives to — packages spawn once it arrives here.")]
    [SerializeField] private Transform pointB;

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

    [Header("Timing")]
    [Tooltip("How long the truck idles at pointB before driving back — packages are spawned as soon as it arrives.")]
    [SerializeField] private float idleDurationAtDestination = 8f;

    [Header("Movement")]
    [SerializeField] private float driveToDuration = 6f;
    [SerializeField] private float driveBackDuration = 6f;
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
        if (pointA == null || pointB == null)
        {
            Debug.LogError("[DeliveryTruckController] pointA/pointB not assigned — cannot run delivery sequence.");
            return;
        }

        StartCoroutine(ServerSequence());
    }

    private IEnumerator ServerSequence()
    {
        _sequenceRunning = true;

        ActivateClientRpc();
        BeginDriveClientRpc(toPointB: true);
        yield return new WaitForSeconds(driveRevDelay + driveToDuration);

        // Truck has arrived at pointB — release the crate and let it tumble down onto its resting spot.
        ReleaseCrateClientRpc();
        yield return new WaitForSeconds(crateTumbleDuration);

        // Crate has settled — this is the moment the mail delivery spawns.
        SortMailTask.Instance?.TriggerTask();

        yield return new WaitForSeconds(idleDurationAtDestination);

        BeginDriveClientRpc(toPointB: false);
        yield return new WaitForSeconds(driveRevDelay + driveBackDuration);

        DeactivateClientRpc();

        _sequenceRunning = false;
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
    private void BeginDriveClientRpc(bool toPointB)
    {
        PlayDriveSequence(toPointB);
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
    /// Drives from the current position to pointA/pointB using DOTween, after a rev delay.
    /// Runs identically on every client (called via <see cref="BeginDriveClientRpc"/>).
    /// </summary>
    private void PlayDriveSequence(bool toPointB)
    {
        Transform to = toPointB ? pointB : pointA;
        float duration = toPointB ? driveToDuration : driveBackDuration;

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
