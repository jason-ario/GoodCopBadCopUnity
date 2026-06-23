using TMPro;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Manages the diegetic tool locker interaction mode.
/// When activated, switches to the locker camera and lets the player purchase
/// shop items by pointing at them with the cursor and clicking. Press Q (or the
/// configured <see cref="ExitKey"/>) to exit.
/// </summary>
public class ToolLockerDiegeticController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("The CinemachineCamera inside the locker used when the player is browsing.")]
    [SerializeField] private CinemachineCamera _lockerCamera;

    [Tooltip("Maximum horizontal pan distance (world units) from the camera's resting position.")]
    [SerializeField] private float _maxPanX = 0.15f;

    [Tooltip("Maximum vertical pan distance (world units) when the cursor is above centre.")]
    [SerializeField] private float _maxPanUp = 0.1f;

    [Tooltip("Maximum vertical pan distance (world units) when the cursor is below centre.")]
    [SerializeField] private float _maxPanDown = 0.2f;

    [Header("Shop Items")]
    [Tooltip("All shop items physically placed inside the locker that the player can buy.")]
    [SerializeField] private ShopItem[] _shopItems;

    [Tooltip("Max raycast distance for detecting shop items under the cursor.")]
    [SerializeField] private float _interactDistance = 3f;

    [Tooltip("Layer mask that includes the ShopItems layer.")]
    [SerializeField] private LayerMask _shopItemLayer;

    [Header("UI")]
    [Tooltip("Screen-space TextMeshPro label used to show item name/price when hovering. " +
             "Must be on an active Canvas. Leave empty if not needed.")]
    [SerializeField] private TextMeshProUGUI _interactPrompt;

    [Tooltip("Key the player presses to exit the locker view.")]
    [SerializeField] private KeyCode _exitKey = KeyCode.Q;

    // ─── Runtime state ───────────────────────────────────────────────────────

    private bool _isActive;
    private PlayerInteractionController _player;
    private ToolsLocker _locker;
    private ShopItem _hoveredItem;
    private GameObject _playerArms;
    private GameObject _playerBody;

    // Camera pan state
    private Quaternion _baseCameraRotation;
    private Vector3 _baseCameraPosition;

    // ─── Constants ───────────────────────────────────────────────────────────

    private const string HoldingObjectMessage = "Put down what you're holding first!";
    private const string PurchaseSuccessMessage = "Item purchased!";
    private const string NotEnoughMoneyMessage = "Not enough coupons!";

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the diegetic locker view for the given player.
    /// Should be called from <see cref="ToolsLocker.Interact"/>.
    /// </summary>
    public void Open(PlayerInteractionController player, ToolsLocker locker)
    {
        if (_isActive) return;

        _player = player;
        _locker = locker;
        _isActive = true;

        // Activate the locker camera — Cinemachine blends to it automatically.
        if (_lockerCamera != null)
        {
            // Capture the resting transform so pan offsets are relative to it.
            _baseCameraRotation = _lockerCamera.transform.rotation;
            _baseCameraPosition = _lockerCamera.transform.position;
            _lockerCamera.gameObject.SetActive(true);
        }

        // Disable player movement and camera look so the locker camera is the authority.
        player.playerMovementController.SetCanControl(false);

        // Suppress the standard interaction/reticle system.
        player.SetSuspectCamMode(true);

        // Show cursor so the player can point at items.
        UIController.Instance.ShowCursor();

        // Hide the first-person arms so they don't occlude the locker view.
        _playerArms = player.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_playerArms != null)
            _playerArms.SetActive(false);

        // Hide the body mesh to prevent it clipping into the locker camera view.
        _playerBody = player.transform.Find("Art")?.gameObject;
        if (_playerBody != null)
            _playerBody.SetActive(false);

        if (_interactPrompt != null)
        {
            _interactPrompt.gameObject.SetActive(true);
            _interactPrompt.text = string.Empty;
        }
    }

    /// <summary>Exits the locker view and fully restores normal player controls.</summary>
    public void Close()
    {
        if (!_isActive) return;
        _isActive = false;

        if (_lockerCamera != null)
        {
            // Reset to the resting transform so it's clean on the next open.
            _lockerCamera.transform.SetPositionAndRotation(_baseCameraPosition, _baseCameraRotation);
            _lockerCamera.gameObject.SetActive(false);
        }

        if (_player != null)
        {
            _player.playerMovementController.SetCanControl(true);
            _player.SetSuspectCamMode(false);
            _player = null;
        }

        UIController.Instance.HideCursor();

        // Restore the first-person arms.
        if (_playerArms != null)
        {
            _playerArms.SetActive(true);
            _playerArms = null;
        }

        // Restore the body mesh.
        if (_playerBody != null)
        {
            _playerBody.SetActive(true);
            _playerBody = null;
        }

        if (_interactPrompt != null)
            _interactPrompt.gameObject.SetActive(false);

        if (_locker != null)
        {
            _locker.NotifyPlayerClosedServerRpc();
            _locker = null;
        }

        _hoveredItem = null;
    }

    // ─── MonoBehaviour ───────────────────────────────────────────────────────

    private void Update()
    {
        if (!_isActive) return;

        if (Input.GetKeyDown(_exitKey))
        {
            Close();
            return;
        }

        HandleCameraMovement();
        HandleLockerRaycast();

        if (Input.GetMouseButtonDown(0) && _hoveredItem != null)
            TryPurchase(_hoveredItem);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Translates the locker camera based on cursor position relative to the screen centre.
    /// Cursor at centre = no offset; cursor at edge = full <see cref="_maxPanX"/>/<see cref="_maxPanY"/> offset.
    /// The offset is expressed in the camera's local XY plane so it feels natural regardless of
    /// the camera's world orientation.
    /// </summary>
    private void HandleCameraMovement()
    {
        if (_lockerCamera == null) return;

        // Normalise cursor position to [-1, 1] relative to the screen centre.
        float normX = (Input.mousePosition.x / Screen.width)  * 2f - 1f;
        float normY = (Input.mousePosition.y / Screen.height) * 2f - 1f;

        // Build the offset in the camera's local space, then convert to world space.
        float panY = normY >= 0f ? normY * _maxPanUp : normY * _maxPanDown;
        Vector3 localOffset = new Vector3(normX * _maxPanX, panY, 0f);
        _lockerCamera.transform.position = _baseCameraPosition + _baseCameraRotation * localOffset;
    }

    private void HandleLockerRaycast()
    {
        if (_player == null || _player.cam == null) return;

        // Cast from the cursor's screen position so the mouse pointer acts as the selector.
        Ray ray = _player.cam.ScreenPointToRay(Input.mousePosition);

        // Clear the previously hovered item each frame.
        _hoveredItem = null;
        ClearPrompt();

        if (!Physics.Raycast(ray, out RaycastHit hit, _interactDistance, _shopItemLayer))
            return;

        ShopItem item = hit.collider.GetComponentInParent<ShopItem>();
        if (item == null || !item.IsAvailable)
            return;

        _hoveredItem = item;
        ShowPrompt(item);
    }

    private void ShowPrompt(ShopItem item)
    {
        if (_interactPrompt == null) return;
        _interactPrompt.text = $"{item.Name}  <sprite=0>{item.Price}  [Click to buy]";
    }

    private void ClearPrompt()
    {
        if (_interactPrompt != null)
            _interactPrompt.text = string.Empty;
    }

    private void TryPurchase(ShopItem item)
    {
        if (item == null || !item.IsAvailable) return;

        PlayerPickupController pickup = GetLocalPlayerPickup();
        if (pickup == null)
        {
            Debug.LogError("ToolLockerDiegeticController: Could not find local PlayerPickupController.");
            return;
        }

        ShopPurchaseAction customAction = item.CustomPurchaseAction;

        if (customAction != null)
        {
            if (customAction.RequiresEmptyHands && pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (!HasEnoughMoney(item.Price)) return;

            customAction.Execute(pickup, item.Price);
        }
        else
        {
            if (pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }

            if (!HasEnoughMoney(item.Price)) return;

            pickup.PurchaseAndPickUp(item.pickableItemData, item.Price, pickup.holdPoint);
        }

        UIController.Instance.ShowShopNotification(PurchaseSuccessMessage);

        bool shouldClose = customAction == null || customAction.CloseShopOnPurchase;
        if (shouldClose)
            Close();
    }

    private bool HasEnoughMoney(int price)
    {
        if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value < price)
        {
            UIController.Instance.ShowShopNotification(NotEnoughMoneyMessage);
            return false;
        }

        return true;
    }

    private static PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
