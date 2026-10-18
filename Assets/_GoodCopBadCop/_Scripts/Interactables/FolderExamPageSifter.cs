using System.Collections.Generic;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local-only "sifting" for exam pages filed in a folder. While the local player is in held-item
/// zoom (inspect mode) on THIS folder, the exam page under the cursor is lifted toward the camera
/// so it sits in front of the other pages. Moving off it (or leaving zoom) eases every page back
/// to its slot.
///
/// Purely visual: drives <see cref="SocketFollow.VisualLocalOffset"/> on each filed page, which is
/// never networked and is reset whenever the page's follow target changes. Hit testing uses each
/// page's root <see cref="BoxCollider"/> geometry directly (as a flat rectangle on its mid-plane),
/// because filed pages' colliders are disabled while the folder is held.
/// Gamepad: the right stick acts as a cursor offset from screen centre, matching zoom panning.
/// </summary>
[RequireComponent(typeof(FolderController))]
public class FolderExamPageSifter : MonoBehaviour
{
    [Tooltip("How far (world units) the hovered page is lifted out of the stack toward the camera.")]
    [SerializeField, Min(0f)] private float _liftDistance = 0.01f;

    [Tooltip("Exponential smoothing speed for the lift/return animation. 0 = snap.")]
    [SerializeField, Min(0f)] private float _liftSmoothing = 18f;

    [Tooltip("Seconds between refreshes of the filed-page list while zoomed.")]
    [SerializeField, Min(0.05f)] private float _pageRefreshInterval = 0.5f;

    private class PageState
    {
        public ExamPage Page;
        public SocketFollow Follow;
        public BoxCollider Box;
        public Transform Slot;
        public float Lift;
    }

    private FolderController _folder;
    private readonly List<PageState> _pages = new List<PageState>();
    private readonly List<ExamPage> _filedScratch = new List<ExamPage>();
    private bool _wasZoomed;
    private float _nextRefreshTime;

    private void Awake()
    {
        _folder = GetComponent<FolderController>();
    }

    private void OnDisable()
    {
        // Stowed / despawned: snap everything back immediately.
        foreach (PageState state in _pages)
            ResetPage(state);
        _pages.Clear();
        _wasZoomed = false;
    }

    private void Update()
    {
        bool zoomed = IsLocallyZoomedOnThisFolder();

        if (zoomed && (!_wasZoomed || Time.unscaledTime >= _nextRefreshTime))
            RefreshPages();
        _wasZoomed = zoomed;

        if (_pages.Count == 0) return;

        PageState hovered = zoomed ? FindHoveredPage() : null;

        Camera cam = PlayerInstance.Instance != null ? PlayerInstance.Instance.GetCamera() : null;
        float t = _liftSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-_liftSmoothing * Time.unscaledDeltaTime);
        bool anyLifted = false;

        for (int i = _pages.Count - 1; i >= 0; i--)
        {
            PageState state = _pages[i];
            if (!IsStillFiled(state))
            {
                ResetPage(state);
                _pages.RemoveAt(i);
                continue;
            }

            float target = state == hovered ? _liftDistance : 0f;
            state.Lift = Mathf.Lerp(state.Lift, target, t);
            if (Mathf.Abs(state.Lift - target) < 0.00005f) state.Lift = target;

            if (state.Lift <= 0f)
            {
                state.Follow.VisualLocalOffset = Vector3.zero;
                continue;
            }

            anyLifted = true;
            state.Follow.VisualLocalOffset = GetThinAxisLocal(state) * (state.Lift * GetSignTowardCamera(state, cam));
        }

