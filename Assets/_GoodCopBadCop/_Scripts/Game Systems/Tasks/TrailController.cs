using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a Catmull-Rom spline through a series of scene waypoints and produces
/// semi-randomly distributed spawn positions along it for UV-visible blood particles.
///
/// Scene setup:
///   - Add this component to any GameObject in the scene.
///   - Assign at least 2 Transform waypoints in the Inspector.
///   - Reference this TrailController from a FollowTrailLocation on FollowTrailThreat.
///
/// Gizmos:
///   - Always visible: red spline curve, dim yellow waypoint spheres.
///   - Selected: bright waypoints, straight connector lines, orange spawn-point preview.
///     Spawn preview uses a fixed random seed so positions stay stable in the editor.
/// </summary>
public class TrailController : MonoBehaviour
{
    [Header("Waypoints")]
    [Tooltip("Control points for the Catmull-Rom spline. Minimum 2 required.")]
    [SerializeField] private List<Transform> _waypoints = new();

    [Header("Spawn Settings")]
    [Tooltip("Number of blood-particle emitters placed along the trail at runtime.")]
    [SerializeField] private int _spawnCount = 12;

    [Tooltip("How far each spawn deviates from its evenly-spaced slot centre. " +
             "0 = perfectly even spacing, 1 = fully random within each slot.")]
    [SerializeField, Range(0f, 1f)] private float _jitter = 0.4f;

    [Header("Gizmos")]
    [SerializeField] private Color _splineColor        = new Color(0.85f, 0.10f, 0.10f, 1.00f);
    [SerializeField] private Color _waypointColor      = new Color(1.00f, 0.85f, 0.00f, 1.00f);
    [SerializeField] private Color _spawnPreviewColor  = new Color(1.00f, 0.45f, 0.00f, 0.90f);
    [SerializeField] private float _waypointRadius     = 0.15f;
    [SerializeField] private int   _gizmoResolution    = 60;

    // ── Public read-only access used by the custom editor ────────────────────

    public IReadOnlyList<Transform> Waypoints      => _waypoints;
    public Color                    WaypointColor  => _waypointColor;
    public float                    WaypointRadius => _waypointRadius;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="count"/> world-space positions distributed semi-randomly
    /// along the Catmull-Rom spline. Each position is offset within its equal-width slot
    /// by <paramref name="jitter"/> (0 = even, 1 = fully random within slot).
    /// </summary>
    public List<Vector3> GetSpawnPositions(int count, float jitter)
    {
        var positions = new List<Vector3>(count);

        if (_waypoints == null || _waypoints.Count < 2)
        {
            Debug.LogWarning("[TrailController] Need at least 2 waypoints to generate spawn positions.", this);
            return positions;
        }

        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f + Random.Range(-0.5f, 0.5f) * jitter) / count;
            positions.Add(SampleSpline(Mathf.Clamp01(t)));
        }

        return positions;
    }

    /// <summary>
    /// Convenience overload using the serialized <see cref="_spawnCount"/> and <see cref="_jitter"/>.
    /// </summary>
    public List<Vector3> GetSpawnPositions() => GetSpawnPositions(_spawnCount, _jitter);

    /// <summary>
    /// Samples the Catmull-Rom spline at a normalised t ∈ [0, 1].
    /// Ghost control points at both ends are reflected so the curve passes cleanly
    /// through the first and last waypoints.
    /// </summary>
    public Vector3 SampleSpline(float t)
    {
        if (_waypoints == null || _waypoints.Count == 0) return transform.position;
        if (_waypoints.Count == 1)
            return _waypoints[0] != null ? _waypoints[0].position : transform.position;

        int n        = _waypoints.Count;
        int segments = n - 1;

        float scaledT = Mathf.Clamp01(t) * segments;
        int   seg     = Mathf.Min(Mathf.FloorToInt(scaledT), segments - 1);
        float localT  = scaledT - seg;

        Vector3 p0 = GetWaypointPos(seg - 1, n);
        Vector3 p1 = GetWaypointPos(seg,     n);
        Vector3 p2 = GetWaypointPos(seg + 1, n);
        Vector3 p3 = GetWaypointPos(seg + 2, n);

        return CatmullRom(p0, p1, p2, p3, localT);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector3 GetWaypointPos(int index, int count)
    {
        // Clamp to valid range (ghost points at endpoints are pinned to the nearest real waypoint).
        index = Mathf.Clamp(index, 0, count - 1);
        Transform wp = _waypoints[index];
        return wp != null ? wp.position : transform.position;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
              2f * p1
            + (-p0 + p2)                        * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3)     * t3
        );
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (_waypoints == null || _waypoints.Count < 2) return;

        // Spline curve.
        Gizmos.color = _splineColor;
        Vector3 prev = SampleSpline(0f);
        for (int i = 1; i <= _gizmoResolution; i++)
        {
            Vector3 next = SampleSpline((float)i / _gizmoResolution);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Waypoints — dim when not selected.
        Gizmos.color = new Color(_waypointColor.r, _waypointColor.g, _waypointColor.b, 0.35f);
        foreach (Transform wp in _waypoints)
        {
            if (wp != null)
                Gizmos.DrawSphere(wp.position, _waypointRadius);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_waypoints == null) return;

        // Bright waypoints + straight connector lines.
        for (int i = 0; i < _waypoints.Count; i++)
        {
            if (_waypoints[i] == null) continue;

            Gizmos.color = _waypointColor;
            Gizmos.DrawSphere(_waypoints[i].position, _waypointRadius * 1.6f);

            if (i > 0 && _waypoints[i - 1] != null)
            {
                Gizmos.color = new Color(_waypointColor.r, _waypointColor.g, _waypointColor.b, 0.25f);
                Gizmos.DrawLine(_waypoints[i - 1].position, _waypoints[i].position);
            }
        }

        if (_waypoints.Count < 2) return;

        // Spawn-point preview — use a fixed seed so positions are stable in the editor.
        Random.State savedState = Random.state;
        Random.InitState(1337);

        Gizmos.color = _spawnPreviewColor;
        foreach (Vector3 p in GetSpawnPositions(_spawnCount, _jitter))
            Gizmos.DrawWireSphere(p, _waypointRadius * 0.55f);

        Random.state = savedState;
    }
}
