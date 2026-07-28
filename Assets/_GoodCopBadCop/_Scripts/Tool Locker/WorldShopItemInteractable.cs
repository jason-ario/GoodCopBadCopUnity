using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Turns any <see cref="ShopItem"/> placed freely in the world into a purchasable interactable.
/// Add this component alongside <see cref="ShopItem"/> on the item's root GameObject.
/// The GameObject must have a <see cref="NetworkObject"/>, a collider on the interact layer,
/// and a <see cref="HighlightEffect"/> (auto-required by the <see cref="Interactable"/> base).
///
/// When the player interacts with the item:
///   - Player movement and interaction are suppressed.
///   - The optional <see cref="_itemZoomCamera"/> blends in, framing the item.
///   - The shared purchase popup is shown.
///   - On confirm: the item is despawned for all clients.
///   - On cancel or day start: the item is restocked for all clients.
/// </summary>
[RequireComponent(typeof(ShopItem))]
public class WorldShopItemInteractable : Interactable
{
    [Header("Zoom Camera")]
    [Tooltip("Optional CinemachineCamera that activates and zooms in on this item during purchase. Leave empty to skip zoom.")]
    [SerializeField] private CinemachineCamera _itemZoomCamera;

    [Header("Despawn Behaviour")]
    [Tooltip("When true, the item is hidden for all clients after a successful purchase (default shop behaviour). " +
             "Set to false for pile-style items that should remain in the world regardless of how many times they are purchased.")]
    [SerializeField] private bool _despawnOnPurchase = true;

    [Header("Drawer Lock")]
    [Tooltip("Optional drawer to lock for all clients while the purchase view is open. " +
             "Assign this when the item lives inside a drawer that should not be moved during purchase.")]
    [SerializeField] private Drawer _drawerToLock;

    [Header("Anomaly Unlock Gate")]
    [Tooltip("Optional. A sibling object (e.g. a drawer 'Tape' label) that should only be visible while " +
             "this item's ShopItem.IsUnlockRequirementMet() is true — i.e. it shows and hides in lockstep " +
             "with this pile as its anomaly category unlocks.")]
    [SerializeField] private GameObject _unlockLabel;

    // ─── Runtime state ────────────────────────────────────────────────────────

    private ShopItem _shopItem;
    private PlayerInteractionController _currentPlayer;
    private bool _popupOpen;

    // ─── Constants ────────────────────────────────────────────────────────────

    private const string HoldingObjectMessage  = "Put down what you're holding first!";
    private const string PurchaseSuccessMessage = "Item purchased!";
    private const string NotEnoughMoneyMessage  = "Not enough coupons!";
    private const float  ZoomPaddingMultiplier  = 1.5f;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _shopItem = GetComponent<ShopItem>();

