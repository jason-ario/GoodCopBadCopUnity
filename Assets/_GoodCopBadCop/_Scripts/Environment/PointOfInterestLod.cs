using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Rendering
{
    /// <summary>
    /// Local-only, distance-based render culling for a place of interest (booth, power station,
    /// radio station, ...). Put it on the location's root; it captures every MeshRenderer /
    /// SkinnedMeshRenderer underneath at Start and, against the local view camera
    /// (<see cref="Camera.main"/>), applies three tiers:
    ///
    ///   - Whole-location tier: past <see cref="_wholeCullDistance"/> from the location's overall
    ///     bounds (captured at Start), every managed renderer stops rendering, large silhouettes
    ///     included. Renderers whose own live bounds are still within that distance (carried
    ///     props) keep rendering.
    ///   - Detail tier: renderers smaller than <see cref="_smallPropSize"/> stop rendering past
    ///     <see cref="_detailCullDistance"/>.
    ///   - Fog tier: everything smaller than <see cref="_silhouetteSize"/> stops rendering once it
    ///     is fully hidden by the current RenderSettings fog (distance derived live from fog mode
    ///     and density, so weather presets and BunkerFogZone are respected). Large pieces stay so
    ///     silhouettes against the skybox don't pop. With fog off this tier does nothing.
    ///
    /// Culling only ever touches <see cref="Renderer.forceRenderingOff"/> (nothing else in the
    /// project writes it), so gameplay that toggles <c>enabled</c>/<c>SetActive</c>, colliders,
    /// raycasts, NetworkObjects and scripts are unaffected. Distances use each renderer's live
    /// bounds, so carried/moved props are judged where they currently are. Renderers created
    /// under the root after Start are not managed. Pure presentation: no network state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PointOfInterestLod : MonoBehaviour
    {
        /// <summary>Fog transmittance below which geometry is indistinguishable from fog colour.</summary>
        private const float FogInvisibleTransmittance = 0.002f;

        /// <summary>Camera jump (m) that forces an immediate re-evaluation (respawn, spectate, cutscene cut).</summary>
        private const float TeleportDistance = 10f;

        /// <summary>Global kill switch (debug / A-B comparisons). Restores everything when false.</summary>
        public static bool GloballyEnabled { get; set; } = true;

        [Header("Whole-location tier")]
        [Tooltip("When the camera is farther than this (m) from the location's overall bounds, every managed renderer stops rendering, silhouettes included. 0 disables.")]
        [SerializeField] private float _wholeCullDistance = 120f;

        [Tooltip("The location must come this much closer (m) than Whole Cull Distance before it shows again, so it doesn't flicker at the boundary.")]
        [SerializeField] private float _wholeCullHysteresis = 5f;

        [Header("Detail tier")]
        [Tooltip("Renderers whose world-bounds diagonal (m) is below this count as small props.")]
        [SerializeField] private float _smallPropSize = 1.5f;

        [Tooltip("Small props farther than this (m, camera to bounds surface) stop rendering.")]
        [SerializeField] private float _detailCullDistance = 45f;

        [Header("Fog tier")]
        [Tooltip("Cull everything below Silhouette Size once it is completely hidden by fog.")]
        [SerializeField] private bool _useFogCulling = true;

        [Tooltip("Renderers with a bounds diagonal (m) at least this large are never fog-culled, so skyline silhouettes stay.")]
        [SerializeField] private float _silhouetteSize = 8f;

        [Tooltip("Extra metres added on top of the fog-derived fully-hidden distance.")]
        [SerializeField] private float _fogDistanceMargin = 15f;

        [Tooltip("The fog tier never culls closer than this (m), whatever the fog density.")]
        [SerializeField] private float _minFogCullDistance = 60f;

        [Header("Exclusions")]
        [Tooltip("Renderers under these transforms are never culled (e.g. landmarks that must stay visible).")]
        [SerializeField] private Transform[] _neverCull = new Transform[0];

        [Header("Update")]
        [Tooltip("Seconds between evaluations. A camera jump larger than 10 m re-evaluates immediately.")]
        [SerializeField] private float _evaluateInterval = 0.2f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<bool> _culledByUs = new List<bool>();
        private float _nextEvaluateTime;
        private Vector3 _lastCameraPosition;
        private bool _wasGloballyEnabled = true;
        private Bounds _locationBounds;
        private bool _hasLocationBounds;

        /// <summary>True while the whole location is culled by distance (debug/profiling).</summary>
        public bool IsWholeLocationCulled { get; private set; }

        /// <summary>Number of managed renderers currently culled by this component (debug/profiling).</summary>
        public int CulledCount { get; private set; }

        /// <summary>Number of renderers managed by this component.</summary>
        public int ManagedCount => _renderers.Count;

        private void Start()
        {
            CollectRenderers();
            // Stagger locations so they don't all evaluate on the same frame.
            _nextEvaluateTime = Time.unscaledTime + Random.Range(0f, _evaluateInterval);
        }

        private void OnDisable()
        {
            RestoreAll();
        }

        private void LateUpdate()
        {
            if (!GloballyEnabled)
            {
                if (_wasGloballyEnabled)
                {
                    RestoreAll();
                    _wasGloballyEnabled = false;
                }
                return;
            }
            _wasGloballyEnabled = true;

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 camPos = cam.transform.position;
            bool teleported = (camPos - _lastCameraPosition).sqrMagnitude > TeleportDistance * TeleportDistance;
            if (!teleported && Time.unscaledTime < _nextEvaluateTime)
            {
                return;
            }

            _nextEvaluateTime = Time.unscaledTime + _evaluateInterval;
            _lastCameraPosition = camPos;
            Evaluate(camPos);
        }

        private void CollectRenderers()
        {
            _renderers.Clear();
            _culledByUs.Clear();

            var excluded = new HashSet<Renderer>();
            foreach (Transform root in _neverCull)
            {
                if (root == null) continue;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    excluded.Add(r);
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (excluded.Contains(r)) continue;
                _renderers.Add(r);
                _culledByUs.Add(false);

                // Overall footprint captured once; the location itself does not move.
                if (_hasLocationBounds) _locationBounds.Encapsulate(r.bounds);
                else { _locationBounds = r.bounds; _hasLocationBounds = true; }
            }
        }

        private bool EvaluateWholeLocation(Vector3 camPos)
        {
            if (_wholeCullDistance <= 0f || !_hasLocationBounds)
            {
                IsWholeLocationCulled = false;
                return false;
            }

            float threshold = IsWholeLocationCulled
                ? Mathf.Max(0f, _wholeCullDistance - _wholeCullHysteresis)
                : _wholeCullDistance;
            IsWholeLocationCulled = _locationBounds.SqrDistance(camPos) > threshold * threshold;
            return IsWholeLocationCulled;
        }

        private void Evaluate(Vector3 camPos)
        {
            float detailSqr = _detailCullDistance * _detailCullDistance;
            float fogDistance = GetFogCullDistance();
            bool fogTier = fogDistance > 0f;
            float fogSqr = fogDistance * fogDistance;
            float smallSqr = _smallPropSize * _smallPropSize;
            float silhouetteSqr = _silhouetteSize * _silhouetteSize;
            bool wholeCulled = EvaluateWholeLocation(camPos);
            float wholeSqr = _wholeCullDistance * _wholeCullDistance;

            int culled = 0;
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null)
                {
                    continue;
                }

                // Inactive objects keep their last state; they are re-judged once active again.
                if (!r.gameObject.activeInHierarchy)
                {
                    if (_culledByUs[i]) culled++;
                    continue;
                }

                Bounds b = r.bounds;
                float sizeSqr = b.size.sqrMagnitude;
                float distSqr = b.SqrDistance(camPos);

                // Whole-location cull, except props that were carried near the camera.
                bool cull = (wholeCulled && distSqr > wholeSqr)
                         || (sizeSqr < smallSqr && distSqr > detailSqr)
                         || (fogTier && sizeSqr < silhouetteSqr && distSqr > fogSqr);

                if (cull != _culledByUs[i])
                {
                    r.forceRenderingOff = cull;
                    _culledByUs[i] = cull;
                }

                if (cull) culled++;
            }

            CulledCount = culled;
        }

        /// <summary>
        /// Distance (m) past which the current RenderSettings fog fully hides geometry, plus margin.
        /// Returns 0 (fog tier disabled) when fog culling or Unity fog is off.
        /// </summary>
        private float GetFogCullDistance()
        {
            if (!_useFogCulling || !RenderSettings.fog)
            {
                return 0f;
            }

            float hidden;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                    hidden = RenderSettings.fogEndDistance;
                    break;
                case FogMode.Exponential:
                    if (RenderSettings.fogDensity <= 0f) return 0f;
                    hidden = -Mathf.Log(FogInvisibleTransmittance) / RenderSettings.fogDensity;
                    break;
                default: // ExponentialSquared
                    if (RenderSettings.fogDensity <= 0f) return 0f;
                    hidden = Mathf.Sqrt(-Mathf.Log(FogInvisibleTransmittance)) / RenderSettings.fogDensity;
                    break;
            }

            return Mathf.Max(hidden + _fogDistanceMargin, _minFogCullDistance);
        }

        private void RestoreAll()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (!_culledByUs[i]) continue;
                if (_renderers[i] != null) _renderers[i].forceRenderingOff = false;
                _culledByUs[i] = false;
            }
            CulledCount = 0;
            IsWholeLocationCulled = false;
        }
    }
}
