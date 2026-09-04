using System.Collections;
using UnityEngine;

// All PickableObject, Interactable, and NetworkBehaviour base types are in the project's global
// using declarations, so no additional using directives are required for those namespaces.

/// <summary>
/// A mop the player can pick up and use to scrub graffiti off checkpoint walls.
///
/// Hold LMB while holding the Mop to enter the "UsingTool" animation state and begin
/// scrubbing. An overlap CAPSULE running the full length of the mop (derived from
/// <see cref="_scrubBounds"/>, the mop's own box collider) detects any
/// <see cref="GraffitiInteractable"/> within <see cref="_scrubRadius"/> of the shaft; while
/// overlap is maintained the graffiti's progress advances on the server until it is fully removed.
///
/// The capsule — rather than a sphere at the pivot — is what makes floor mopping work. The mop's
/// pivot sits on the mop-head end and the bounds box runs from there up the handle, so a sphere at
/// the pivot only ever covered a bubble around the head and nothing past it. The capsule spans the
/// whole mop and, more importantly, pushes <see cref="_scrubHeadReach"/> further out past the head
/// so a floor surface stays in range when the player looks down and the head is angled away.
///
/// Prefab requirements:
///   - NetworkObject
///   - NetworkTransform
///   - HighlightEffect  (required by Interactable base)
///   - ParentConstraint (required by PickableObject)
///   - Collider on the Interactable layer
///   - <see cref="PickableItemData"/> ScriptableObject assigned in the "Item Data" field
///   - <see cref="_graffitiLayerMask"/> set to the Interactable layer in the Inspector
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
public class Mop : PickableObject
{
    private const string UsingToolAnimBool = "UsingTool";

    [Header("Scrub Detection")]
    [Tooltip("Radius of the overlap capsule swept along the mop's length to detect graffiti.")]
    [SerializeField] private float _scrubRadius = 0.6f;

    [Tooltip("Box collider whose longest axis defines the mop's length. The scrub capsule runs " +
             "end to end along that axis, so the mop head reaches the floor when looking down. " +
             "Leave empty to auto-use the BoxCollider on this GameObject.")]
    [SerializeField] private BoxCollider _scrubBounds;

    [Tooltip("Extra reach added past the MOP HEAD end (the pivot end), in metres. This is the " +
             "knob to turn if the mop still feels short when mopping the floor.")]
    [SerializeField] private float _scrubHeadReach = 0.35f;

    [Tooltip("Extra reach added past the HANDLE end. Keep at 0 — the handle end points back at " +
             "the player, so padding it only lets you scrub things behind you.")]
    [SerializeField] private float _scrubHandleReach = 0f;

    [Tooltip("Layer mask for the graffiti collider. Must include the Interactable layer.")]
    [SerializeField] private LayerMask _graffitiLayerMask;

    [Header("VFX")]
    [Tooltip("Particle system played while the mop is in use and touching any surface. " +
             "Repositioned each frame to the closest point on the contacted collider.")]
    [SerializeField] private ParticleSystem _scrubParticles;

    [Tooltip("Layers the mop can trigger scrub particles against (walls, floors, props, etc.). " +
             "Keep this broader than _graffitiLayerMask.")]
    [SerializeField] private LayerMask _surfaceLayerMask;

    [Header("Audio")]
    [Tooltip("Looping AudioSource played in sync with the scrub particles. " +
             "Set the AudioSource clip, loop = true, and Play On Awake = false in the Inspector.")]
    [SerializeField] private AudioSource _scrubAudio;

    private Coroutine _scrubRoutine;
    private GraffitiInteractable _activeGraffiti;

    /// <summary>Cached fallback for <see cref="_scrubBounds"/> when it is not assigned.</summary>
    private BoxCollider _autoScrubBounds;
    private bool _autoScrubBoundsResolved;

    // ── PickableObject overrides ───────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerPickupController"/> when the owner presses LMB.
    /// Activates the UsingTool animation and starts the overlap-sphere scrub loop.
    /// Only runs on the owning client.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();

