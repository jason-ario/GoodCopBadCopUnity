using System.Collections;
using UnityEngine;

// All PickableObject, Interactable, and NetworkBehaviour base types are in the project's global
// using declarations, so no additional using directives are required for those namespaces.

/// <summary>
/// A mop the player can pick up and use to scrub graffiti off checkpoint walls.
///
/// Hold LMB while holding the Mop to enter the "UsingTool" animation state and begin
/// scrubbing. An overlap sphere centred on the mop detects any <see cref="GraffitiInteractable"/>
/// within <see cref="_scrubRadius"/>; while overlap is maintained the graffiti's progress
/// advances on the server until it is fully removed.
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
    [Tooltip("Radius of the overlap sphere used to detect graffiti from the mop's position.")]
    [SerializeField] private float _scrubRadius = 0.6f;

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
                    Vector3 closestPoint = particleCollider.ClosestPoint(transform.position);
                    Vector3 toSurface    = closestPoint - transform.position;
                    float   dist         = toSurface.magnitude;

                    // Raycast from the mop centre toward the surface to get the real normal.
                    // This correctly handles angled contact (e.g. mopping the floor while
                    // holding the handle at an angle), unlike the mop-to-surface approximation.
                    Vector3 hitPoint      = closestPoint;
                    Vector3 surfaceNormal = dist > 0.001f ? -toSurface.normalized : Vector3.up;
                    if (dist > 0.001f)
                    {
                        RaycastHit hit;
                        if (Physics.Raycast(transform.position, toSurface.normalized,
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

    private GraffitiInteractable FindGraffitiInRange(out Collider hitCollider)
    {
        hitCollider = null;
        if (!IsHeld) return null;

        Collider[] hits = Physics.OverlapSphere(transform.position, _scrubRadius, _graffitiLayerMask);

        foreach (Collider col in hits)
        {
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
    /// to the mop centre, or null if nothing is in range. Used to drive scrub particles on
    /// any surface, not just graffiti.
    /// </summary>
    private Collider FindSurfaceInRange()
    {
        if (!IsHeld) return null;

        Collider[] hits = Physics.OverlapSphere(transform.position, _scrubRadius, _surfaceLayerMask);

        Collider closest = null;
        float closestSqDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            float sqDist = (col.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
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
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, _scrubRadius);
        Gizmos.color = new Color(0f, 0.8f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, _scrubRadius);
    }
#endif
}
