using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Reusable base class for any diegetic first-person view (tool locker, puzzle panels, etc.).
/// Handles the camera, mouse panning, player state (movement, cursor, arms, body) and exit key.
/// Subclasses override <see cref="OnOpened"/>, <see cref="OnClosed"/>, and <see cref="OnUpdate"/>
/// to add their own interaction logic.
/// </summary>
public abstract class DiegeticViewController : MonoBehaviour
{
    [Header("View Camera")]
    [Tooltip("The CinemachineCamera that becomes active while this view is open.")]
    [SerializeField] private CinemachineCamera _viewCamera;

    [Header("Camera Pan")]
    [Tooltip("Maximum horizontal pan distance (world units) from the camera's resting position.")]
    [SerializeField] private float _maxPanX = 0.15f;

    [Tooltip("Maximum vertical pan distance (world units) when the cursor is above centre.")]
    [SerializeField] private float _maxPanUp = 0.05f;

    [Tooltip("Maximum vertical pan distance (world units) when the cursor is below centre.")]
    [SerializeField] private float _maxPanDown = 0.2f;

    [Header("Controls")]
    [Tooltip("Key the player presses to exit this view.")]
    [SerializeField] private KeyCode _exitKey = KeyCode.Q;

    // ─── Protected access for subclasses ─────────────────────────────────────

    /// <summary>Whether this view is currently open.</summary>
    protected bool IsActive { get; private set; }

    /// <summary>
    /// True while ANY diegetic view is open on this client.
    /// Used by external systems (e.g. <see cref="UIController"/>) to suppress
    /// input handling that would conflict with the view's own exit-key logic.
    /// </summary>
    public static bool IsAnyViewActive { get; private set; }

    /// <summary>
    /// The currently active diegetic view instance, or null if none is open.
    /// Use <c>DiegeticViewController.Current?.Close()</c> to dismiss whichever
    /// view is open without knowing its concrete type.
    /// </summary>
    public static DiegeticViewController Current { get; private set; }

    /// <summary>The interaction controller of the player who opened this view.</summary>
    protected PlayerInteractionController Player { get; private set; }

    /// <summary>
    /// The rendering camera (CinemachineBrain) that subclasses should use for
    /// screen-to-world raycasts. This is the camera that actually composites the
    /// active virtual camera, so its perspective matches what the player sees.
    /// </summary>
    protected Camera RaycastCamera => PlayerInstance.Instance?.GetCamera();

    // ─── Private state ────────────────────────────────────────────────────────

    private Quaternion _baseCameraRotation;
    private Vector3 _baseCameraPosition;
    private GameObject _playerArms;
    private GameObject _playerBody;
    private PlayerInstance _playerInstance;

    /// <summary>Cached outdoor/indoor state at the moment this view opened, restored on close.</summary>
    private bool _preOpenLightActive;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens this diegetic view for <paramref name="player"/>.
    /// Activates the view camera, disables player movement and standard interaction,
    /// shows the cursor, and hides the player arms and body mesh.
    /// Calls <see cref="OnOpened"/> after setup is complete.
    /// </summary>
    public void Open(PlayerInteractionController player)
    {
        if (IsActive) return;

        Player = player;
        IsActive = true;
        IsAnyViewActive = true;
        Current = this;

        // Activate the view camera — Cinemachine blends to it automatically.
        if (_viewCamera != null)
        {
            _baseCameraRotation = _viewCamera.transform.rotation;
            _baseCameraPosition = _viewCamera.transform.position;
            _viewCamera.gameObject.SetActive(true);
        }

        // Disable movement and camera look; the view camera is the authority.
        player.playerMovementController.SetCanControl(false);

        // Suppress the standard interaction/reticle system.
        player.SetSuspectCamMode(true);

        // Show cursor so the player can point at objects.
        UIController.Instance.ShowCursor();
        if (ShowBackButton)
            UIController.Instance.ShowBackButton(Close);

        // Hide first-person arms so they don't occlude the view.
        _playerArms = player.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_playerArms != null)
            _playerArms.SetActive(false);

        // Hide the body mesh to prevent it clipping into the camera view.
        _playerBody = player.transform.Find("Art")?.gameObject;
        if (_playerBody != null)
            _playerBody.SetActive(false);

        // Diegetic views must never leave the player's point light off — force it on
        // regardless of any indoor/outdoor state or other system that may have hidden it,
        // and remember whether it was already off so Close() can restore the true state.
        _playerInstance = player.GetComponent<PlayerInstance>();
        if (_playerInstance != null)
        {
            _preOpenLightActive = _playerInstance.IsOutsideLocal;
            _playerInstance.SetPlayerLightActive(true);
        }

