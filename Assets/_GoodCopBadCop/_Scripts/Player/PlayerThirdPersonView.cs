using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Centralizes entering and exiting the third-person preview mode on the local player.
/// Manages the FaceCamera, first-person arms, and FP fill light so systems like the
/// Cosmetics Locker and the Emote Wheel share a single, consistent implementation.
///
/// References are auto-resolved from the player hierarchy on spawn.
/// Only meaningful on the local player — all methods are no-ops on proxy clients.
/// </summary>
public class PlayerThirdPersonView : NetworkBehaviour
{
    [Tooltip("The CinemachineCamera that faces the player's character. Auto-resolved from a " +
             "child named 'FaceCamera' if left empty.")]
    [SerializeField] private CinemachineCamera _faceCamera;

    private PlayerAnimationController _animController;
    private GameObject _firstPersonArms;
    private Light _playerFillLight;

    private void Awake()
    {
        _animController = GetComponent<PlayerAnimationController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsLocalPlayer) return;

        // Auto-resolve FaceCamera from child hierarchy.
        if (_faceCamera == null)
            _faceCamera = transform.Find("FaceCamera")?.GetComponent<CinemachineCamera>();

        // Pre-cache the first-person arms and fill light so Enter/Exit are allocation-free.
        _firstPersonArms = transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        _playerFillLight = transform.Find("CinemachineCamera/Point Light")?.GetComponent<Light>();

        if (_faceCamera == null)
            Debug.LogWarning("[PlayerThirdPersonView] FaceCamera not found in player hierarchy.", this);

        if (_firstPersonArms == null)
            Debug.LogWarning("[PlayerThirdPersonView] Player_Arms not found at CinemachineCamera/Arms_Socket/Player_Arms.", this);
    }

    /// <summary>
    /// Enters third-person view: restores the head bone to full scale so it is visible,
    /// switches the body arms mesh to normal shadow casting, activates the FaceCamera
    /// so Cinemachine blends to it, hides the first-person arms, and disables the FP fill light.
    /// No-op on proxy clients.
    /// </summary>
    public void Enter()
    {
        if (!IsLocalPlayer) return;

        // Restore visuals BEFORE activating the camera so the Cinemachine blend
        // starts against the correct geometry.
        _animController?.EnterThirdPersonPreview();

        if (_faceCamera != null)
            _faceCamera.gameObject.SetActive(true);

        if (_firstPersonArms != null)
            _firstPersonArms.SetActive(false);

        if (_playerFillLight != null)
            _playerFillLight.enabled = false;
    }

    /// <summary>
    /// Exits third-person view: deactivates the FaceCamera so Cinemachine blends back to the
    /// first-person camera, reverts the head bone and body arms to FP-safe state, restores the
    /// first-person arms, and re-enables the FP fill light.
    /// No-op on proxy clients.
    /// </summary>
    public void Exit()
    {
        if (!IsLocalPlayer) return;

        if (_faceCamera != null)
            _faceCamera.gameObject.SetActive(false);

        // Revert head bone and body arms after the camera blend starts so the
        // FP camera starts hidden before the mesh reverts.
        _animController?.ExitThirdPersonPreview();

        if (_firstPersonArms != null)
            _firstPersonArms.SetActive(true);

        if (_playerFillLight != null)
            _playerFillLight.enabled = true;
    }
}
