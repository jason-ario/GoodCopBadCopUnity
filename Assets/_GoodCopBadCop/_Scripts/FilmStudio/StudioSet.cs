using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// A film set in the Film Mode overlay: marks, points of interest, camera spots and camera occluders aligned to a
    /// live Main.unity environment root (<see cref="sourceRoot"/>). The environment itself is rendered by Main.unity, not copied.
    /// </summary>
    [DisallowMultipleComponent]
    public class StudioSet : MonoBehaviour
    {
        public string setId;
        [Tooltip("Main.unity root this set's marks were generated from, e.g. '---ENV_Booth'.")]
        public string sourceRoot;
        [TextArea(2, 5)] public string description;
        public List<StudioMark> marks = new();
        public List<StudioPoint> points = new();
        public List<StudioCameraSpot> cameraSpots = new();

        [Tooltip("Optional preset camera spots (saved from the Scene View). Prefer StudioCameraSpot components.")]
        public List<Transform> cameraAnchors = new();

        public void Activate(bool on) => gameObject.SetActive(on);

        /// <summary>Re-collects marks, points and camera spots from children (including inactive).</summary>
        public void RefreshMarks()
        {
            marks.Clear();
            marks.AddRange(GetComponentsInChildren<StudioMark>(true));
            points.Clear();
            points.AddRange(GetComponentsInChildren<StudioPoint>(true));
            cameraSpots.Clear();
            cameraSpots.AddRange(GetComponentsInChildren<StudioCameraSpot>(true));
        }

        public StudioMark FindMark(string markId)
        {
            foreach (StudioMark m in marks)
                if (m != null && m.markId == markId) return m;
            return null;
        }

        public StudioPoint FindPoint(string pointId)
        {
            foreach (StudioPoint p in points)
                if (p != null && p.pointId == pointId) return p;
            return null;
        }

        public StudioCameraSpot FindSpot(string spotId)
        {
            foreach (StudioCameraSpot s in cameraSpots)
                if (s != null && s.spotId == spotId) return s;
            return null;
        }

        /// <summary>World position of a mark or point id (null if neither exists).</summary>
        public Vector3? FindTargetPosition(string id)
        {
            Transform t = FindTargetTransform(id);
            return t != null ? t.position : null;
        }

        /// <summary>Transform of a point or mark id (points first), or null.</summary>
        public Transform FindTargetTransform(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            StudioPoint p = FindPoint(id);
            if (p != null) return p.transform;
            StudioMark m = FindMark(id);
            return m != null ? m.transform : null;
        }

        /// <summary>Average position of all marks (a cheap "inside of the set" reference).</summary>
        public Vector3 MarksCentre()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (StudioMark m in marks)
                if (m != null) { sum += m.transform.position; n++; }
            return n > 0 ? sum / n : transform.position;
        }
    }
}
