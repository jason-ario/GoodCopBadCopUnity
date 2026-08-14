using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// A pickable polaroid camera that supports a two-phase flow:
///
///   1. Pick up the camera (LMB — standard <see cref="PickableObject"/> pickup).
///   2. LMB while holding → enter camera mode: enables the viewfinder Unity Camera,
///      sets the "usingTool" animator bool, disables world interaction.
///   3. LMB inside camera mode → take a photo: captures the viewfinder render texture,
///      JPG-encodes it, spawns a polaroid <see cref="NetworkObject"/> at <see cref="_photoSpawnPoint"/>,
///      broadcasts the encoded photo bytes to every client so all players see the same image,
///      DOTweens it to <see cref="_photoFinalPoint"/>, then locks it to the camera via
///      <see cref="PickableObject.SetSocketFollowWithLocalOffset"/>.
///   4. Escape inside camera mode → exit camera mode.
///   5. Drop the camera.
///   6. E near the placed camera → extract the polaroid into the player's hands
///      (clears socket follow, routes the already-spawned object directly to the player).
///
/// Prefab requirements
/// ─────────────────────────────────────────────────────────────────────────────
///   • NetworkObject + NetworkTransform
///   • HighlightEffect   (required by <see cref="Interactable"/>)
///   • ParentConstraint  (required by <see cref="PickableObject"/>)
///   • Collider on the Interactable layer
///   • Child <see cref="Camera"/> (Viewfinder Camera) → assign to <see cref="_viewfinderCamera"/>.
///     No RenderTexture — renders directly to the player's screen. Starts disabled.
///   • Child <see cref="Camera"/> (Render Camera) → assign to <see cref="_renderCamera"/>.
///     Must have <c>Photo.renderTexture</c> set as its Target Texture. Stays disabled
///     until the shutter fires; enabled for exactly one frame to capture the photo.
///   • Two child Transforms → <see cref="_photoSpawnPoint"/> (eject origin) and
///     <see cref="_photoFinalPoint"/> (resting slot on the camera body).
///   • <see cref="_polaroidPrefab"/> → a prefab with NetworkObject + NetworkTransform
///     + PickableObject (+ ParentConstraint + HighlightEffect + Collider) that is
///     registered in the NetworkManager's NetworkPrefabs list.
///   • "Item Data" field → Camera.asset  (<see cref="PickableItemData"/>)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CameraPickup : PickableObject
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Camera Mode")]
    [Tooltip("Child Unity Camera used as the viewfinder. Renders directly to the player's screen — no RenderTexture needed. Starts disabled.")]
    [SerializeField] private Camera _viewfinderCamera;
    [Tooltip("Child Unity Camera used exclusively for photo capture. Must have Photo.renderTexture assigned as its Target Texture. Stays disabled until the shutter fires.")]
    [SerializeField] private Camera _renderCamera;

    [Header("Photo Polaroid")]
    [Tooltip("Prefab for the polaroid photo. Must have NetworkObject + NetworkTransform + PickableObject + ParentConstraint. Registered in NetworkManager prefabs.")]
    [SerializeField] private GameObject _polaroidPrefab;
    [Tooltip("Child Transform that the polaroid actually follows. Animated from spawn to final position in local space.")]
    [SerializeField] private Transform _photoFollowPoint;
    [Tooltip("Reference child Transform: local-space start of the eject animation.")]
    [SerializeField] private Transform _photoSpawnPoint;
    [Tooltip("Reference child Transform: local-space end of the eject animation (resting slot).")]
    [SerializeField] private Transform _photoFinalPoint;
    [Tooltip("Duration in seconds for the eject DOTween.")]
    [SerializeField] private float _photoAnimDuration = 0.5f;

    [Header("Audio")]
    [Tooltip("Played locally when the shutter fires.")]
    [SerializeField] private AudioClip _shutterSound;
    [Tooltip("Played locally when the photo ejects.")]
    [SerializeField] private AudioClip _ejectSound;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const string UsingToolBool = "UsingTool";
    private const string InteractTextDefault = "Camera";
    private const string InteractTextHasPhoto = "to grab photo";

    /// <summary>
    /// Brief window after entering camera mode during which LMB does not fire the shutter,
    /// preventing the same click that activated camera mode from immediately taking a photo.
    /// </summary>
    private const float CameraModeCooldown = 0.3f;

    /// <summary>JPG encode quality used when broadcasting the captured photo to all clients.</summary>
    private const int PhotoJpegQuality = 85;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private bool _inCameraMode;
    private float _cameraModeEnterTime;
    private bool _isAnimatingPhoto;
    private PlayerInteractionController _interactionController;
    private PlayerInstance _cutscenePlayer;

    /// <summary>
    /// NetworkObjectId of the currently attached polaroid (0 = none).
    /// Server-authoritative; all clients read it to drive reticle text and extraction.
    /// </summary>
    private readonly NetworkVariable<ulong> _netPolaroidId = new(
        0UL,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Shows the E-key reticle hint while a polaroid is available to collect.</summary>
    public override bool ShowInteractHint => _netPolaroidId.Value != 0UL;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_viewfinderCamera != null)
            _viewfinderCamera.gameObject.SetActive(false);

        if (_renderCamera != null)
            _renderCamera.gameObject.SetActive(false);

        interactText = InteractTextDefault;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _netPolaroidId.OnValueChanged += OnNetPolaroidIdChanged;
        // Sync reticle text for late-joining clients.
        interactText = _netPolaroidId.Value != 0UL ? InteractTextHasPhoto : InteractTextDefault;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _netPolaroidId.OnValueChanged -= OnNetPolaroidIdChanged;
        UnsubscribeFromCutsceneState();
    }

    private void Update()
    {
        // Only execute on the local owning player while in camera mode.
        if (playerPickupController == null || !playerPickupController.IsOwner) return;
        if (!_inCameraMode) return;

        // Guard: don't take a photo on the same frame we entered camera mode
        bool cooldownElapsed = Time.time >= _cameraModeEnterTime + CameraModeCooldown;
        if (!cooldownElapsed) return;

        // Handle shutter (LMB) manually because InteractionController is disabled in this mode.
        // We check GetMouseButtonDown(0) directly here.
        if (Input.GetMouseButtonDown(0))
        {
            if (!_isAnimatingPhoto && _netPolaroidId.Value == 0UL)
            {
                TakePhoto();
            }
        }
    }

    // ── Camera Mode ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerPickupController"/> when the player LMBs while holding the camera.
    /// Enables the viewfinder camera, sets the "usingTool" animator bool, and suppresses world interaction.
    /// </summary>
    public override void OnStartUse()
    {
        base.OnStartUse();
        if (_inCameraMode) return;
        EnterCameraMode();
    }

    /// <summary>Overridden so mouse-up does not exit camera mode; the Back control handles manual exit.</summary>
    public override void OnStopUse()
    {
        if (_inCameraMode) return;
        base.OnStopUse();
    }

    private void EnterCameraMode()
    {
        _inCameraMode = true;
        _cameraModeEnterTime = Time.time;

        if (_viewfinderCamera != null)
            _viewfinderCamera.gameObject.SetActive(true);

        // Keep the render camera running alongside the viewfinder so its RT is continuously
        // updated. Capturing mid-Update then reads the previous frame's fully-composed result
        // without needing a coroutine or WaitForEndOfFrame.
        if (_renderCamera != null)
            _renderCamera.gameObject.SetActive(true);

        playerPickupController.PlayerAnimationController.SetAnimBool(UsingToolBool, true);

        _interactionController ??= playerPickupController.GetComponent<PlayerInteractionController>();
        _interactionController?.SetSuspectCamMode(true);
        SubscribeToCutsceneState();
        UIController.Instance?.ShowBackButton(ExitCameraMode);
    }

    private void SubscribeToCutsceneState()
    {
        _cutscenePlayer = PlayerInstance.Instance;
        if (_cutscenePlayer != null)
            _cutscenePlayer.OnCutsceneStateChanged += HandleCutsceneStateChanged;
    }

    private void UnsubscribeFromCutsceneState()
    {
        if (_cutscenePlayer != null)
            _cutscenePlayer.OnCutsceneStateChanged -= HandleCutsceneStateChanged;

        _cutscenePlayer = null;
    }

    private void HandleCutsceneStateChanged(bool isInCutscene)
    {
        if (isInCutscene && _inCameraMode)
            ExitCameraMode();
    }


    private void ExitCameraMode()
    {
        _inCameraMode = false;
        isUsing = false;
        UnsubscribeFromCutsceneState();

        if (_viewfinderCamera != null)
            _viewfinderCamera.gameObject.SetActive(false);

        if (_renderCamera != null)
            _renderCamera.gameObject.SetActive(false);

        if (playerPickupController != null)
            playerPickupController.PlayerAnimationController.SetAnimBool(UsingToolBool, false);

        UIController.Instance?.HideBackButton();

        // Defer re-enabling interaction by one frame.
        // If we restore _canInteract immediately, PlayerInteractionController.Update() may
        // run later in the same frame, see the same LMB click, call OnStartUse(), and
        // immediately re-enter camera mode. Deferring keeps _canInteract false for the
        // remainder of this frame so that click is never processed as an item-use.
        StartCoroutine(RestoreInteractionNextFrame());
    }

    private IEnumerator RestoreInteractionNextFrame()
    {
        yield return null;
        if (PlayerInstance.Instance == null || !PlayerInstance.Instance.IsInCutscene)
            _interactionController?.SetSuspectCamMode(false);
    }

    // ── Equip / Unequip ───────────────────────────────────────────────────────

    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
        _interactionController = player.GetComponent<PlayerInteractionController>();
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        if (_inCameraMode)
            ExitCameraMode();

        base.OnUnequip(player);
    }

    // ── Photo Capture ─────────────────────────────────────────────────────────

    private void TakePhoto()
    {
        if (_polaroidPrefab == null)
        {
            Debug.LogWarning("[CameraPickup] _polaroidPrefab is not assigned — cannot take a photo.");
            return;
        }

        if (_renderCamera == null || _renderCamera.targetTexture == null)
        {
            Debug.LogWarning("[CameraPickup] _renderCamera or its Target Texture is not assigned — cannot capture photo.", this);
            return;
        }

        _isAnimatingPhoto = true;

        // The render camera has been running alongside the viewfinder since EnterCameraMode,
        // so its RT holds the previous frame's fully-composed scene — exactly what the player
        // last saw through the viewfinder. Capture it now, before ExitCameraMode shuts the camera down.
        byte[] photoData = CaptureAndEncodePhoto();

        ExitCameraMode();

        if (_shutterSound != null)
            SFXController.Instance.PlayAtPosition(_shutterSound, transform.position);

        // Send spawn/final positions in the follow point's parent local space (i.e. camera local space).
        Vector3    localSpawnPos = _photoSpawnPoint.localPosition;
        Quaternion localSpawnRot = _photoSpawnPoint.localRotation;
        Vector3    localFinalPos = _photoFinalPoint.localPosition;
        Quaternion localFinalRot = _photoFinalPoint.localRotation;

        SpawnPhotoServerRpc(localSpawnPos, localSpawnRot, localFinalPos, localFinalRot, photoData);
    }

    /// <summary>
    /// Reads the current contents of <see cref="_renderCamera"/>'s target RenderTexture and
    /// JPG-encodes it into a byte array suitable for broadcasting over the network.
    ///
    /// The intermediate Texture2D is created with <c>linear = true</c> because URP writes
    /// linear-space values into the <c>R8G8B8A8_UNorm</c> render texture (no automatic gamma
    /// encoding). JPG encoding operates on the raw pixel bytes regardless of that flag, so it
    /// does not affect the resulting file — the flag matters again on the receiving end, where
    /// the decoded texture must also be marked linear to avoid a double sRGB conversion in the
    /// polaroid material shader.
    /// </summary>
    private byte[] CaptureAndEncodePhoto()
    {
        RenderTexture rt   = _renderCamera.targetTexture;
        RenderTexture prev = RenderTexture.active;

        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false, true);
        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = prev;

        byte[] data = ImageConversion.EncodeToJPG(texture, PhotoJpegQuality);
        Destroy(texture);
        return data;
    }

    // ── Photo Spawn RPC ───────────────────────────────────────────────────────

    /// <summary>
    /// Spawns the polaroid NetworkObject on the server and broadcasts positioning data to all clients.
    /// Spawn/final positions are in the camera's local space (follow-point parent space).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SpawnPhotoServerRpc(
        Vector3 localSpawnPos, Quaternion localSpawnRot,
        Vector3 localFinalPos, Quaternion localFinalRot,
        byte[] photoData,
        ServerRpcParams rpcParams = default)
    {
        if (_polaroidPrefab == null) return;

        // Spawn the polaroid at the spawn point's current world position.
        GameObject spawned = Instantiate(_polaroidPrefab, _photoSpawnPoint.position, _photoSpawnPoint.rotation);
        NetworkObject no = spawned.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[CameraPickup] _polaroidPrefab is missing a NetworkObject component.");
            Destroy(spawned);
            return;
        }

        no.Spawn(true);
        _netPolaroidId.Value = no.NetworkObjectId;

        ulong photographerId = rpcParams.Receive.SenderClientId;

        AttachPolaroidClientRpc(
            new NetworkObjectReference(no),
            localSpawnPos, localSpawnRot,
            localFinalPos, localFinalRot,
            photographerId,
            photoData);
    }

    /// <summary>
    /// Received on all clients. Moves <see cref="_photoFollowPoint"/> to the spawn position,
    /// attaches the polaroid to it with zero local offset, decodes and displays the JPG photo
    /// data on every client, then DOTweens the follow point to the final position in local
    /// space on the photographing client (all others snap).
    /// </summary>
    [ClientRpc]
    private void AttachPolaroidClientRpc(
        NetworkObjectReference polaroidRef,
        Vector3 localSpawnPos, Quaternion localSpawnRot,
        Vector3 localFinalPos, Quaternion localFinalRot,
        ulong photographerClientId,
        byte[] photoData)
    {
        if (!polaroidRef.TryGet(out NetworkObject polaroidNO))
        {
            Debug.LogWarning("[CameraPickup] AttachPolaroidClientRpc: could not resolve polaroid NetworkObject.");
            return;
        }

        PickableObject polaroid = polaroidNO.GetComponent<PickableObject>();
        if (polaroid == null)
        {
            Debug.LogWarning("[CameraPickup] AttachPolaroidClientRpc: polaroid prefab has no PickableObject.");
            return;
        }

        // Suppress NetworkTransform — SocketFollow drives position on all clients.
        NetworkTransform nt = polaroidNO.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        Rigidbody rb = polaroidNO.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        polaroidNO.AutoObjectParentSync = false;

        // Place the follow point at the spawn position (local space).
        _photoFollowPoint.localPosition = localSpawnPos;
        _photoFollowPoint.localRotation = localSpawnRot;

        // Attach the polaroid to the follow point with zero local offset.
        // SocketFollow will track _photoFollowPoint.position each frame, so as the
        // follow point animates or the camera moves, the polaroid moves with it.
        polaroid.SetSocketFollowWithLocalOffset(_photoFollowPoint, Vector3.zero, Quaternion.identity);

        // Decode and paint the photo on every client — the JPG bytes travelled through the
        // server RPC round-trip so all clients (not just the photographer) see the same image.
        if (photoData != null && photoData.Length > 0)
        {
            Polaroid polaroidDisplay = polaroidNO.GetComponent<Polaroid>();
            if (polaroidDisplay != null)
            {
                var photo = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
                photo.LoadImage(photoData);
                polaroidDisplay.SetPhoto(photo, takeOwnership: true);
            }
            else
            {
                Debug.LogWarning("[CameraPickup] Spawned polaroid prefab has no Polaroid component.", this);
            }
        }

        bool isPhotographer = NetworkManager.Singleton.LocalClientId == photographerClientId;

        if (isPhotographer)
        {
            if (_ejectSound != null)
                SFXController.Instance.PlayAtPosition(_ejectSound, transform.position);

            // Tween the follow point in local space — the polaroid rides along automatically.
            _photoFollowPoint.DOLocalMove(localFinalPos, _photoAnimDuration)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => _isAnimatingPhoto = false);

            _photoFollowPoint.DOLocalRotateQuaternion(localFinalRot, _photoAnimDuration)
                .SetEase(Ease.OutCubic);
        }
        else
        {
            // Non-photographer clients: snap the follow point to the final slot immediately.
            _photoFollowPoint.localPosition = localFinalPos;
            _photoFollowPoint.localRotation = localFinalRot;
        }
    }

    // ── Photo Extraction ──────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting the placed camera.
    /// Routes the already-spawned polaroid NetworkObject directly into the player's hands.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
    {
        if (_netPolaroidId.Value == 0UL) return;
        if (player.pickupController.HeldObject != null) return;

        ExtractPhotoServerRpc();
    }

    /// <summary>
    /// Server detaches the polaroid from the camera on all clients, then routes it to the
    /// requesting player's hands via a targeted ClientRpc.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ExtractPhotoServerRpc(ServerRpcParams rpcParams = default)
    {
        if (_netPolaroidId.Value == 0UL) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                _netPolaroidId.Value, out NetworkObject polaroidNO))
        {
            Debug.LogWarning("[CameraPickup] ExtractPhotoServerRpc: polaroid NetworkObject not found.");
            _netPolaroidId.Value = 0UL;
            return;
        }

        ulong clientId = rpcParams.Receive.SenderClientId;

        DetachPolaroidClientRpc(new NetworkObjectReference(polaroidNO));
        GivePolaroidToPlayerClientRpc(
            new NetworkObjectReference(polaroidNO),
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

        _netPolaroidId.Value = 0UL;
    }

    /// <summary>Received on all clients. Clears the socket follow and restores physics.</summary>
    [ClientRpc]
    private void DetachPolaroidClientRpc(NetworkObjectReference polaroidRef)
    {
        if (!polaroidRef.TryGet(out NetworkObject polaroidNO)) return;

        PickableObject polaroid = polaroidNO.GetComponent<PickableObject>();
        polaroid?.ClearSocketFollow();

        Rigidbody rb = polaroidNO.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
    }

    /// <summary>
    /// Received only by the extracting player. Picks up the polaroid — NT is suppressed again
    /// internally by <see cref="PlayerPickupController.PickUpObject"/> while held.
    /// </summary>
    [ClientRpc]
    private void GivePolaroidToPlayerClientRpc(
        NetworkObjectReference polaroidRef,
        ClientRpcParams rpcParams = default)
    {
        if (!polaroidRef.TryGet(out NetworkObject polaroidNO))
        {
            Debug.LogWarning("[CameraPickup] GivePolaroidToPlayerClientRpc: could not resolve polaroid.");
            return;
        }

        PickableObject polaroid = polaroidNO.GetComponent<PickableObject>();
        if (polaroid == null) return;

        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        ppc?.PickUpObject(polaroid);
    }

    // ── Network Callbacks ─────────────────────────────────────────────────────

    private void OnNetPolaroidIdChanged(ulong previous, ulong current)
    {
        interactText = current != 0UL ? InteractTextHasPhoto : InteractTextDefault;
    }
}