        // Nothing left to animate once zoom is closed and every page is home.
        if (!zoomed && !anyLifted)
            _pages.Clear();
    }

    private bool IsLocallyZoomedOnThisFolder()
    {
        if (DiegeticViewController.Current is not HeldItemZoomView zoom || !zoom.IsZoomed) return false;
        HeldItemZoomable target = zoom.CurrentTarget;
        return target != null && target.Pickable == _folder;
    }

    private void RefreshPages()
    {
        _nextRefreshTime = Time.unscaledTime + _pageRefreshInterval;

        _filedScratch.Clear();
        _filedScratch.AddRange(_folder.GetFiledExamPages());

        foreach (ExamPage page in _filedScratch)
        {
            if (page == null || _pages.Exists(s => s.Page == page)) continue;

            SocketFollow follow = page.GetComponent<SocketFollow>();
            BoxCollider box = page.GetComponent<BoxCollider>();
            if (follow == null || box == null) continue;

            _pages.Add(new PageState { Page = page, Follow = follow, Box = box, Slot = follow.Target });
        }
    }

    private static bool IsStillFiled(PageState state)
    {
        return state.Page != null
            && state.Follow != null
            && state.Follow.enabled
            && state.Follow.Target != null
            && state.Follow.Target == state.Slot;
    }

    private static void ResetPage(PageState state)
    {
        state.Lift = 0f;
        // Only clear if the page is still on the slot we lifted it on — a retarget already reset it.
        if (state.Follow != null && state.Follow.Target == state.Slot)
            state.Follow.VisualLocalOffset = Vector3.zero;
    }

    // ─── Hit testing ─────────────────────────────────────────────────────────

    private PageState FindHoveredPage()
    {
        if (UIController.Instance != null && UIController.Instance.IsPaused) return null;

        Camera cam = PlayerInstance.Instance != null ? PlayerInstance.Instance.GetCamera() : null;
        if (cam == null) return null;

        Ray ray = cam.ScreenPointToRay(ReadPointerScreenPosition());

        PageState best = null;
        float bestDistance = float.MaxValue;
        foreach (PageState state in _pages)
        {
            if (!IsStillFiled(state)) continue;
            if (TryHitPageRect(state, ray, out float distance) && distance < bestDistance)
            {
                bestDistance = distance;
                best = state;
            }
        }
        return best;
    }

    /// <summary>
    /// Ray vs the page box's mid-plane rectangle (thin axis flattened), tested at the page's
    /// ORIGINAL slot position (the visual lift is cancelled out). Distances along the ray order
    /// overlapping pages by their natural stack height, so a lifted page never blocks a page that
    /// sits above it in the normal stack.
    /// </summary>
    private static bool TryHitPageRect(PageState state, Ray ray, out float distance)
    {
        distance = 0f;

        // Shifting the ray by +lift is equivalent to moving the page back down by -lift.
        Vector3 worldLift = state.Follow.transform.rotation * state.Follow.VisualLocalOffset;
        ray = new Ray(ray.origin + worldLift, ray.direction);

        Transform t = state.Box.transform;
        Vector3 size = state.Box.size;
        int thin = ThinAxisIndex(size);

        Vector3 localNormal = Vector3.zero;
        localNormal[thin] = 1f;

        Vector3 planePoint = t.TransformPoint(state.Box.center);
        Vector3 planeNormal = t.TransformDirection(localNormal);
        Plane plane = new Plane(planeNormal, planePoint);
        if (!plane.Raycast(ray, out distance) || distance <= 0f) return false;

        Vector3 local = t.InverseTransformPoint(ray.GetPoint(distance)) - state.Box.center;
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == thin) continue;
            if (Mathf.Abs(local[axis]) > size[axis] * 0.5f) return false;
        }
        return true;
    }

    private static int ThinAxisIndex(Vector3 size)
    {
        Vector3 s = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        if (s.x <= s.y && s.x <= s.z) return 0;
        return s.y <= s.z ? 1 : 2;
    }

    private static Vector3 GetThinAxisLocal(PageState state)
    {
        Vector3 axis = Vector3.zero;
        axis[ThinAxisIndex(state.Box.size)] = 1f;
        return axis;
    }

    /// <summary>+1 if the page's thin axis points toward the camera, else -1 (so the lift always comes out of the stack).</summary>
    private static float GetSignTowardCamera(PageState state, Camera cam)
    {
        if (cam == null) return 1f;
        Transform t = state.Page.transform;
        Vector3 normal = t.rotation * GetThinAxisLocal(state);
        return Vector3.Dot(normal, cam.transform.position - t.position) >= 0f ? 1f : -1f;
    }

    private static Vector2 ReadPointerScreenPosition()
    {
        Gamepad gamepad = Gamepad.current;
        if (ActiveInputDeviceTracker.IsGamepad && gamepad != null)
        {
            Vector2 stick = Vector2.ClampMagnitude(gamepad.rightStick.ReadValue(), 1f);
            return new Vector2(Screen.width * 0.5f * (1f + stick.x), Screen.height * 0.5f * (1f + stick.y));
        }
        return Input.mousePosition;
    }
}