        // Mirror the shop item's name as the interaction tooltip.
        if (_shopItem != null && string.IsNullOrEmpty(interactText))
            interactText = _shopItem.Name;
    }

    public override void OnNetworkSpawn()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;

        AnomalyUnlockManager.OnAnomalyUnlocked += OnAnomalyUnlocked;
        ApplyUnlockGate();
    }

    public override void OnNetworkDespawn()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;

        AnomalyUnlockManager.OnAnomalyUnlocked -= OnAnomalyUnlocked;
    }

    // ─── Interactable override ────────────────────────────────────────────────

    /// <summary>Opens the purchase view when the player clicks or presses E on this item.</summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (_popupOpen || _shopItem == null || !_shopItem.IsAvailable) return;
        _currentPlayer = player;
        OpenPurchaseView();
    }

    // ─── Purchase view ────────────────────────────────────────────────────────

    private void OpenPurchaseView()
    {
        _popupOpen = true;
        _shopItem.SetHighlightBlocked(true);

        // Suspend player movement and standard interaction.
        _currentPlayer.playerMovementController.SetCanControl(false);
        _currentPlayer.SetSuspectCamMode(true);

        UIController.Instance.ShowCursor();
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(ClosePurchaseView);
        UIController.OnPauseMenuOpened += ClosePurchaseView;

        ActivateZoomCamera();
        UIController.Instance.OpenShopItemPurchasePopup(_shopItem, OnBuyConfirmed, ClosePurchaseView);
        _drawerToLock?.SetLocked(true);
    }

    private void ClosePurchaseView()
    {
        if (!_popupOpen) return;
        _popupOpen = false;
        _shopItem.SetHighlightBlocked(false);

        UIController.OnPauseMenuOpened -= ClosePurchaseView;

        if (_itemZoomCamera != null)
            _itemZoomCamera.gameObject.SetActive(false);

        UIController.Instance.CloseShopItemPurchasePopup();
        UIController.Instance.HideBackButton();
        UIController.Instance.HideCursor();
        _drawerToLock?.SetLocked(false);

        if (_currentPlayer != null)
        {
            _currentPlayer.SetSuspectCamMode(false);
            _currentPlayer.playerMovementController.SetCanControl(true);
            _currentPlayer = null;
        }
    }

    private void OnBuyConfirmed() => TryPurchase();

    // ─── Purchase logic ───────────────────────────────────────────────────────

    private void TryPurchase()
    {
        if (_shopItem == null || !_shopItem.IsAvailable) return;

        PlayerPickupController pickup = GetLocalPlayerPickup();
        if (pickup == null)
        {
            Debug.LogError("WorldShopItemInteractable: could not find local PlayerPickupController.");
            return;
        }

        ShopPurchaseAction customAction = _shopItem.CustomPurchaseAction;

        if (customAction != null)
        {
            if (customAction.RequiresEmptyHands && pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }
            if (!HasEnoughMoney()) return;
            customAction.Execute(pickup, _shopItem.Price);
        }
        else
        {
            if (pickup.IsHoldingObject)
            {
                UIController.Instance.ShowShopNotification(HoldingObjectMessage);
                return;
            }
            if (!HasEnoughMoney()) return;
            pickup.PurchaseAndPickUp(_shopItem.pickableItemData, _shopItem.Price, pickup.holdPoint);
        }

        UIController.Instance.ShowShopNotification(PurchaseSuccessMessage);
        if (_despawnOnPurchase)
            DespawnItemServerRpc();
        ClosePurchaseView();
    }

    private bool HasEnoughMoney()
    {
        if (GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value < _shopItem.Price)
        {
            UIController.Instance.ShowShopNotification(NotEnoughMoneyMessage);
            return false;
        }
        return true;
    }

    // ─── Networking ───────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void DespawnItemServerRpc() => DespawnItemClientRpc();

    /// <summary>Hides the item for all clients after a successful purchase.</summary>
    [ClientRpc]
    private void DespawnItemClientRpc()
    {
        _shopItem.SetAvailable(false);
        _shopItem.gameObject.SetActive(false);
    }

    private void OnDayStart()
    {
        if (!IsServer) return;
        RestockClientRpc();
    }

    /// <summary>Re-shows the item for all clients at the start of each new day, unless it is still
    /// gated behind an anomaly-category unlock (see <see cref="ApplyUnlockGate"/>).</summary>
    [ClientRpc]
    private void RestockClientRpc()
    {
        ApplyUnlockGate();
    }

    // ─── Anomaly unlock gating ────────────────────────────────────────────────

    private void OnAnomalyUnlocked(string typeName) => ApplyUnlockGate();

    /// <summary>
    /// Shows or hides this pile (and its optional <see cref="_unlockLabel"/>) to match
    /// <see cref="ShopItem.IsUnlockRequirementMet"/>. Items with no required anomaly type always
    /// resolve to available/true here, so this is a no-op for ordinary shop items.
    /// </summary>
    private void ApplyUnlockGate()
    {
        if (_shopItem == null) return;

        bool unlocked = _shopItem.IsUnlockRequirementMet();
        _shopItem.SetAvailable(unlocked);
        gameObject.SetActive(unlocked);

        if (_unlockLabel != null)
            _unlockLabel.SetActive(unlocked);
    }

    // ─── Camera framing ───────────────────────────────────────────────────────

    private void ActivateZoomCamera()
    {
        if (_itemZoomCamera == null) return;

        Renderer[] renderers = _shopItem.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        Camera rayCam = PlayerInstance.Instance?.GetCamera();
        if (rayCam == null) return;

        Vector3 dir      = (bounds.center - rayCam.transform.position).normalized;
        float   fovRad   = _itemZoomCamera.Lens.FieldOfView * Mathf.Deg2Rad;
        float   radius   = bounds.extents.magnitude * ZoomPaddingMultiplier;
        float   distance = radius / Mathf.Sin(fovRad * 0.5f);

        _itemZoomCamera.transform.position = bounds.center - dir * distance;
        _itemZoomCamera.transform.LookAt(bounds.center);
        _itemZoomCamera.gameObject.SetActive(true);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
