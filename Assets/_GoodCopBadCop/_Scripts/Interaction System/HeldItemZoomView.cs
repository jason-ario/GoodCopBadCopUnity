using GoodCopBadCop.Input;
using GoodCopBadCop.Settings;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// "Zoom mode": close-up view of the document the local player is holding (ID card,
/// application, newspaper, daily fax, folder, ...). Built on <see cref="DiegeticViewController"/>
/// so it shares the mini fridge / tool locker behaviour: Cinemachine blend in, movement +
/// interaction locked, cursor shown, cursor-driven panning, Q / back button to exit.
///
/// Differences from the scene-placed diegetic views:
///  • Lives on the Player prefab (one shared camera) and opens itself from
///    <see cref="GameAction.ZoomHeldItem"/> (F by default, rebindable) or gamepad R3 while the
///    held item has a <see cref="HeldItemZoomable"/>.
///  • Documents (<see cref="HeldItemZoomable.InspectWhileZoomed"/>): zoom behaves exactly as if
///    LMB were held for its whole duration (<see cref="PlayerPickupController.BeginHeldUseFromZoom"/>)
///    and releases it on close. The camera sits on the line from the player's eye to the
///    document and follows it every frame, so the close-up shows the full inspect animation
///    exactly as the player would see it — just closer.
///  • Anchored items (folder "Zoom Cam Pos"): camera pose is locked to the item. Closed folders
///    are opened first.
///  • Arms stay visible by default and both animators are forced to always animate, so
///    hand-driven items can't freeze mid-transition when the arms leave the close-up frame.
///  • A local-only vignette (runtime URP Volume, same approach as DamageVignetteView) fades
///    in on open and out on close.
///
/// Auto-closes when the item leaves the hand (drop, stow, throw, despawn), when it stops being
/// zoomable (folder closed), or when a cutscene starts.
/// </summary>
[DefaultExecutionOrder(-100)] // Write the camera pose before CinemachineBrain's LateUpdate.
public class HeldItemZoomView : DiegeticViewController
{
    [Header("Held Item Zoom")]
    [Tooltip("Also toggle with the gamepad right stick press.")]
    [SerializeField] private bool _allowGamepadToggle = true;

    [Tooltip("Near clip plane used by the zoom camera once the blend has finished, so very close documents don't get cut off. " +
             "During the blend in/out the player camera's near plane is kept, because an animating near plane makes " +
             "Forward+ light clustering flicker.")]
    [SerializeField, Min(0.001f)] private float _nearClipPlane = 0.01f;

