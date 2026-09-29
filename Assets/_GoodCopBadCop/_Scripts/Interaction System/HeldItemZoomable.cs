using UnityEngine;

/// <summary>
/// Marks a <see cref="PickableObject"/> (ID card, application, newspaper, daily fax, folder, ...)
/// as usable in zoom mode. While the local player holds this item and presses the zoom key,
/// <see cref="HeldItemZoomView"/> blends to a close-up camera that follows the item and pans
/// with the cursor, reusing the same diegetic camera system as the mini fridge / tool locker.
///
/// Per-item options:
///  • Framing: automatic (camera moves along the player's eye → document line and follows the
///    document as the hand animates) or hand-authored via <see cref="_cameraAnchor"/>
///    (e.g. the folder's "Zoom Cam Pos", locked to the item).
///  • <see cref="_inspectWhileZoomed"/>: zoom acts as if LMB is held for its whole duration,
///    so the document runs its normal inspect (see <see cref="HeldDocumentInspection"/>).
///  • Folders: entering zoom opens a closed folder first (same as LMB).
/// Purely local: no networking beyond what the item's own use/open paths already do.
/// </summary>
[DisallowMultipleComponent]
public class HeldItemZoomable : MonoBehaviour
{
    [Header("Framing")]
    [Tooltip("Optional child Transform whose pose the zoom camera uses exactly (its +Z must face the document). " +
             "Leave empty to auto-frame from the player's current viewpoint.")]
    [SerializeField] private Transform _cameraAnchor;

    [Tooltip("Anchor framing only: extra distance (world units) the camera is pulled back from the anchor along its view direction. " +
             "Tune this instead of moving the anchor when the close-up is too tight.")]
    [SerializeField] private float _anchorPullback = 0f;

    [Tooltip("Optional point the camera centres on. Defaults to the centre of this item's visible renderers.")]
    [SerializeField] private Transform _focusPoint;

    [Tooltip("Auto-framing only: look straight down the document's surface normal (camera flat with the paper) " +
             "instead of along the player's eye line. Use for small documents held at a tilt.")]
    [SerializeField] private bool _faceDocumentSurface;

    [Tooltip("Renderer whose flat face defines the document surface. Leave empty to use the largest flat renderer on this item.")]
    [SerializeField] private Renderer _documentSurface;

    // FOV is intentionally not configurable: zoom reuses the player camera's lens so the blend
    // never animates lens values (that made lights flicker mid-blend).

    [Tooltip("Camera distance from the focus point (auto-framing only). 0 = auto-fit to the item's renderer bounds.")]
    [SerializeField, Min(0f)] private float _zoomDistance = 0f;

    [Tooltip("Multiplier on the auto-fit size. >1 leaves a margin around the document, <1 crops in tighter.")]
    [SerializeField, Min(0.1f)] private float _autoFitPadding = 1.1f;

    [Tooltip("Closest the camera may get to the focus point (world units).")]
    [SerializeField, Min(0.02f)] private float _minDistance = 0.08f;

    [Header("Cursor Pan")]
    [Tooltip("Max horizontal pan as a fraction of the framed half-size. Cursor at screen edge = full pan.")]
    [SerializeField, Min(0f)] private float _panX = 0.6f;

    [Tooltip("Max vertical pan as a fraction of the framed half-size.")]
    [SerializeField, Min(0f)] private float _panY = 0.6f;

    [Tooltip("How quickly the camera catches up to the cursor pan target. 0 = instant.")]
    [SerializeField, Min(0f)] private float _panSmoothing = 12f;

    [Header("Behaviour")]
    [Tooltip("Zoom acts as if LMB is held for its whole duration (ID card, application, newspaper, daily fax). Released when zoom closes.")]
    [SerializeField] private bool _inspectWhileZoomed = true;

    [Tooltip("Hide the first-person arms while zoomed (useful if the hands clip into a tight close-up).")]
    [SerializeField] private bool _hidePlayerArms;

    [Tooltip("Folders only: seconds to wait for the open request to replicate before zoom gives up.")]
    [SerializeField, Min(0.1f)] private float _folderOpenTimeout = 2f;

    private FolderController _folder;
    private PickableObject _pickable;
    private float _folderOpenDeadline = -1f;

    public PickableObject Pickable => _pickable;
    public float PanX => _panX;
    public float PanY => _panY;
    public float PanSmoothing => _panSmoothing;
    public bool HidePlayerArms => _hidePlayerArms;
    public bool InspectWhileZoomed => _inspectWhileZoomed;

    /// <summary>Whether zoom may start on this item right now.</summary>
    public virtual bool CanBeginZoom => isActiveAndEnabled;

    /// <summary>
    /// Whether an open zoom may continue. Folders stay valid while opening (the open is a
    /// short delay + server round trip) and close the zoom if they end up closed.
    /// </summary>
    public virtual bool CanStayZoomed
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (_folder == null) return true;

            if (_folder.IsOpen)
            {
                _folderOpenDeadline = -1f;
                return true;
            }