        playerPickupController?.PlayerAnimationController.SetAnimBool(UsingToolAnimBool, true);
        _scrubRoutine = StartCoroutine(ScrubRoutine());
    }

    /// <summary>
    /// Called when the owner releases LMB or drops the mop while scrubbing.
    /// Deactivates the animation and stops any active scrub contribution.
    /// </summary>
    public override void OnStopUse()
    {
        base.OnStopUse();

        playerPickupController?.PlayerAnimationController.SetAnimBool(UsingToolAnimBool, false);

        if (_scrubRoutine != null)
        {
            StopCoroutine(_scrubRoutine);
            _scrubRoutine = null;
        }

        _scrubParticles?.Stop();
        _scrubAudio?.Stop();

        // Notify the graffiti that this mop is no longer contributing.
        NotifyStopScrubbing();
    }

    // ── Scrub loop ─────────────────────────────────────────────────────────────

    private IEnumerator ScrubRoutine()
    {
        while (isUsing)
        {
            Collider hitCollider;
            GraffitiInteractable found = FindGraffitiInRange(out hitCollider);

            // Unity's == operator treats destroyed MonoBehaviours as null, so comparing
            // a destroyed _activeGraffiti against a null found will evaluate correctly.
            if (found != _activeGraffiti)
            {
                // Left the range of the previous graffiti or switched to a different one.
                NotifyStopScrubbing();

                _activeGraffiti = found;

                if (_activeGraffiti != null)
                    _activeGraffiti.StartScrubServerRpc();
            }

            // Position particles at the closest surface point — graffiti takes priority,
            // but any surface collider in range will do.
            Collider particleCollider = hitCollider ?? FindSurfaceInRange();
            if (particleCollider != null)
            {
                if (_scrubParticles != null)
                {
                    // Origin is the point on the mop shaft nearest the contacted surface, so the
                    // splash lands under the mop head instead of at the pivot in the player's hand.
                    Vector3 origin       = GetShaftContactOrigin(particleCollider);
                    Vector3 closestPoint = particleCollider.ClosestPoint(origin);
                    Vector3 toSurface    = closestPoint - origin;
                    float   dist         = toSurface.magnitude;

                    // Raycast from the shaft toward the surface to get the real normal.
                    // This correctly handles angled contact (e.g. mopping the floor while
                    // holding the handle at an angle), unlike the mop-to-surface approximation.
                    Vector3 hitPoint      = closestPoint;
                    Vector3 surfaceNormal = dist > 0.001f ? -toSurface.normalized : Vector3.up;
                    if (dist > 0.001f)
                    {
                        RaycastHit hit;
                        if (Physics.Raycast(origin, toSurface.normalized,
                                            out hit, dist + 0.05f,
                                            _graffitiLayerMask | _surfaceLayerMask))
                        {
                            hitPoint      = hit.point;
                            surfaceNormal = hit.normal;
                        }
                    }

                    _scrubParticles.transform.position = hitPoint;
                    if (surfaceNormal != Vector3.zero)
                        _scrubParticles.transform.rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
                    if (!_scrubParticles.isPlaying)
                    {
                        _scrubParticles.Play();
                        if (_scrubAudio != null && !_scrubAudio.isPlaying)
                            _scrubAudio.Play();
                    }
                }
            }
            else if (_scrubParticles != null && _scrubParticles.isPlaying)
            {
                _scrubParticles.Stop();
                _scrubAudio?.Stop();
            }

            yield return null;
        }

        // Clean up after the loop exits (isUsing became false).
        _scrubParticles?.Stop();
        _scrubAudio?.Stop();
        NotifyStopScrubbing();
        _scrubRoutine = null;
    }

    /// <summary>
    /// Sends StopScrub to the current active graffiti and clears the reference.
    /// Null-safe: does nothing if no graffiti is active or if it has been destroyed.
    /// </summary>
    private void NotifyStopScrubbing()
    {
        if (_activeGraffiti != null)
        {
            _activeGraffiti.StopScrubServerRpc();
            _activeGraffiti = null;
        }
    }

    // ── Detection ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the world-space segment the scrub capsule is swept along: end to end of the mop,
    /// extended past the head by <see cref="_scrubHeadReach"/> and past the handle by
    /// <see cref="_scrubHandleReach"/>. Falls back to a zero-length segment at the pivot (i.e. the
    /// old sphere behaviour) when no bounds collider can be found.
    ///
    /// <paramref name="pointA"/> is always the MOP HEAD end. The head is identified as whichever
    /// end of the bounds box is nearer this transform's pivot, since the mop's pivot sits on the
    /// head — that keeps the directional reach correct no matter how the mop is rotated or which
    /// local axis the model happens to run along.
    /// </summary>
    private void GetScrubSegment(out Vector3 pointA, out Vector3 pointB)
    {
        BoxCollider bounds = ResolveScrubBounds();

        if (bounds == null)
        {
            pointA = pointB = transform.position;
            return;
        }

        Vector3 size   = bounds.size;
        Vector3 centre = bounds.center;

        // Longest local axis of the box = the mop shaft.
        Vector3 localAxis;
        float   localLength;
        if (size.y >= size.x && size.y >= size.z)      { localAxis = Vector3.up;      localLength = size.y; }
        else if (size.x >= size.z)                     { localAxis = Vector3.right;   localLength = size.x; }
        else                                           { localAxis = Vector3.forward; localLength = size.z; }

        Transform t = bounds.transform;
        Vector3 end0 = t.TransformPoint(centre - localAxis * (localLength * 0.5f));
        Vector3 end1 = t.TransformPoint(centre + localAxis * (localLength * 0.5f));

        // The end closest to the pivot is the mop head.
        Vector3 pivot = transform.position;
        bool zeroIsHead = (end0 - pivot).sqrMagnitude <= (end1 - pivot).sqrMagnitude;
        pointA = zeroIsHead ? end0 : end1;   // head
        pointB = zeroIsHead ? end1 : end0;   // handle

        Vector3 delta = pointB - pointA;
        if (delta.sqrMagnitude > 0.000001f)
        {
            Vector3 handleDir = delta.normalized;
            pointA -= handleDir * _scrubHeadReach;
            pointB += handleDir * _scrubHandleReach;
        }
    }

    private BoxCollider ResolveScrubBounds()
    {
        if (_scrubBounds != null) return _scrubBounds;

        if (!_autoScrubBoundsResolved)
        {
            _autoScrubBoundsResolved = true;
            _autoScrubBounds = GetComponent<BoxCollider>();
        }

        return _autoScrubBounds;
    }

    /// <summary>
    /// Overlaps the scrub capsule against <paramref name="layerMask"/>. Degrades to a sphere
    /// overlap when the segment has no length.
    /// </summary>
    private Collider[] OverlapScrubVolume(LayerMask layerMask, out Vector3 pointA, out Vector3 pointB)
    {
        GetScrubSegment(out pointA, out pointB);

        return (pointB - pointA).sqrMagnitude > 0.000001f
            ? Physics.OverlapCapsule(pointA, pointB, _scrubRadius, layerMask)
            : Physics.OverlapSphere(pointA, _scrubRadius, layerMask);
    }

    /// <summary>Closest point on the scrub segment to <paramref name="target"/>.</summary>
    private static Vector3 ClosestPointOnSegment(Vector3 pointA, Vector3 pointB, Vector3 target)
    {
        Vector3 delta = pointB - pointA;
        float   sqLen = delta.sqrMagnitude;
        if (sqLen <= 0.000001f) return pointA;

        float t = Mathf.Clamp01(Vector3.Dot(target - pointA, delta) / sqLen);
        return pointA + delta * t;
    }

    /// <summary>
    /// Point on the mop shaft nearest <paramref name="col"/>. Used as the origin for the contact
    /// raycast and the reference for the particle placement so VFX appear at the part of the mop
    /// actually touching the surface, not at the pivot.
    /// </summary>
    private Vector3 GetShaftContactOrigin(Collider col)
    {
        GetScrubSegment(out Vector3 pointA, out Vector3 pointB);

        // Two iterations converge well enough on the mutually closest pair. Bias from the head end
        // (pointA) so contact resolves at the business end of the mop.
        Vector3 onSegment = ClosestPointOnSegment(pointA, pointB, col.ClosestPoint(pointA));
        onSegment = ClosestPointOnSegment(pointA, pointB, col.ClosestPoint(onSegment));
        return onSegment;
    }

    private GraffitiInteractable FindGraffitiInRange(out Collider hitCollider)
    {
        hitCollider = null;
        if (!IsHeld) return null;

        Collider[] hits = OverlapScrubVolume(_graffitiLayerMask, out _, out _);

        foreach (Collider col in hits)
        {
            if (col.transform.IsChildOf(transform)) continue;

            GraffitiInteractable graffiti = col.GetComponent<GraffitiInteractable>()
                                         ?? col.GetComponentInParent<GraffitiInteractable>();
            if (graffiti != null)
            {
                hitCollider = col;
                return graffiti;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the collider in <see cref="_surfaceLayerMask"/> whose surface point is closest
    /// to the mop shaft, or null if nothing is in range. Used to drive scrub particles on
    /// any surface, not just graffiti.
    /// </summary>
    private Collider FindSurfaceInRange()
    {
        if (!IsHeld) return null;

        Collider[] hits = OverlapScrubVolume(_surfaceLayerMask, out Vector3 pointA, out Vector3 pointB);

        Collider closest = null;
        float closestSqDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            if (col.transform.IsChildOf(transform)) continue;

            Vector3 reference = ClosestPointOnSegment(pointA, pointB, col.ClosestPoint(pointA));
            float sqDist = (col.ClosestPoint(reference) - reference).sqrMagnitude;
            if (sqDist < closestSqDist)
            {
                closestSqDist = sqDist;
                closest = col;
            }
        }

        return closest;
    }

    // ── Editor ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        GetScrubSegment(out Vector3 pointA, out Vector3 pointB);

        // Head end (pointA) in cyan, handle end in a dimmer tone.
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(pointA, _scrubRadius);
        Gizmos.color = new Color(0f, 0.8f, 1f, 1f);
        Gizmos.DrawWireSphere(pointA, _scrubRadius);

        Gizmos.color = new Color(0.4f, 0.5f, 0.6f, 0.15f);
        Gizmos.DrawSphere(pointB, _scrubRadius);
        Gizmos.color = new Color(0.4f, 0.6f, 0.75f, 1f);
        Gizmos.DrawWireSphere(pointB, _scrubRadius);
        Gizmos.DrawLine(pointA, pointB);
    }
#endif
}
