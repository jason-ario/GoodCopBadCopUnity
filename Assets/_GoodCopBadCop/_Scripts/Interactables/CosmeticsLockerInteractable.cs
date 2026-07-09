using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A world interactable that opens a face-camera cosmetics view, letting the local player
/// browse and equip hats via <see cref="CosmeticsMenuUI"/>.
///
/// On open:
///   - Activates the face <see cref="CinemachineCamera"/> so Cinemachine blends to it.
///   - Calls <see cref="PlayerAnimationController.EnterThirdPersonPreview"/> to restore the
///     head bone scale and make the body arms mesh visible (mirrors the ragdoll death restore).
///   - Hides the first-person arms at CinemachineCamera/Arms_Socket/Player_Arms.
///   - Disables player movement and the interaction reticle.
///   - Shows the Back UI button (Q to exit).
///
/// On close:
///   - Calls <see cref="PlayerAnimationController.ExitThirdPersonPreview"/> to revert visuals.
///   - Re-enables everything in reverse order.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CosmeticsLockerInteractable : Interactable, IHeldItemPassthrough
{
    [Header("Face Camera")]
    [Tooltip("CinemachineCamera that faces the player's character. Parented to the player prefab and inactive by default; set its priority higher than the FP camera.")]
    [SerializeField] private CinemachineCamera _faceCamera;

    [Header("UI")]
    [Tooltip("The cosmetics selection panel to show while this view is open.")]
    [SerializeField] private CosmeticsMenuUI _menuUI;

    private PlayerInteractionController _interactingPlayer;
    private PlayerAnimationController _playerAnimController;
    private GameObject _firstPersonArms;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Cosmetics";
    }

    // ─── IInteractable ───────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_interactingPlayer != null) return; // Already open.

        _interactingPlayer = player;
        OpenView(player);
    }

    // ─── View lifecycle ──────────────────────────────────────────────────────

    private void OpenView(PlayerInteractionController player)
    {
        // Resolve the face camera from the spawned player if not set in the Inspector.
        if (_faceCamera == null)
            _faceCamera = player.transform.Find("FaceCamera")?.GetComponent<CinemachineCamera>();

        // Disable movement and the standard interaction/reticle system.
        player.playerMovementController.SetCanControl(false);
        player.SetSuspectCamMode(true);

        // Snap look angle to straight ahead so the face camera starts centred.
        player.playerMovementController.ResetCameraRotation();

        // Restore head bone and body arms to a visible state BEFORE activating the
        // face camera so the Cinemachine blend starts against the correct visuals.
        _playerAnimController = player.GetComponent<PlayerAnimationController>();
        _playerAnimController?.EnterThirdPersonPreview();

        // Activate the face camera — Cinemachine blends automatically.
        if (_faceCamera != null)
            _faceCamera.gameObject.SetActive(true);

        // Hide first-person arms to prevent them occluding the face view.
        _firstPersonArms = player.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_firstPersonArms != null)
            _firstPersonArms.SetActive(false);

        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(CloseView);

        // Open the cosmetics menu UI and bind the local player's hat controller.
        if (_menuUI != null)
        {
            PlayerHatController hatController = player.GetComponent<PlayerHatController>();
            _menuUI.Open(hatController);
        }
    }

    private void CloseView()
    {
        // Close the cosmetics menu UI first so it can clean up bindings.
        _menuUI?.Close();

        // Deactivate face camera — Cinemachine blends back to the FP camera.
        if (_faceCamera != null)
            _faceCamera.gameObject.SetActive(false);

        // Revert head bone and body arms to normal first-person state.
        _playerAnimController?.ExitThirdPersonPreview();
        _playerAnimController = null;

        // Restore first-person arms.
        if (_firstPersonArms != null)
        {
            _firstPersonArms.SetActive(true);
            _firstPersonArms = null;
        }

        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();

        // Re-enable player movement and interaction.
        if (_interactingPlayer != null)
        {
            _interactingPlayer.SetSuspectCamMode(false);
            _interactingPlayer.playerMovementController.SetCanControl(true);
            _interactingPlayer = null;
        }
    }
}
