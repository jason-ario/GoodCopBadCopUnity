using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world-space purchasable interactable that fires a <see cref="UnityEvent"/> on successful
/// purchase instead of giving the player a physical item. Designed for scene-level actions such
/// as repairing the booth glass, unlocking a fixture, or triggering any one-off effect.
///
/// Requires a <see cref="ShopItem"/> component on the same GameObject to supply the display
/// name and price shown in the shared purchase popup. Does NOT use the ShopItem's pickable data.
///
/// Availability is controlled by <see cref="SetAvailable"/>. Call it from within an already-
/// synchronised context (e.g. inside a ClientRpc or from code that runs on all clients).
/// Use <see cref="SetAvailableServerRpc"/> from server-only code to broadcast the state change.
///
/// The GameObject must also have a <see cref="NetworkObject"/> component.
/// </summary>
[RequireComponent(typeof(ShopItem))]
public class WorldPurchaseActionInteractable : Interactable
{
    [Header("Zoom Camera")]
    [Tooltip("Optional CinemachineCamera that blends in to frame this object during purchase. Leave empty to skip.")]
    [SerializeField] private CinemachineCamera _itemZoomCamera;

    [Header("Drawer Lock")]
    [Tooltip("Optional drawer to lock while the purchase view is open.")]
    [SerializeField] private Drawer _drawerToLock;

    [Header("Purchase Action")]
    [Tooltip("Title shown in the purchase popup. When empty, falls back to the ShopItem name (without the 'Buy ' prefix).")]
    [SerializeField] private string _popupTitle;

    [Tooltip("Fired on all clients after a successful purchase.")]
    [SerializeField] private UnityEvent _onPurchaseConfirmed;

    // ─── Runtime state ─────────────────────────────────────────────────────────

    private ShopItem _shopItem;
    private PlayerInteractionController _currentPlayer;
    private bool _popupOpen;

    // ─── Constants ─────────────────────────────────────────────────────────────

    private const string PurchaseSuccessMessage = "Done!";
    private const string NotEnoughMoneyMessage  = "Not enough coupons!";
    private const float  ZoomPaddingMultiplier  = 1.5f;

    // ─── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _shopItem = GetComponent<ShopItem>();

        if (_shopItem != null && string.IsNullOrEmpty(interactText))
            interactText = _shopItem.Name;
    }

    // ─── Interactable override ─────────────────────────────────────────────────

    /// <summary>Opens the purchase popup when the player clicks or presses E on this object.</summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (_popupOpen || _shopItem == null || !_shopItem.IsAvailable) return;
        _currentPlayer = player;
        OpenPurchaseView();
    }

    // ─── Purchase view ─────────────────────────────────────────────────────────

    private void OpenPurchaseView()
    {
        _popupOpen = true;
        _shopItem.SetHighlightBlocked(true);

        _currentPlayer.playerMovementController.SetCanControl(false);
        _currentPlayer.SetSuspectCamMode(true);

        UIController.Instance.ShowCursor();
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(ClosePurchaseView);
        UIController.OnPauseMenuOpened += ClosePurchaseView;

        ActivateZoomCamera();
        string title = string.IsNullOrEmpty(_popupTitle) ? _shopItem.Name : _popupTitle;
        UIController.Instance.OpenShopItemPurchasePopup(_shopItem, OnBuyConfirmed, ClosePurchaseView, title);
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

    // ─── Purchase logic ─────────────────────────────────────────────────────────

    private void TryPurchase()
    {
        if (_shopItem == null || !_shopItem.IsAvailable) return;
        if (!HasEnoughMoney()) return;

        // Deduct money — SubtractMoneyFromClient routes through ServerRpc if called from a client.
        GlobalHostVariables.Instance?.SubtractMoneyFromClient(_shopItem.Price);
        UIController.Instance.ShowShopNotification(PurchaseSuccessMessage);

        // If the NetworkObject is spawned, broadcast the purchase to all clients via RPC.
        // Fallback to direct invocation when offline or when the NetworkObject isn't yet spawned
        // (can happen if ApplySmash was called outside a networked context, e.g. via cheat console
        // before a host session is running).
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            ExecutePurchaseServerRpc();
        }
        else
        {
            _onPurchaseConfirmed?.Invoke();
            SetAvailable(false);
        }

        ClosePurchaseView();
    }

    private bool HasEnoughMoney()
    {
        if (GlobalHostVariables.Instance != null &&
            GlobalHostVariables.Instance.money.Value < _shopItem.Price)
        {
            UIController.Instance.ShowShopNotification(NotEnoughMoneyMessage);
            return false;
        }
        return true;
    }

    // ─── Networking ─────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ExecutePurchaseServerRpc() => ExecutePurchaseClientRpc();

    /// <summary>
    /// Fires the purchase event and deactivates this interactable on all clients.
    /// </summary>
    [ClientRpc]
    private void ExecutePurchaseClientRpc()
    {
        _onPurchaseConfirmed?.Invoke();
        SetAvailable(false);
    }

    /// <summary>
    /// Shows or hides this interactable on the local client.
    /// Safe to call from any already-synchronised context such as inside a ClientRpc
    /// or from code that is already guaranteed to run on all peers.
    /// </summary>
    public void SetAvailable(bool available)
    {
        _shopItem?.SetAvailable(available);
        gameObject.SetActive(available);
    }

    /// <summary>
    /// Requests an availability change from any peer. The server propagates to all clients.
    /// Use this from server-only code when the change does not originate inside a ClientRpc.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SetAvailableServerRpc(bool available) => SetAvailableClientRpc(available);

    [ClientRpc]
    private void SetAvailableClientRpc(bool available) => SetAvailable(available);

    // ─── Camera framing ─────────────────────────────────────────────────────────

    private void ActivateZoomCamera()
    {
        if (_itemZoomCamera == null) return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
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
}
