using DG.Tweening;
using UnityEngine;

/// <summary>
/// Placement polish: DOPunchScale on the dropped object and a "poof" burst
/// particle effect whose spawn positions are individually sampled from the
/// collider face that is touching the placement surface.
///
/// Attach to a child of the ObjectPlacer alongside a ParticleSystem component.
/// The ParticleSystem only needs its visual modules configured (size/color over
/// lifetime, gravity, renderer). Emission and shape are driven entirely in code.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class PlacementFeedback : MonoBehaviour
{
    [Header("Punch Scale")]
    [SerializeField] private Vector3 punchStrength = new Vector3(0.2f, 0.2f, 0.2f);
    [SerializeField] private float punchDuration = 0.35f;
    [SerializeField] private int punchVibrato = 6;
    [SerializeField][Range(0f, 1f)] private float punchElasticity = 0.5f;

    [Header("Poof Emission")]
    [SerializeField] private int burstCountMin = 12;
    [SerializeField] private int burstCountMax = 20;
    [SerializeField] private float lifetimeMin = 0.2f;
    [SerializeField] private float lifetimeMax = 0.45f;
    [SerializeField] private float sizeMin = 0.04f;
    [SerializeField] private float sizeMax = 0.12f;
    [SerializeField] private float velocityMin = 0.4f;
    [SerializeField] private float velocityMax = 1.2f;
    [Tooltip("Half-angle of the launch cone relative to the surface normal.")]
    [SerializeField][Range(0f, 90f)] private float spreadAngle = 35f;

    [Header("Placement Sound")]
    [Tooltip("Fallback clip played when the placed item's PickableItemData has no PlacementSound " +
             "of its own assigned. Prefer setting a per-item sound on PickableItemData.PlacementSound " +
             "so different objects (mail package, folder, mop, etc.) get their own distinct thud.")]
    [SerializeField] private AudioClip _defaultPlacementSfxClip;
    [Tooltip("Volume for _defaultPlacementSfxClip.")]
    [SerializeField] private float _defaultPlacementSfxVolume = 1f;

    private ParticleSystem _ps;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// Fires DOPunchScale on <paramref name="placedObject"/>, emits a poof burst at the collider
    /// face touching the surface (oriented by <paramref name="surfaceNormal"/>), and plays a
    /// placement sound. <paramref name="placementSfxClip"/>/<paramref name="placementSfxVolume"/>
    /// are normally sourced from the placed item's own <see cref="PickableItemData.PlacementSound"/>
    /// so each object type gets its own distinct sound — pass null to fall back to this
    /// component's <see cref="_defaultPlacementSfxClip"/> instead.
    /// </summary>
    public void PlayPlacementFeedback(Transform placedObject, Vector3 contactPoint, Vector3 surfaceNormal,
        AudioClip placementSfxClip = null, float placementSfxVolume = 1f)
    {
        if (placedObject != null)
            PlayPunchScale(placedObject);

        PlayPoofParticle(placedObject, contactPoint, surfaceNormal);
        PlayPlacementSfx(contactPoint, placementSfxClip, placementSfxVolume);
    }

    // -------------------------------------------------------------------------
    // Punch scale
    // -------------------------------------------------------------------------

    private void PlayPunchScale(Transform target)
    {
        target.DOKill(complete: true);
        target.DOPunchScale(Vector3.Scale(target.localScale, punchStrength), punchDuration, punchVibrato, punchElasticity);
    }

    // -------------------------------------------------------------------------
    // Placement sound
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays the "object placed" thud at <paramref name="contactPoint"/>. Fires on every
    /// placement unconditionally — success/failure feedback for a specific placement outcome
    /// (e.g. MailPackageItem's sort-success chime) is layered on top of this by whatever system
    /// evaluates that outcome, separately and slightly after this call. Uses
    /// <paramref name="clip"/> (normally the placed item's own PickableItemData.PlacementSound)
    /// when provided, otherwise falls back to <see cref="_defaultPlacementSfxClip"/>.
    /// </summary>
    private void PlayPlacementSfx(Vector3 contactPoint, AudioClip clip, float volume)
    {
        AudioClip resolvedClip = clip != null ? clip : _defaultPlacementSfxClip;
        float resolvedVolume = clip != null ? volume : _defaultPlacementSfxVolume;

        if (resolvedClip != null)
            SFXController.Instance?.PlayAtPosition(resolvedClip, contactPoint, resolvedVolume);
    }

    // -------------------------------------------------------------------------
    // Poof particle — manual per-particle Emit so positions come from collider
    // -------------------------------------------------------------------------

    private void PlayPoofParticle(Transform placedObject, Vector3 contactPoint, Vector3 surfaceNormal)
    {
        if (_ps == null) return;

        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Keep the PS transform at the contact point so any radial/noise
        // modules in the inspector remain correctly anchored.
        _ps.transform.position = contactPoint;

        Vector3 normal = surfaceNormal.sqrMagnitude > 0.001f
            ? surfaceNormal.normalized
            : Vector3.up;

        int count = Random.Range(burstCountMin, burstCountMax + 1);
        for (int i = 0; i < count; i++)
        {
            // Sample a random world-space point on the collider face that is
            // resting against the placement surface.
            Vector3 spawnPos = placedObject != null
                ? SampleColliderContactFace(placedObject, normal)
                : contactPoint;

            var ep = new ParticleSystem.EmitParams
            {
                position      = spawnPos,
                velocity      = BuildConeVelocity(normal),
                startLifetime = Random.Range(lifetimeMin, lifetimeMax),
                startSize     = Random.Range(sizeMin, sizeMax),
            };

            _ps.Emit(ep, 1);
        }
    }

    /// <summary>Builds a random velocity inside a cone pointing along <paramref name="normal"/>.</summary>
    private Vector3 BuildConeVelocity(Vector3 normal)
    {
        float speed       = Random.Range(velocityMin, velocityMax);
        float coneRad     = Random.Range(0f, spreadAngle * Mathf.Deg2Rad);
        float azimuth     = Random.Range(0f, Mathf.PI * 2f);
        Vector3 tangent   = GetTangent(normal);
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        Vector3 dir = normal    * Mathf.Cos(coneRad)
                    + tangent   * (Mathf.Sin(coneRad) * Mathf.Cos(azimuth))
                    + bitangent * (Mathf.Sin(coneRad) * Mathf.Sin(azimuth));

        return dir.normalized * speed;
    }

    // -------------------------------------------------------------------------
    // Collider face sampling — returns a random world-space point on the face
    // whose outward normal is most aligned with -surfaceNormal (facing into the
    // placement surface).
    // -------------------------------------------------------------------------

    private static Vector3 SampleColliderContactFace(Transform target, Vector3 surfaceNormal)
    {
        Collider col = null;
        foreach (Collider c in target.GetComponentsInChildren<Collider>(true))
        {
            if (!c.isTrigger) { col = c; break; }
        }
        if (col == null) return target.position;

        if (col is BoxCollider box)     return SampleBoxFace(box, surfaceNormal);
        if (col is SphereCollider sph)  return SampleSphereFace(sph, surfaceNormal);
        if (col is CapsuleCollider cap) return SampleCapsuleFace(cap, surfaceNormal);
        return SampleBoundsFace(col.bounds, surfaceNormal);
    }

    private static Vector3 SampleBoxFace(BoxCollider box, Vector3 surfaceNormal)
    {
        Transform t    = box.transform;
        Vector3 center = t.TransformPoint(box.center);
        Vector3 scale  = t.lossyScale;

        // World-space half-vectors along each local axis
        Vector3 hR = t.right   * (box.size.x * Mathf.Abs(scale.x) * 0.5f);
        Vector3 hU = t.up      * (box.size.y * Mathf.Abs(scale.y) * 0.5f);
        Vector3 hF = t.forward * (box.size.z * Mathf.Abs(scale.z) * 0.5f);

        // (outward face normal, center offset, tangent half-vec, bitangent half-vec)
        var faces = new (Vector3 n, Vector3 off, Vector3 tA, Vector3 tB)[]
        {
            ( t.right,    hR,  hU, hF),
            (-t.right,   -hR,  hU, hF),
            ( t.up,       hU,  hR, hF),
            (-t.up,      -hU,  hR, hF),
            ( t.forward,  hF,  hR, hU),
            (-t.forward, -hF,  hR, hU),
        };

        // The touching face has its outward normal most aligned with -surfaceNormal
        Vector3 into = -surfaceNormal;
        int best = 0;
        float bestDot = Vector3.Dot(faces[0].n, into);
        for (int i = 1; i < faces.Length; i++)
        {
            float d = Vector3.Dot(faces[i].n, into);
            if (d > bestDot) { bestDot = d; best = i; }
        }

        Vector3 fc = center + faces[best].off;
        return fc
             + faces[best].tA * Random.Range(-1f, 1f)
             + faces[best].tB * Random.Range(-1f, 1f);
    }

    private static Vector3 SampleSphereFace(SphereCollider sph, Vector3 surfaceNormal)
    {
        Transform t    = sph.transform;
        Vector3 center = t.TransformPoint(sph.center);
        float radius   = sph.radius * Mathf.Max(
            Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z));

        Vector3 contact = center - surfaceNormal * radius;
        Vector2 disk    = Random.insideUnitCircle * (radius * 0.5f);
        Vector3 t0      = GetTangent(surfaceNormal);
        Vector3 t1      = Vector3.Cross(surfaceNormal, t0);
        return contact + t0 * disk.x + t1 * disk.y;
    }

    private static Vector3 SampleCapsuleFace(CapsuleCollider cap, Vector3 surfaceNormal)
    {
        Transform t    = cap.transform;
        Vector3 center = t.TransformPoint(cap.center);
        float radius   = cap.radius * Mathf.Max(
            Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.z));

        Vector3 contact = center - surfaceNormal * radius;
        Vector2 disk    = Random.insideUnitCircle * radius;
        Vector3 t0      = GetTangent(surfaceNormal);
        Vector3 t1      = Vector3.Cross(surfaceNormal, t0);
        return contact + t0 * disk.x + t1 * disk.y;
    }

    private static Vector3 SampleBoundsFace(Bounds bounds, Vector3 surfaceNormal)
    {
        Vector3 into = -surfaceNormal.normalized;

        var faces = new (Vector3 n, Vector3 c, float eA, float eB)[]
        {
            (Vector3.right,   bounds.center + new Vector3( bounds.extents.x, 0, 0), bounds.extents.y, bounds.extents.z),
            (Vector3.left,    bounds.center - new Vector3( bounds.extents.x, 0, 0), bounds.extents.y, bounds.extents.z),
            (Vector3.up,      bounds.center + new Vector3(0,  bounds.extents.y, 0), bounds.extents.x, bounds.extents.z),
            (Vector3.down,    bounds.center - new Vector3(0,  bounds.extents.y, 0), bounds.extents.x, bounds.extents.z),
            (Vector3.forward, bounds.center + new Vector3(0, 0,  bounds.extents.z), bounds.extents.x, bounds.extents.y),
            (Vector3.back,    bounds.center - new Vector3(0, 0,  bounds.extents.z), bounds.extents.x, bounds.extents.y),
        };

        int best = 0;
        float bestDot = Vector3.Dot(faces[0].n, into);
        for (int i = 1; i < faces.Length; i++)
        {
            float d = Vector3.Dot(faces[i].n, into);
            if (d > bestDot) { bestDot = d; best = i; }
        }

        Vector3 tangent   = GetTangent(faces[best].n);
        Vector3 bitangent = Vector3.Cross(faces[best].n, tangent).normalized;
        return faces[best].c
             + tangent   * (Random.Range(-1f, 1f) * faces[best].eA)
             + bitangent * (Random.Range(-1f, 1f) * faces[best].eB);
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static Vector3 GetTangent(Vector3 normal)
    {
        Vector3 t = Vector3.Cross(normal, Vector3.up);
        if (t.sqrMagnitude < 0.001f)
            t = Vector3.Cross(normal, Vector3.forward);
        return t.normalized;
    }
}
