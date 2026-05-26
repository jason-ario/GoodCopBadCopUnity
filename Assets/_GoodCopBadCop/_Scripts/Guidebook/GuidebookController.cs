using System;
using UnityEngine;

/// <summary>
/// Opens and closes the guidebook when the local player presses Tab.
/// On open: deactivates the held object, locks movement and look, and sets both
/// arm animators to the HoldingGuidebook state via PlayerAnimationController.
/// On close: reverses all of the above.
/// </summary>
[RequireComponent(typeof(PlayerAnimationController))]
[RequireComponent(typeof(PlayerPickupController))]
[RequireComponent(typeof(PlayerMovementController))]
public class GuidebookController : MonoBehaviour
{
    /// <summary>Raised whenever the local player opens the guidebook.</summary>
    public static event Action OnGuidebookOpened;
    private static readonly string AnimParam  = "HoldingGuidebook";
    private static readonly string InputButton = "Guidebook";

    [SerializeField] private GameObject _guidebookObject;

    private PlayerAnimationController _animationController;
    private PlayerPickupController    _pickupController;
    private PlayerMovementController  _movementController;

    private GameObject _deactivatedHeldObject;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        _animationController = GetComponent<PlayerAnimationController>();
        _pickupController    = GetComponent<PlayerPickupController>();
        _movementController  = GetComponent<PlayerMovementController>();

        if (_guidebookObject != null)
            _guidebookObject.SetActive(false);
    }

    private void Update()
    {
        if (PlayerInstance.Instance == null || !PlayerInstance.Instance.IsLocalPlayer) return;

        if (!IsOpen && Input.GetButtonDown(InputButton))
            OpenGuidebook();
        else if (IsOpen && Input.GetButtonDown(InputButton))
            CloseGuidebook();
    }

    /// <summary>
    /// Opens the guidebook: deactivates the held object, freezes the player,
    /// and transitions both animators to the HoldingGuidebook state.
    /// </summary>
    public void OpenGuidebook()
    {
        if (IsOpen) return;
        IsOpen = true;
        OnGuidebookOpened?.Invoke();

        // Deactivate held object without dropping or despawning it.
        if (_pickupController.HeldObject != null)
        {
            _deactivatedHeldObject = _pickupController.HeldObject.gameObject;
            _deactivatedHeldObject.SetActive(false);
        }

        // Freeze movement and look, stopping any active movement immediately.
        _movementController.SetCanMove(false);
        _movementController.SetCanControl(false);
        _movementController.SetCanLook(false);

        // Both arms at full weight so the guidebook hold pose drives the full rig.
        _animationController.EnableHoldObjectTwoArmsMask();

        // Set the animator bool on both body and arms animators (networked).
        _animationController.SetAnimBool(AnimParam, true);

        if (_guidebookObject != null)
            _guidebookObject.SetActive(true);

        GuidebookContentsContainer.Instance?.Open();
    }

    /// <summary>
    /// Closes the guidebook and restores full player state.
    /// </summary>
    public void CloseGuidebook()
    {
        if (!IsOpen) return;
        IsOpen = false;

        _animationController.SetAnimBool(AnimParam, false);

        if (_guidebookObject != null)
            _guidebookObject.SetActive(false);

        GuidebookContentsContainer.Instance?.Close();
        if (_deactivatedHeldObject != null)
        {
            _deactivatedHeldObject.SetActive(true);

            PickableObject pickable = _deactivatedHeldObject.GetComponent<PickableObject>();
            if (pickable != null && pickable.ItemData.usesTwoArms)
                _animationController.EnableHoldObjectTwoArmsMask();
            else
                _animationController.EnableRightArmMask();

            _deactivatedHeldObject = null;
        }
        else
        {
            // Nothing held — clear all arm layers.
            _animationController.DisableRightArmMask();
        }

        // Restore look first so SetCanControl finds CanLook == true and re-enables the reticle.
        _movementController.SetCanLook(true);
        _movementController.SetCanControl(true);
        _movementController.SetCanMove(true);
    }
}