    [Header("Exit Blend")]
    [Tooltip("Blend used only when leaving zoom (overrides the brain's default for this transition). " +
             "HardOut leaves the close-up immediately and eases into the player view, so the camera pulls " +
             "back together with the lowering document instead of lingering on it.")]
    [SerializeField] private CinemachineBlendDefinition _exitBlend =
        new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.HardOut, 0.45f);

    // Exit blends per zoom camera (one per player instance): only applied when blending from that
    // zoom camera back to its own player camera, so e.g. a cutscene closing zoom keeps its own blend.
    private static readonly System.Collections.Generic.Dictionary<ICinemachineCamera, (ICinemachineCamera to, CinemachineBlendDefinition blend)>
        s_ExitBlends = new();
    private static CinemachineCore.GetBlendOverrideDelegate s_PreviousBlendOverride;

    private static void EnsureBlendOverrideInstalled()
    {
        CinemachineCore.GetBlendOverrideDelegate handler = OverrideExitBlend;
        if (CinemachineCore.GetBlendOverride == handler) return;
        s_PreviousBlendOverride = CinemachineCore.GetBlendOverride;
        CinemachineCore.GetBlendOverride = handler;
    }

    private static CinemachineBlendDefinition OverrideExitBlend(
        ICinemachineCamera fromVcam, ICinemachineCamera toVcam,
        CinemachineBlendDefinition defaultBlend, Object owner)
    {
        if (s_PreviousBlendOverride != null)
            defaultBlend = s_PreviousBlendOverride(fromVcam, toVcam, defaultBlend, owner);

        if (fromVcam != null && s_ExitBlends.TryGetValue(fromVcam, out var exit)
            && (exit.to == null || ReferenceEquals(exit.to, toVcam)))
            return exit.blend;

        return defaultBlend;
    }

    [Header("Vignette")]
    [Tooltip("Fade a vignette in while zoomed (local-only runtime URP Volume).")]
    [SerializeField] private bool _useVignette = true;

    [SerializeField] private Color _vignetteColor = Color.black;

    [SerializeField, Range(0f, 1f)] private float _vignetteIntensity = 0.4f;

    [SerializeField, Range(0.01f, 1f)] private float _vignetteSmoothness = 0.45f;

    [Tooltip("Seconds to fade the vignette in when zoom opens.")]
    [SerializeField, Min(0f)] private float _vignetteFadeIn = 0.35f;

    [Tooltip("Seconds to fade the vignette out when zoom closes.")]
    [SerializeField, Min(0f)] private float _vignetteFadeOut = 0.25f;

    // Below DamageVignetteView (100) so hit/drunk pulses still layer on top.
    private const float VignetteVolumePriority = 90f;

    private Volume _vignetteVolume;
    private VolumeProfile _vignetteProfile;
    private Vignette _vignette;

    private PlayerPickupController _pickup;
    private PlayerInteractionController _interaction;
    private PlayerAnimationController _animation;
    private Transform _eye;
    private CinemachineCamera _playerVcam;
    private bool _nearTightened;
    private PlayerInstance _cutscenePlayer;

    private HeldItemZoomable _target;
    private bool _holdingUse;

    // Anchor framing (item-local pose).
    private Vector3 _localCameraPosition;
    private Quaternion _localCameraRotation;

    // Eye framing (item-local focus + distance along the eye → focus line).
    private Vector3 _localFocus;
    private float _eyeDistance;

    // Surface framing (item-local document normal, side chosen toward the eye with hysteresis).
    private bool _useSurfaceNormal;
    private Vector3 _localNormal;
    private float _normalSign = 1f;

    private float _framedHalfSize;
    private Vector3 _currentPan;

    /// <summary>True while zoom mode is open on this client.</summary>
    public bool IsZoomed => IsActive;

    protected override bool HidePlayerArms => _target != null && _target.HidePlayerArms;

    // Zoom is a camera move only — it must not change scene lighting.
    protected override bool ForcePlayerLightOn => false;

    // Panning is driven here in LateUpdate so it composes with the follow.
    protected override bool SuppressCameraMovement => true;

    private void Awake()
    {
        _pickup = GetComponentInParent<PlayerPickupController>();
        _interaction = GetComponentInParent<PlayerInteractionController>();
        _animation = GetComponentInParent<PlayerAnimationController>();
        if (_pickup != null)
            _eye = _pickup.transform.Find("CinemachineCamera");
        if (_eye != null)
            _playerVcam = _eye.GetComponent<CinemachineCamera>();

        if (ViewCamera != null)
        {
            // Blend out from a snapshot of the zoom camera's last pose instead of a camera that
            // is still tied to the (now lowering) item — detaches the view at the start of the exit.
            ViewCamera.BlendHint |= CinemachineCore.BlendHints.FreezeWhenBlendingOut;
            ViewCamera.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        // Guard against teardown order on quit / scene unload (Close touches UIController).
        if (IsActive && UIController.Instance != null) Close();
        if (_vignetteVolume != null) _vignetteVolume.weight = 0f;
    }

    private void OnDestroy()
    {
        if (ViewCamera != null) s_ExitBlends.Remove(ViewCamera);
        if (_vignetteVolume != null) Destroy(_vignetteVolume.gameObject);
        if (_vignetteProfile != null) Destroy(_vignetteProfile);
    }

    // ─── Vignette ────────────────────────────────────────────────────────────

    /// <summary>Lazily builds the runtime vignette Volume (local player only, first zoom).</summary>
    private void EnsureVignetteVolume()
    {
        if (_vignetteVolume != null) return;

        var root = new GameObject("Held Item Zoom Vignette");
        // Match the base post-processing volume's layer so it falls inside the camera's Volume Mask.
        var baseAnchor = FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();
        if (baseAnchor != null) root.layer = baseAnchor.gameObject.layer;
        root.transform.SetParent(transform, false);

        _vignetteProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _vignetteProfile.name = "Held Item Zoom Vignette (Runtime)";
        _vignette = _vignetteProfile.Add<Vignette>();

        _vignetteVolume = root.AddComponent<Volume>();
        _vignetteVolume.isGlobal = true;
        _vignetteVolume.priority = VignetteVolumePriority;
        _vignetteVolume.weight = 0f;
        _vignetteVolume.sharedProfile = _vignetteProfile;
    }

    private void ApplyVignetteSettings()
    {
        Color c = _vignetteColor;
        c.a = 1f;
        _vignette.active = true;
        _vignette.color.Override(c);
        _vignette.intensity.Override(_vignetteIntensity);
        _vignette.smoothness.Override(_vignetteSmoothness);
    }

    /// <summary>Fades the vignette weight toward 1 while zoomed and back to 0 after closing.</summary>
    private void UpdateVignette()
    {
        if (_vignetteVolume == null) return;

        float target = IsActive && _useVignette ? 1f : 0f;
        float duration = target > _vignetteVolume.weight ? _vignetteFadeIn : _vignetteFadeOut;
        _vignetteVolume.weight = duration <= 0f
            ? target
            : Mathf.MoveTowards(_vignetteVolume.weight, target, Time.unscaledDeltaTime / duration);
    }

    // ─── Input ───────────────────────────────────────────────────────────────

    protected override void Update()
    {
        if (!IsActive)
        {
            // Return after opening so the same key press isn't read as "close" this frame.
            if (TogglePressed()) TryOpen();
            return;
        }

        base.Update();
    }

    private bool TogglePressed()
    {
        if (RebindableInput.GetKeyDown(GameAction.ZoomHeldItem)) return true;
        return _allowGamepadToggle && (Gamepad.current?.rightStickButton.wasPressedThisFrame ?? false);
    }

    /// <summary>
    /// Opens zoom mode on the currently held item if every gate passes. Safe to call from
    /// other systems (e.g. a tutorial prompt). Returns true if the view opened.
    /// </summary>
    public bool TryOpen()
    {
        if (IsActive || _pickup == null || _interaction == null) return false;
        if (!_pickup.IsLocalPlayer) return false;
        if (IsAnyViewActive) return false;
        if (UIController.Instance == null || UIController.Instance.IsPaused) return false;
        if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsInCutscene) return false;
        if (!_pickup.CanPickUpAndPlace || !_interaction.CanInteract) return false;

        PickableObject held = _pickup.HeldObject;
        if (held == null) return false;

        HeldItemZoomable zoomable = held.GetComponent<HeldItemZoomable>();
        if (zoomable == null || !zoomable.CanBeginZoom) return false;

        // Documents already being inspected with LMB are fine — zoom just keeps that use held.
        if (held.IsBeingUsed && !zoomable.InspectWhileZoomed) return false;

        if (ViewCamera == null)
        {
            Debug.LogWarning("[HeldItemZoomView] No view camera assigned.", this);
            return false;
        }

        _target = zoomable;
        _currentPan = Vector3.zero;

        // Opens a closed folder (same as LMB) before the camera moves in.
        zoomable.PrepareForZoom();

        // Use the player camera's lens exactly (FOV, near plane, physical props) so the blend is
        // a pure position/rotation move — no lens values animate. Framing uses the same FOV.
        if (_playerVcam != null) ViewCamera.Lens = _playerVcam.Lens;
        float fov = ViewCamera.Lens.FieldOfView;
        _nearTightened = false;

        _useSurfaceNormal = false;
        if (zoomable.HasCameraAnchor || _eye == null)
            zoomable.ComputeAnchorPose(fov, out _localCameraPosition, out _localCameraRotation, out _framedHalfSize);
        else
        {
            zoomable.ComputeEyeFraming(fov, out _localFocus, out _eyeDistance, out _framedHalfSize);
            _useSurfaceNormal = zoomable.TryGetDocumentNormal(out _localNormal);
            if (_useSurfaceNormal)
            {
                Vector3 n = zoomable.transform.TransformDirection(_localNormal);
                Vector3 toEye = _eye.position - zoomable.transform.TransformPoint(_localFocus);
                _normalSign = Vector3.Dot(n, toEye) >= 0f ? 1f : -1f;
            }
        }

        // Position before activation so Cinemachine blends toward the correct pose from frame one.
        ApplyCameraPose();
        Open(_interaction);
        return true;
    }

    // ─── DiegeticViewController hooks ────────────────────────────────────────

    protected override void OnOpened()
    {
        // Same lock the exam notebook inspection uses: blocks place (RMB) and throw (MMB).
        _pickup.CanPickUpAndPlace = false;

        _animation?.SetForceAlwaysAnimate(true);

        if (_useVignette)
        {
            EnsureVignetteVolume();
            ApplyVignetteSettings();
        }

        // Documents: act exactly as if the player pressed and is holding LMB.
        if (_target != null && _target.InspectWhileZoomed)
        {
            _pickup.BeginHeldUseFromZoom();
            _holdingUse = true;
        }

        _cutscenePlayer = PlayerInstance.Instance;
        if (_cutscenePlayer != null)
            _cutscenePlayer.OnCutsceneStateChanged += HandleCutsceneStateChanged;
    }

    protected override void OnClosed()
    {
        if (_cutscenePlayer != null)
        {
            _cutscenePlayer.OnCutsceneStateChanged -= HandleCutsceneStateChanged;
            _cutscenePlayer = null;
        }

        // Blend out with the player's near plane (runs before the base deactivates the camera).
        // Refresh the camera state now so the FreezeWhenBlendingOut snapshot picks it up — otherwise
        // the frozen exit pose keeps the 0.01 close-up near plane and the lowering arm/document
        // sweeps right across the lens.
        SetViewNearClip(PlayerNearClip);
        _nearTightened = false;
        if (ViewCamera != null)
        {
            s_ExitBlends[ViewCamera] = (_playerVcam, _exitBlend);
            EnsureBlendOverrideInstalled();
            ViewCamera.InternalUpdateCameraState(Vector3.up, -1f);
        }

        // Release immediately. The camera is detached from the item for the blend out via the
        // FreezeWhenBlendingOut hint (see Awake), so the hand lowering can't drag the view.
        ReleaseHeldUse(_target != null ? _target.Pickable : null);
        _animation?.SetForceAlwaysAnimate(false);

        if (_pickup != null)
            _pickup.CanPickUpAndPlace = true;

        _target = null;
    }

    /// <summary>
    /// "Releases LMB". If the document left the hand while zoomed (drop / stow / snatched),
    /// the unequip path already stopped its use; just make sure the inspect bool isn't left on.
    /// </summary>
    private void ReleaseHeldUse(PickableObject item)
    {
        if (!_holdingUse || _pickup == null) return;
        _holdingUse = false;

        bool stillHeld = item != null && _pickup.HeldObject == item;
        _pickup.EndHeldUseFromZoom();

        if (!stillHeld && _pickup.HeldObject == null)
            HeldDocumentInspection.SetInspecting(_pickup, false);
    }

    protected override void OnUpdate()
    {
        if (TogglePressed() || !IsTargetStillValid())
            Close();
    }

    private bool IsTargetStillValid()
    {
        return _target != null
            && _target.CanStayZoomed
            && _pickup != null
            && _pickup.HeldObject != null
            && _pickup.HeldObject == _target.Pickable;
    }

    private void HandleCutsceneStateChanged(bool isInCutscene)
    {
        if (isInCutscene && IsActive) Close();
    }

    // ─── Camera follow + pan ─────────────────────────────────────────────────

    private float PlayerNearClip => _playerVcam != null ? _playerVcam.Lens.NearClipPlane : 0.1f;

    private void SetViewNearClip(float near)
    {
        if (ViewCamera == null) return;
        var lens = ViewCamera.Lens;
        lens.NearClipPlane = near;
        ViewCamera.Lens = lens;
    }

    /// <summary>
    /// Tightens the near plane only once the brain is fully live on the zoom camera, so the
    /// near plane never changes mid-blend. Reads last frame's brain state (this runs first).
    /// </summary>
    private void UpdateNearClip()
    {
        if (_nearTightened) return;

        Camera cam = RaycastCamera;
        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        bool blendDone = brain == null
            || (ReferenceEquals(brain.ActiveVirtualCamera, ViewCamera) && !brain.IsBlending);
        if (!blendDone) return;

        SetViewNearClip(_nearClipPlane);
        _nearTightened = true;
    }

    private void LateUpdate()
    {
        // Runs while closed too so the vignette can fade out.
        UpdateVignette();

        if (!IsActive || _target == null) return;

        UpdateNearClip();

        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        if (!paused)
        {
            float normX = Mathf.Clamp((Input.mousePosition.x / Screen.width) * 2f - 1f, -1f, 1f);
            float normY = Mathf.Clamp((Input.mousePosition.y / Screen.height) * 2f - 1f, -1f, 1f);
            Vector3 targetPan = new Vector3(
                normX * _target.PanX * _framedHalfSize,
                normY * _target.PanY * _framedHalfSize,
                0f);

            float smoothing = _target.PanSmoothing;
            _currentPan = smoothing <= 0f
                ? targetPan
                : Vector3.Lerp(_currentPan, targetPan, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
        }

        ApplyCameraPose();
    }

    private void ApplyCameraPose()
    {
        if (_target == null || ViewCamera == null) return;

        Transform item = _target.transform;
        Vector3 worldPosition;
        Quaternion worldRotation;

        if (_target.HasCameraAnchor || _eye == null)
        {
            worldRotation = item.rotation * _localCameraRotation;
            worldPosition = item.TransformPoint(_localCameraPosition);
        }
        else
        {
            // Move the player's eye toward the document along their line of sight, tracking
            // the document as the inspect animation raises it.
            Vector3 focus = item.TransformPoint(_localFocus);
            Vector3 toEye = _eye.position - focus;
            if (toEye.sqrMagnitude < 0.0001f) toEye = -_eye.forward;
            toEye.Normalize();

            Vector3 viewDir = toEye;
            if (_useSurfaceNormal)
            {
                // Look straight down the paper's normal so the camera is flat with the page.
                // Hysteresis stops the side flipping while the page passes edge-on mid-animation.
                Vector3 n = item.TransformDirection(_localNormal).normalized;
                if (Vector3.Dot(n, toEye) * _normalSign < -0.2f) _normalSign = -_normalSign;
                viewDir = n * _normalSign;
            }

            Vector3 up = _eye.up;
            if (Mathf.Abs(Vector3.Dot(up, viewDir)) > 0.98f) up = _eye.forward;

            worldPosition = focus + viewDir * _eyeDistance;
            worldRotation = Quaternion.LookRotation(-viewDir, up);
        }

        worldPosition += worldRotation * _currentPan;
        ViewCamera.transform.SetPositionAndRotation(worldPosition, worldRotation);
    }
}
