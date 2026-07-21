using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Applies bonus radiation to players who stray further than <see cref="safeRadius"/>
/// from the nearest point on a <see cref="TrailController"/> Catmull-Rom spline.
///
/// Fully additive with existing <see cref="RadiationHotspot"/> zones — both independently
/// call <see cref="PlayerRadiation.AddRadiation"/>. Runs server-side only, matching the
/// authority model of <see cref="PlayerRadiation"/>.
///
/// Enable/disable this component (or its GameObject) from <see cref="FollowTrailThreat"/>
/// to scope the effect to the trail event window, or leave it always-on for a global rule.
/// </summary>
public class OffTrailRadiation : MonoBehaviour
{
    [Header("Trail References")]
    [Tooltip("One or more TrailControllers whose splines define safe corridors. " +
             "A player is considered on-trail if they are within safeRadius of ANY listed trail.")]
    [SerializeField] private List<TrailController> trails = new();

    [Header("Safe Zones")]
    [Tooltip("RadiationSafeZone volumes (camps, shelters, etc.) that exempt players from the " +
             "off-trail penalty even when they are outside the trail corridor.")]
    [SerializeField] private List<RadiationSafeZone> safeZones = new();

    [Header("Zone Settings")]
    [Tooltip("Distance from the spline within which the player is considered on-trail and safe.")]
    [SerializeField] private float safeRadius = 15f;

    [Tooltip("Extra radiation per second applied when outside the safe radius. " +
             "Stacks additively on top of any RadiationHotspot zones the player is also inside.")]
    [SerializeField] private float bonusRadiationPerSecond = 0.5f;

    [Tooltip("Samples taken along the spline to approximate the closest point. " +
             "64 is accurate to ~0.5 m on typical trail lengths; raise for very long trails.")]
    [SerializeField, Range(16, 128)] private int splineSampleCount = 64;

    [Tooltip("Measure XZ distance only, ignoring height differences. " +
             "Recommended for outdoor terrain where elevation varies along the trail.")]
    [SerializeField] private bool ignoreYAxis = true;

    [Header("Gizmos")]
    [SerializeField] private Color safeZoneColor = new Color(0f, 0.9f, 0.4f, 0.18f);
    [SerializeField] private Color dangerZoneColor = new Color(1f, 0.3f, 0f, 0.08f);
    [SerializeField] private int gizmoCorridorSections = 40;

    // ── Internals ─────────────────────────────────────────────────────────────

    private readonly List<PlayerRadiation> _players = new();
    private float _refreshTimer;

    /// Interval at which the player list is rebuilt to pick up late-joiners.
    private const float PlayerRefreshInterval = 2f;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        // Seed the timer so the list is populated immediately on first Update.
        _refreshTimer = 0f;
    }

    private void Update()
    {
        // Mirror the authority model used by PlayerRadiation.Update() —
        // only the server (host or dedicated) drives radiation accumulation.
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
            return;

        if (trails == null || trails.Count == 0) return;

        // Rebuild the player list periodically rather than every frame.
        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            RefreshPlayerList();
            _refreshTimer = PlayerRefreshInterval;
        }

        float dt = Time.deltaTime;
        foreach (PlayerRadiation player in _players)
        {
            if (player == null || player.IsInvincible) continue;
            if (IsInAnySafeZone(player)) continue;

            float dist = GetDistanceToSpline(player.transform.position);
            if (dist > safeRadius)
            {
                player.AddRadiation(bonusRadiationPerSecond * player.RadiationMultiplier * dt);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshPlayerList()
    {
        _players.Clear();
        PlayerRadiation[] found = FindObjectsByType<PlayerRadiation>(FindObjectsSortMode.None);
        foreach (PlayerRadiation p in found)
            _players.Add(p);
    }

    /// <summary>
    /// Returns true if <paramref name="player"/> is currently inside any registered
    /// <see cref="RadiationSafeZone"/>, exempting them from the off-trail penalty.
    /// </summary>
    private bool IsInAnySafeZone(PlayerRadiation player)
    {
        foreach (RadiationSafeZone zone in safeZones)
        {
            if (zone != null && zone.Contains(player))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the minimum distance from <paramref name="worldPos"/> to the nearest spline
    /// across all registered trails, computed by sampling <see cref="splineSampleCount"/>
    /// evenly-spaced points per trail. Respects <see cref="ignoreYAxis"/>.
    /// </summary>
    private float GetDistanceToSpline(Vector3 worldPos)
    {
        float minSqr = float.MaxValue;
        float step = 1f / splineSampleCount;

        foreach (TrailController trail in trails)
        {
            if (trail == null) continue;

            for (int i = 0; i <= splineSampleCount; i++)
            {
                Vector3 sp = trail.SampleSpline(i * step);

                float sqr;
                if (ignoreYAxis)
                {
                    float dx = worldPos.x - sp.x;
                    float dz = worldPos.z - sp.z;
                    sqr = dx * dx + dz * dz;
                }
                else
                {
                    sqr = (worldPos - sp).sqrMagnitude;
                }

                if (sqr < minSqr) minSqr = sqr;
            }
        }

        return Mathf.Sqrt(minSqr);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        foreach (TrailController trail in trails)
        {
            if (trail == null || trail.Waypoints == null || trail.Waypoints.Count < 2) continue;

            for (int i = 0; i <= gizmoCorridorSections; i++)
            {
                float t  = (float)i / gizmoCorridorSections;
                Vector3 pt = trail.SampleSpline(t);

                Gizmos.color = safeZoneColor;
                Gizmos.DrawWireSphere(pt, safeRadius);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        float dangerRadius = safeRadius * 1.5f;

        foreach (TrailController trail in trails)
        {
            if (trail == null || trail.Waypoints == null || trail.Waypoints.Count < 2) continue;

            for (int i = 0; i <= gizmoCorridorSections; i++)
            {
                float t    = (float)i / gizmoCorridorSections;
                Vector3 pt = trail.SampleSpline(t);

                Gizmos.color = dangerZoneColor;
                Gizmos.DrawWireSphere(pt, dangerRadius);
            }
        }
    }
#endif
}