            return _folder.IsOpeningOrClosing || Time.unscaledTime < _folderOpenDeadline;
        }
    }

    private void Awake()
    {
        _pickable = GetComponent<PickableObject>();
        _folder = GetComponent<FolderController>();
    }

    /// <summary>Called by the zoom view right before it opens. Opens a closed folder first.</summary>
    public void PrepareForZoom()
    {
        _folderOpenDeadline = -1f;
        if (_folder != null && !_folder.IsOpen && _folder.RequestOpen())
            _folderOpenDeadline = Time.unscaledTime + _folderOpenTimeout;
    }

    /// <summary>True when framing comes from a hand-authored <see cref="_cameraAnchor"/> (e.g. the folder).</summary>
    public bool HasCameraAnchor => _cameraAnchor != null;

    /// <summary>
    /// Anchor framing: the zoom camera pose in this item's local space (so it follows the item
    /// as the hand animates) plus the visible half-height at the document's depth, used to
    /// scale panning.
    /// </summary>
    public void ComputeAnchorPose(float fieldOfView, out Vector3 localPosition, out Quaternion localRotation, out float framedHalfSize)
    {
        Vector3 focus = _focusPoint != null ? _focusPoint.position : GetVisualBounds().center;
        float tanHalfFov = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

        if (_cameraAnchor == null)
        {
            localPosition = transform.InverseTransformPoint(focus - transform.forward * _minDistance);
            localRotation = Quaternion.identity;
            framedHalfSize = _minDistance * tanHalfFov;
            return;
        }

        Vector3 cameraPosition = _cameraAnchor.position - _cameraAnchor.forward * _anchorPullback;
        localPosition = transform.InverseTransformPoint(cameraPosition);
        localRotation = Quaternion.Inverse(transform.rotation) * _cameraAnchor.rotation;

        // Visible half-height at the document's depth — independent of renderer bounds,
        // which don't follow the folder's open blendshape.
        float depth = Vector3.Dot(focus - cameraPosition, _cameraAnchor.forward);
        framedHalfSize = Mathf.Max(depth, _minDistance) * tanHalfFov;
    }

    /// <summary>
    /// Eye framing (no anchor): returns the focus point in this item's local space and the
    /// camera distance from it. The zoom view places the camera on the line from the player's
    /// eye to the focus every frame, so the close-up shows the document exactly as the player
    /// sees it while inspecting (hand animation included) — just closer.
    /// </summary>
    public void ComputeEyeFraming(float fieldOfView, out Vector3 localFocus, out float distance, out float framedHalfSize)
    {
        Bounds bounds = GetVisualBounds();
        Vector3 focus = _focusPoint != null ? _focusPoint.position : bounds.center;
        float tanHalfFov = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

        framedHalfSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * _autoFitPadding;
        distance = _zoomDistance > 0f ? _zoomDistance : framedHalfSize / tanHalfFov;
        distance = Mathf.Max(distance, _minDistance);
        localFocus = transform.InverseTransformPoint(focus);
    }

    /// <summary>
    /// When <see cref="_faceDocumentSurface"/> is on, returns the document's surface normal in
    /// this item's local space (so it follows the hand animation): the thinnest axis of the
    /// document renderer. The zoom view picks the side facing the player.
    /// </summary>
    public bool TryGetDocumentNormal(out Vector3 localNormal)
    {
        localNormal = Vector3.forward;
        if (!_faceDocumentSurface) return false;

        Renderer surface = _documentSurface != null ? _documentSurface : FindLargestFlatRenderer();
        if (surface == null) return false;

        Transform space = GetBoundsSpace(surface);
        Vector3 size = ScaledLocalSize(surface, space);

        Vector3 axis = Vector3.right;
        if (size.y <= size.x && size.y <= size.z) axis = Vector3.up;
        else if (size.z <= size.x && size.z <= size.y) axis = Vector3.forward;

        Vector3 worldNormal = space.TransformDirection(axis);
        localNormal = transform.InverseTransformDirection(worldNormal).normalized;
        return true;
    }

    private Renderer FindLargestFlatRenderer()
    {
        Renderer best = null;
        float bestArea = 0f;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            Vector3 s = ScaledLocalSize(r, GetBoundsSpace(r));
            float min = Mathf.Min(s.x, s.y, s.z);
            float max = Mathf.Max(s.x, s.y, s.z);
            float mid = s.x + s.y + s.z - min - max;
            float area = max * mid;
            if (area > bestArea) { bestArea = area; best = r; }
        }

        return best;
    }

    // SkinnedMeshRenderer.localBounds is expressed relative to its root bone.
    private static Transform GetBoundsSpace(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr && smr.rootBone != null) return smr.rootBone;
        return r.transform;
    }

    private static Vector3 ScaledLocalSize(Renderer r, Transform space)
    {
        Vector3 s = Vector3.Scale(r.localBounds.size, space.lossyScale);
        return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    /// <summary>World-space bounds of every enabled renderer on this item.</summary>
    private Bounds GetVisualBounds()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (hasBounds) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; hasBounds = true; }
        }

        if (!hasBounds) bounds.extents = Vector3.one * 0.05f;
        return bounds;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_cameraAnchor == null) return;

        // Preview of the zoom camera frustum from the anchor (approximate player FOV, 16:9 aspect).
        Gizmos.color = Color.cyan;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(_cameraAnchor.position - _cameraAnchor.forward * _anchorPullback, _cameraAnchor.rotation, Vector3.one);
        Gizmos.DrawFrustum(Vector3.zero, 60f, 0.3f, 0.01f, 16f / 9f);
        Gizmos.matrix = previous;
    }
#endif
}
