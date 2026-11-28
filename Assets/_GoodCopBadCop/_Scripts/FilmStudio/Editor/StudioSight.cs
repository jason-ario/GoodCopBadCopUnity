using System.Text.RegularExpressions;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Camera sight-line queries for sets. Glass and windows (<see cref="StudioSeeThrough"/> occluders, colliders whose
    /// name says glass/pane, or transparent materials) don't block the view, so cameras can shoot through the booth window.
    /// </summary>
    public static class StudioSight
    {
        private static readonly Regex GlassName = new(@"(glass|pane\b|pane[\s_.(]|windowpane|windshield|plexi)", RegexOptions.IgnoreCase);
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly Collider[] Overlaps = new Collider[32];

        public static bool IsGlassName(string n) => !string.IsNullOrEmpty(n) && GlassName.IsMatch(n) && !n.ToLowerInvariant().Contains("lamp");

        /// <summary>True if the renderer draws with a transparent material (render queue ≥ 2900).</summary>
        public static bool IsTransparent(Renderer r)
        {
            if (r == null) return false;
            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            foreach (Material m in mats)
                if (m == null || m.renderQueue < 2900) return false;
            return true;
        }

        public static bool IsSeeThrough(Collider c)
        {
            if (c == null) return true;
            if (c.GetComponent<StudioSeeThrough>() != null) return true;
            if (c.GetComponentInParent<StudioActor>() != null) return true; // actors never block their own framing
            if (IsGlassName(c.name) || (c.transform.parent != null && IsGlassName(c.transform.parent.name))) return true;
            return IsTransparent(c.GetComponent<Renderer>());
        }

        /// <summary>Line of sight from <paramref name="from"/> to <paramref name="to"/>, ignoring triggers and see-through colliders.</summary>
        public static bool IsClear(Vector3 from, Vector3 to, out bool throughGlass)
        {
            throughGlass = false;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.001f) return true;
            int n = Physics.RaycastNonAlloc(from, d / len, Hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (IsSeeThrough(Hits[i].collider))
                {
                    if (Hits[i].collider != null && Hits[i].collider.GetComponentInParent<StudioActor>() == null) throughGlass = true;
                    continue;
                }
                return false;
            }
            return true;
        }

        public static bool IsClear(Vector3 from, Vector3 to) => IsClear(from, to, out _);

        /// <summary>Distance to the first opaque hit along a ray (max if none).</summary>
        public static float OpenDistance(Vector3 from, Vector3 dir, float max)
        {
            int n = Physics.RaycastNonAlloc(from, dir.normalized, Hits, max, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = max;
            for (int i = 0; i < n; i++)
                if (!IsSeeThrough(Hits[i].collider) && Hits[i].distance < best) best = Hits[i].distance;
            return best;
        }

        /// <summary>True if a camera of <paramref name="radius"/> fits at <paramref name="p"/> without touching opaque geometry.</summary>
        public static bool HasClearance(Vector3 p, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(p, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsSeeThrough(Overlaps[i])) return false;
            return true;
        }
    }
}
