using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Trailer free-fly camera. Takes over the player's Cinemachine view with a runtime
/// <see cref="CinemachineCamera"/> at priority 1000 (above the death camera's 100), so the player
/// rig is never touched and turning it off hands control straight back.
/// Runs on unscaled time so it keeps working while the game is slowed or frozen.
/// Controls: mouse look, WASD move, Q/E down/up, Shift x4, Alt x0.25, scroll = speed,
/// Z/C roll, R reset roll, Ctrl+Keypad1-4 save bookmark, Keypad1-4 recall, Keypad5 dolly 1 → 2.
/// </summary>
public class TrailerFreeCam : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int    VcamPriority       = 1000;
    private const int    BookmarkSlots      = 4;
    private const string BookmarkKeyPrefix  = "trailer.cam.bookmark.";
    private const float  LookSensitivity    = 2f;
    private const float  RollSpeed          = 30f;   // degrees / second
    private const float  MinSpeed           = 0.25f;
    private const float  MaxSpeed           = 50f;

    [Serializable]
    private struct Bookmark
    {
        public Vector3 position;
        public float   yaw;
        public float   pitch;
        public float   roll;
        public float   fov;
    }

    // ── Settings ──────────────────────────────────────────────────────────────
    [SerializeField, Min(0.1f)] private float _moveSpeed = 3f;
    [SerializeField, Range(0f, 0.95f)] private float _smoothing = 0.8f;
    [SerializeField, Range(10f, 120f)] private float _fieldOfView = 60f;
    [SerializeField, Min(0.5f)] private float _dollyDuration = 6f;

    [Tooltip("While the free cam is active, show the local player's full body (head + body arms) like the " +
             "death/cosmetics third-person view, and hide the floating first-person arms and held item.")]
    [SerializeField] private bool _showPlayerBody = true;

    private const string FirstPersonArmsPath = "CinemachineCamera/Arms_Socket/Player_Arms";

    // ── Runtime state ─────────────────────────────────────────────────────────
    private CinemachineCamera _vcam;
    private CinemachineBrain  _brain;
    private CinemachineBlendDefinition _savedBlend;
    private bool _savedIgnoreTimeScale;
    private bool _savedCanControl;
    private bool _savedCanInteract;
    private CursorLockMode _savedCursorLock;
    private bool _savedCursorVisible;
    private bool _isActive;

    // Third-person body visuals
    private bool _bodyVisualsApplied;
    private PlayerInstance _bodyPlayer;
    private GameObject _hiddenFirstPersonArms;
    private readonly List<Renderer> _hiddenRenderers = new();

    // Target pose (driven by input) and actual pose (smoothed toward the target).
    private Vector3 _targetPos;
    private float _yaw, _pitch, _roll;
    private Vector3 _pos;
    private Quaternion _rot = Quaternion.identity;
    private float _fov;

    // Dolly
    private bool _dollyPlaying;
    private float _dollyTime;
    private Bookmark _dollyFrom, _dollyTo;

    /// <summary>Raised with a short human-readable status message (for the console status line).</summary>
    public event Action<string> StatusChanged;

    /// <summary>True while the free camera owns the view.</summary>
    public bool IsActive => _isActive;

    /// <summary>Base fly speed in m/s (scroll wheel scales it).</summary>
    public float MoveSpeed
    {
        get => _moveSpeed;
        set => _moveSpeed = Mathf.Clamp(value, MinSpeed, MaxSpeed);
    }

    /// <summary>Pose smoothing, 0 (none) to 0.95 (very floaty).</summary>
    public float Smoothing
    {
        get => _smoothing;
        set => _smoothing = Mathf.Clamp(value, 0f, 0.95f);
    }

    /// <summary>Vertical field of view in degrees.</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = Mathf.Clamp(value, 10f, 120f);
    }

    /// <summary>Seconds (unscaled) for the bookmark A → B dolly move.</summary>
    public float DollyDuration
    {
        get => _dollyDuration;
        set => _dollyDuration = Mathf.Clamp(value, 0.5f, 60f);
    }

    /// <summary>True while a dolly move is playing.</summary>
    public bool IsDollyPlaying => _dollyPlaying;

    /// <summary>
    /// Show the local player's full body (head + body arms) while flying, like the death view.
    /// Applies immediately if the free cam is already active.
    /// </summary>
    public bool ShowPlayerBody
    {
        get => _showPlayerBody;
        set
        {
            _showPlayerBody = value;
            if (!_isActive) return;
            if (value) ApplyBodyVisuals(PlayerInstance.Instance);
            else RestoreBodyVisuals();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Unity messages
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDisable() => SetActive(false);

    private void OnDestroy()
    {
        SetActive(false);
        if (_vcam != null) Destroy(_vcam.gameObject);
    }

    private void Update()
    {
        if (!_isActive) return;

        if (_vcam == null || PlayerInstance.Instance == null)
        {
            SetActive(false);
            return;
        }

        float dt = Time.unscaledDeltaTime;

        if (!TrailerConsoleUI.IsOpen)
            HandleInput(dt);

        if (_dollyPlaying)
            TickDolly(dt);
        else
            ApplySmoothing(dt);

        _vcam.transform.SetPositionAndRotation(_pos, _rot);
        _vcam.Lens.FieldOfView = _fov;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enter / exit
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Turns the free camera on or off.</summary>
    public void SetActive(bool active)
    {
        if (active == _isActive) return;
        if (active) Enter();
        else Exit();
    }

    private void Enter()
    {
        var player = PlayerInstance.Instance;
        if (player == null) { Post("No local player"); return; }

        Camera main = Camera.main;
        if (main == null) { Post("No main camera"); return; }

        if (_vcam == null)
        {
            var go = new GameObject("[TrailerFreeCam]");
            _vcam = go.AddComponent<CinemachineCamera>();
        }

        // Start exactly where the player is looking.
        Transform mt = main.transform;
        _pos = _targetPos = mt.position;
        Vector3 e = mt.rotation.eulerAngles;
        _pitch = NormalizeAngle(e.x);
        _yaw   = e.y;
        _roll  = 0f;
        _rot   = TargetRotation();
        _fov   = _fieldOfView = main.fieldOfView;

        _vcam.gameObject.SetActive(true);
        _vcam.transform.SetPositionAndRotation(_pos, _rot);
        _vcam.Lens = LensSettings.FromCamera(main);
        _vcam.Lens.FieldOfView = _fov;
        _vcam.Priority = VcamPriority;

        _brain = main.GetComponent<CinemachineBrain>();
        if (_brain != null)
        {
            _savedBlend = _brain.DefaultBlend;
            _savedIgnoreTimeScale = _brain.IgnoreTimeScale;
            _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            _brain.IgnoreTimeScale = true;
        }

        _savedCanControl  = player.CanControl;
        _savedCanInteract = player.PlayerInteractionController != null && player.PlayerInteractionController.CanInteract;
        _savedCursorLock    = Cursor.lockState;
        _savedCursorVisible = Cursor.visible;

        var movement = player.GetComponent<PlayerMovementController>();
        if (movement != null) movement.SetCanControl(false);
        player.SetCanInteract(false);

        ApplyCursor(CursorLockMode.Locked, false);

        if (_showPlayerBody)
            ApplyBodyVisuals(player);

        _isActive = true;
        _dollyPlaying = false;
        Post("Free cam ON");
    }

    private void Exit()
    {
        _isActive = false;
        _dollyPlaying = false;

        // Revert body visuals first (skipped for the head/body if the player died — the ragdoll owns them).
        RestoreBodyVisuals();

        if (_vcam != null)
        {
            _vcam.Priority = 0;
            _vcam.gameObject.SetActive(false);
        }

        if (_brain != null)
        {
            _brain.DefaultBlend = _savedBlend;
            _brain.IgnoreTimeScale = _savedIgnoreTimeScale;
            _brain = null;
        }

        var player = PlayerInstance.Instance;
        bool playerDead = player != null && player.PlayerHealth != null && player.PlayerHealth.IsDead;

        // If the player died while flying, the death flow owns control/interact state.
        if (player != null && !playerDead)
        {
            if (_savedCanControl)
            {
                var movement = player.GetComponent<PlayerMovementController>();
                if (movement != null) movement.SetCanControl(true);
            }
            if (_savedCanInteract)
                player.SetCanInteract(true);
        }

        if (!playerDead)
            ApplyCursor(_savedCursorLock, _savedCursorVisible);

        Post("Free cam OFF");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Third-person body visuals
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="PlayerThirdPersonView.Enter"/> (minus the FaceCamera): restores the head
    /// bone and full body rendering via <see cref="PlayerAnimationController.EnterThirdPersonPreview"/>
    /// (the same restore the ragdoll does on death), and hides the first-person arms plus the held
    /// item, which follows the FP arm socket and would otherwise float in front of the face.
    /// Held-item renderers are disabled (never SetActive — it's a live NetworkObject).
    /// </summary>
    private void ApplyBodyVisuals(PlayerInstance player)
    {
        if (_bodyVisualsApplied || player == null) return;
        if (player.PlayerHealth != null && player.PlayerHealth.IsDead) return; // ragdoll already shows the body

        _bodyPlayer = player;
        _bodyVisualsApplied = true;

        player.PlayerAnimationController?.EnterThirdPersonPreview();

        Transform fpArms = player.transform.Find(FirstPersonArmsPath);
        if (fpArms != null && fpArms.gameObject.activeSelf)
        {
            fpArms.gameObject.SetActive(false);
            _hiddenFirstPersonArms = fpArms.gameObject;
        }

        var pickup = player.PlayerPickupController;
        if (pickup != null && pickup.HeldObject != null)
        {
            foreach (Renderer r in pickup.HeldObject.GetComponentsInChildren<Renderer>(true))
                HideRenderer(r);
        }

        // Player-root line renderers such as the throw arc.
        foreach (LineRenderer line in player.GetComponentsInChildren<LineRenderer>())
            HideRenderer(line);
    }

    private void HideRenderer(Renderer r)
    {
        if (r == null || !r.enabled || _hiddenRenderers.Contains(r)) return;
        r.enabled = false;
        _hiddenRenderers.Add(r);
    }

    private void RestoreBodyVisuals()
    {
        if (!_bodyVisualsApplied) return;
        _bodyVisualsApplied = false;

        var player = _bodyPlayer;
        _bodyPlayer = null;

        foreach (Renderer r in _hiddenRenderers)
            if (r != null) r.enabled = true;
        _hiddenRenderers.Clear();

        if (_hiddenFirstPersonArms != null)
            _hiddenFirstPersonArms.SetActive(true);
        _hiddenFirstPersonArms = null;

        // If the player died meanwhile, the ragdoll owns the head/body state — don't zero the head.
        bool dead = player != null && player.PlayerHealth != null && player.PlayerHealth.IsDead;
        if (player != null && !dead)
            player.PlayerAnimationController?.ExitThirdPersonPreview();
    }

    /// <summary>
    /// Sets the cursor, or (while the Trailer Console menu is open and owns the cursor) updates the
    /// state the menu will restore when it closes.
    /// </summary>
    private static void ApplyCursor(CursorLockMode lockMode, bool visible)
    {
        if (TrailerConsoleUI.IsOpen)
        {
            TrailerConsoleUI.SetCursorRestoreState(lockMode, visible);
            return;
        }

        Cursor.lockState = lockMode;
        Cursor.visible   = visible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input
    // ─────────────────────────────────────────────────────────────────────────

    private void HandleInput(float dt)
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // Bookmarks: Ctrl+Keypad1-4 save, Keypad1-4 recall, Keypad5 dolly.
        for (int slot = 1; slot <= BookmarkSlots; slot++)
        {
            if (!Input.GetKeyDown(KeyCode.Keypad0 + slot)) continue;
            if (ctrl) SaveBookmark(slot);
            else RecallBookmark(slot);
        }
        if (Input.GetKeyDown(KeyCode.Keypad5))
            PlayDolly();

        // Look
        _yaw   += Input.GetAxisRaw("Mouse X") * LookSensitivity;
        _pitch -= Input.GetAxisRaw("Mouse Y") * LookSensitivity;
        _pitch  = Mathf.Clamp(_pitch, -89f, 89f);

        // Roll
        if (Input.GetKey(KeyCode.Z)) _roll += RollSpeed * dt;
        if (Input.GetKey(KeyCode.C)) _roll -= RollSpeed * dt;
        if (Input.GetKeyDown(KeyCode.R)) _roll = 0f;

        // Speed
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            MoveSpeed *= scroll > 0f ? 1.2f : 1f / 1.2f;

        // Move
        Vector3 input = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) input.z += 1f;
        if (Input.GetKey(KeyCode.S)) input.z -= 1f;
        if (Input.GetKey(KeyCode.D)) input.x += 1f;
        if (Input.GetKey(KeyCode.A)) input.x -= 1f;
        if (Input.GetKey(KeyCode.E)) input.y += 1f;
        if (Input.GetKey(KeyCode.Q)) input.y -= 1f;

        if (input.sqrMagnitude > 0f)
        {
            if (_dollyPlaying) { _dollyPlaying = false; Post("Dolly cancelled"); SnapTargetToActual(); }

            float speed = _moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= 4f;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) speed *= 0.25f;

            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 move = look * new Vector3(input.x, 0f, input.z) + Vector3.up * input.y;
            _targetPos += move.normalized * (speed * dt);
        }
    }

    private void ApplySmoothing(float dt)
    {
        // Frame-rate independent exponential smoothing (factor defined per 60 fps frame).
        float t = _smoothing <= 0f ? 1f : 1f - Mathf.Pow(_smoothing, dt * 60f);
        _pos = Vector3.Lerp(_pos, _targetPos, t);
        _rot = Quaternion.Slerp(_rot, TargetRotation(), t);
        _fov = Mathf.Lerp(_fov, _fieldOfView, t);
    }

    private Quaternion TargetRotation() => Quaternion.Euler(_pitch, _yaw, _roll);

    private void SnapTargetToActual()
    {
        _targetPos = _pos;
        Vector3 e = _rot.eulerAngles;
        _pitch = NormalizeAngle(e.x);
        _yaw   = e.y;
        _roll  = NormalizeAngle(e.z);
        _fieldOfView = _fov;
    }

    private static float NormalizeAngle(float a)
    {
        a %= 360f;
        return a > 180f ? a - 360f : a < -180f ? a + 360f : a;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bookmarks
    // ─────────────────────────────────────────────────────────────────────────

    private static string BookmarkKey(int slot) => BookmarkKeyPrefix + slot;

    private static bool IsValidSlot(int slot) => slot >= 1 && slot <= BookmarkSlots;

    /// <summary>True if a bookmark is stored in <paramref name="slot"/> (1-4).</summary>
    public bool HasBookmark(int slot) => IsValidSlot(slot) && PlayerPrefs.HasKey(BookmarkKey(slot));

    /// <summary>Saves the current free cam pose to <paramref name="slot"/> (1-4), persisted in PlayerPrefs.</summary>
    public void SaveBookmark(int slot)
    {
        if (!IsValidSlot(slot)) return;
        if (!_isActive) { Post("Enable free cam first"); return; }

        var b = new Bookmark { position = _targetPos, yaw = _yaw, pitch = _pitch, roll = _roll, fov = _fieldOfView };
        PlayerPrefs.SetString(BookmarkKey(slot), JsonUtility.ToJson(b));
        PlayerPrefs.Save();
        Post($"Saved bookmark {slot}");
    }

    /// <summary>Snaps the free cam to the bookmark in <paramref name="slot"/>. Returns false if unavailable.</summary>
    public bool RecallBookmark(int slot)
    {
        if (!_isActive) { Post("Enable free cam first"); return false; }
        if (!TryLoadBookmark(slot, out Bookmark b)) { Post($"Bookmark {slot} is empty"); return false; }

        _dollyPlaying = false;
        ApplyBookmarkInstant(b);
        Post($"Bookmark {slot}");
        return true;
    }

    private bool TryLoadBookmark(int slot, out Bookmark b)
    {
        b = default;
        if (!HasBookmark(slot)) return false;
        try
        {
            b = JsonUtility.FromJson<Bookmark>(PlayerPrefs.GetString(BookmarkKey(slot)));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ApplyBookmarkInstant(Bookmark b)
    {
        _pos = _targetPos = b.position;
        _yaw = b.yaw; _pitch = b.pitch; _roll = b.roll;
        _rot = TargetRotation();
        _fov = _fieldOfView = b.fov > 0f ? b.fov : _fieldOfView;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dolly
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a SmoothStep move from bookmark <paramref name="fromSlot"/> to <paramref name="toSlot"/>
    /// over <see cref="DollyDuration"/> unscaled seconds. Any movement input cancels it.
    /// </summary>
    public void PlayDolly(int fromSlot = 1, int toSlot = 2)
    {
        if (!_isActive) { Post("Enable free cam first"); return; }
        if (!TryLoadBookmark(fromSlot, out _dollyFrom)) { Post($"Bookmark {fromSlot} is empty"); return; }
        if (!TryLoadBookmark(toSlot, out _dollyTo)) { Post($"Bookmark {toSlot} is empty"); return; }

        ApplyBookmarkInstant(_dollyFrom);
        _dollyTime = 0f;
        _dollyPlaying = true;
        Post($"Dolly {fromSlot} -> {toSlot} ({_dollyDuration:0.#}s)");
    }

    private void TickDolly(float dt)
    {
        _dollyTime += dt;
        float t = Mathf.Clamp01(_dollyTime / Mathf.Max(0.01f, _dollyDuration));
        float s = Mathf.SmoothStep(0f, 1f, t);

        _pos = Vector3.Lerp(_dollyFrom.position, _dollyTo.position, s);
        _rot = Quaternion.Slerp(
            Quaternion.Euler(_dollyFrom.pitch, _dollyFrom.yaw, _dollyFrom.roll),
            Quaternion.Euler(_dollyTo.pitch, _dollyTo.yaw, _dollyTo.roll), s);
        _fov = Mathf.Lerp(_dollyFrom.fov, _dollyTo.fov, s);

        if (t >= 1f)
        {
            _dollyPlaying = false;
            SnapTargetToActual();
            Post("Dolly done");
        }
    }

    private void Post(string message) => StatusChanged?.Invoke(message);
}