        OnOpened();
    }

    /// <summary>
    /// Closes this view and fully restores normal player controls.
    /// Calls <see cref="OnClosed"/> before tearing down generic state so subclasses
    /// still have access to <see cref="Player"/> during their cleanup.
    /// </summary>
    public void Close()
    {
        if (!IsActive) return;
        IsActive = false;
        IsAnyViewActive = false;
        Current = null;

        // Give subclass a chance to clean up while Player is still valid.
        OnClosed();

        if (_viewCamera != null)
        {
            _viewCamera.transform.SetPositionAndRotation(_baseCameraPosition, _baseCameraRotation);
            _viewCamera.gameObject.SetActive(false);
        }

        // Cache before nulling so we can re-apply pickup state after arms are re-enabled.
        PlayerInteractionController closingPlayer = Player;
        if (Player != null)
        {
            Player.playerMovementController.SetCanControl(true);
            Player.SetSuspectCamMode(false);
            Player = null;
        }

        UIController.Instance.HideCursor();
        if (ShowBackButton)
            UIController.Instance.HideBackButton();

        // Restore the point light to whatever the real outdoor/indoor state dictates —
        // do not leave it force-enabled beyond the lifetime of this view.
        if (_playerInstance != null)
        {
            _playerInstance.SetPlayerLightActive(_preOpenLightActive);
            _playerInstance = null;
        }

        if (_playerArms != null)
        {
            _playerArms.SetActive(true);
            _playerArms = null;

            // On host, SpawnAndPickUpClientRpc fires synchronously within the purchase
            // call stack while the arms Animator is inactive. Unity resets all Animator
            // parameters to defaults on re-enable (keepAnimatorStateOnDisable = false),
            // which clears any pickupAnimBool set during that window. Re-apply it now
            // so the hold animation matches the actual held item.
            ReapplyHeldItemAnimatorState(closingPlayer);
        }

        if (_playerBody != null)
        {
            _playerBody.SetActive(true);
            _playerBody = null;
        }
    }

    /// <summary>
    /// Re-applies the currently held item's <see cref="PickableItemData.pickupAnimBool"/>
    /// to the local animators directly. Called after the arm GameObject is re-enabled to
    /// counteract the Animator parameter reset that Unity performs on re-activation.
    /// Uses <see cref="PlayerAnimationController.SetAnimBoolLocal"/> to avoid sending a
    /// redundant RPC (the original RPC from <see cref="PickableObject.OnEquipped"/> already
    /// handles all other clients).
    /// </summary>
    private static void ReapplyHeldItemAnimatorState(PlayerInteractionController player)
    {
        if (player == null) return;
        PlayerPickupController pickup = player.GetComponent<PlayerPickupController>();
        PickableItemData itemData = pickup?.HeldObject?.ItemData;
        if (itemData == null || string.IsNullOrEmpty(itemData.pickupAnimBool)) return;
        PlayerAnimationController pac = player.GetComponent<PlayerAnimationController>();
        pac?.SetAnimBoolLocal(itemData.pickupAnimBool, true);
    }

    // ─── Subclass hooks ──────────────────────────────────────────────────────

    /// <summary>Called once after all generic open logic has run. Override to set up subclass state.</summary>
    protected virtual void OnOpened() { }

    /// <summary>
    /// Called at the start of <see cref="Close"/>, before generic teardown.
    /// <see cref="Player"/> is still valid here.
    /// Override to perform subclass-specific cleanup.
    /// </summary>
    protected virtual void OnClosed() { }

    /// <summary>
    /// Called every frame while this view is active, after the exit-key check and camera pan.
    /// Override to handle custom interaction logic (raycasts, input, etc.).
    /// </summary>
    protected virtual void OnUpdate() { }

    /// <summary>
    /// When overridden to return true, suppresses camera panning for that frame.
    /// Use this to freeze the view while a popup or overlay is open.
    /// </summary>
    protected virtual bool SuppressCameraMovement => false;

    /// <summary>
    /// When overridden to return false, the back button is not shown while this view
    /// is open. Useful for views that exit via their own input (e.g. mouse release).
    /// Defaults to true.
    /// </summary>
    protected virtual bool ShowBackButton => true;

    /// <summary>
    /// Called when the player presses the exit key. Defaults to <see cref="Close"/>.
    /// Override to intercept the key — for example, to dismiss a popup before closing the view.
    /// </summary>
    protected virtual void OnExitKeyPressed() => Close();

    // ─── MonoBehaviour ───────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsActive) return;

        // Don't process any input or interaction while the game is paused.
        if (UIController.Instance != null && UIController.Instance.IsPaused) return;

        if (Input.GetKeyDown(_exitKey))
        {
            OnExitKeyPressed();
            return;
        }

        if (!SuppressCameraMovement)
            HandleCameraMovement();
        OnUpdate();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Translates the view camera based on cursor position relative to the screen centre.
    /// Cursor at centre = no offset; cursor at screen edge = full pan limit.
    /// The offset is expressed in the camera's local XY plane so it works regardless
    /// of the camera's world orientation.
    /// </summary>
    private void HandleCameraMovement()
    {
        if (_viewCamera == null) return;

        float normX = (Input.mousePosition.x / Screen.width)  * 2f - 1f;
        float normY = (Input.mousePosition.y / Screen.height) * 2f - 1f;

        float panY = normY >= 0f ? normY * _maxPanUp : normY * _maxPanDown;
        Vector3 localOffset = new Vector3(normX * _maxPanX, panY, 0f);
        _viewCamera.transform.position = _baseCameraPosition + _baseCameraRotation * localOffset;
    }
}
