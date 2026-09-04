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
/// Availability is server-authoritative and replicated via a NetworkVariable, so it is identical on
/// every peer including late joiners. Call <see cref="SetAvailable"/> from the server (or from an
/// already-synchronised context such as inside a ClientRpc); <see cref="SetAvailableServerRpc"/>
/// routes a request from a client to the server.
///
/// The GameObject must also have a <see cref="NetworkObject"/> component. Note that an interactable
/// authored inactive in the scene has no spawned NetworkObject until the server explicitly spawns it
/// (see BreakableGlassController.ShowRepairInteractable), which is why availability is also mirrored
/// locally in <see cref="_availableLocal"/>.
///
/// Implements <see cref="IHeldItemPassthrough"/> so the purchase popup still opens even while
/// the player is holding an item (e.g. a package or tool), instead of the held item silently
/// swallowing the click/E-press.
/// </summary>
[RequireComponent(typeof(ShopItem))]
public class WorldPurchaseActionInteractable : Interactable, IHeldItemPassthrough
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

    [Header("Persistence")]
    [Tooltip("Optional. When set, this purchase is remembered permanently across play sessions via " +
             "SaveDataManager. Use a stable, unique ID per interactable (e.g. 'BoothPC', 'BoothRadio', " +
             "'BoothTV'). Leave empty for purchases that should only last for the current session.")]
    [SerializeField] private string _persistentUnlockId;

    // ─── Runtime state ─────────────────────────────────────────────────────────

    private ShopItem _shopItem;
    private PlayerInteractionController _currentPlayer;
    private bool _popupOpen;

    // ─── Networked state ───────────────────────────────────────────────────────

    /// <summary>
    /// Server-authoritative visibility of this interactable. Replicated so that a client can never
    /// end up with the object active while another has it hidden (the reported Purchase Glass bug):
    /// availability used to be driven only by ClientRpcs, which are dropped for peers that are not
    /// connected yet and are never replayed for late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _netAvailable = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Server-authoritative "this persistent purchase has already been made". Only meaningful when
    /// <see cref="_persistentUnlockId"/> is set. Replicated so the host's save file is the single
    /// source of truth — previously every peer read its OWN save, so two players with different
    /// local saves would disagree about which purchases had been unlocked.
    /// </summary>
    private readonly NetworkVariable<bool> _netPurchased = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Local mirror of <see cref="_netAvailable"/>, valid before/without a live session.</summary>
    private bool _availableLocal = true;

    /// <summary>Guards the persistent-unlock replay so its effect is only invoked once.</summary>
    private bool _persistentUnlockReplayed;

    // ─── Constants ─────────────────────────────────────────────────────────────

    private const string PurchaseSuccessMessage = "Done!";
    private const string NotEnoughMoneyMessage  = "Not enough coupons!";

    // ─── Lifecycle ──────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _shopItem = GetComponent<ShopItem>();

        if (_shopItem != null && string.IsNullOrEmpty(interactText))
            interactText = _shopItem.Name;

        ApplySavedUnlockState();
    }

    public override void OnNetworkSpawn()
    {
        _netAvailable.OnValueChanged += HandleNetAvailableChanged;
        _netPurchased.OnValueChanged += HandleNetPurchasedChanged;

        if (IsServer)
        {
            // The host publishes its own state as the authority for everyone.
            _netPurchased.Value = HasPersistentUnlockInSave();
            _netAvailable.Value = _availableLocal;
        }
        else
        {
            // Adopt the host's state, including for a late joiner that missed every RPC.
            if (_netPurchased.Value)
                ReplayPersistentUnlock();

            ApplyAvailableLocal(_netAvailable.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        _netAvailable.OnValueChanged -= HandleNetAvailableChanged;
        _netPurchased.OnValueChanged -= HandleNetPurchasedChanged;
    }

    private void HandleNetAvailableChanged(bool previous, bool current) => ApplyAvailableLocal(current);

    private void HandleNetPurchasedChanged(bool previous, bool current)
    {
        if (current) ReplayPersistentUnlock();
    }

    // ─── Persistence ────────────────────────────────────────────────────────────

    /// <summary>
    /// If <see cref="_persistentUnlockId"/> was already purchased in a previous session, replays the
    /// purchase effect immediately (without charging) and hides this interactable.
    ///
    /// Only the authority (host, or offline play) reads the save here. Connected clients deliberately
    /// skip it and instead adopt <see cref="_netPurchased"/> in <see cref="OnNetworkSpawn"/>, so a
    /// client whose local save disagrees with the host's can no longer show a different world state.
    /// </summary>
    private void ApplySavedUnlockState()
    {
        if (string.IsNullOrEmpty(_persistentUnlockId)) return;

        var nm = NetworkManager.Singleton;
        bool isAuthority = nm == null || !nm.IsListening || nm.IsServer;
        if (!isAuthority) return;

        if (!HasPersistentUnlockInSave()) return;

        ReplayPersistentUnlock();
    }

    /// <summary>True when this interactable has a persistent ID that is already unlocked in the local save.</summary>
    private bool HasPersistentUnlockInSave()
    {
        return !string.IsNullOrEmpty(_persistentUnlockId) &&
               SaveDataManager.Instance != null &&
               SaveDataManager.Instance.IsWorldObjectUnlocked(_persistentUnlockId);
    }

    /// <summary>
    /// Re-applies an already-completed persistent purchase: fires the effect and hides the stand.
    /// Idempotent — unlike a repeatable purchase (e.g. the glass repair, which has no persistent ID)
    /// a persistent unlock must only ever be replayed once per session.
    /// </summary>
    private void ReplayPersistentUnlock()
    {
        if (_persistentUnlockReplayed) return;
        _persistentUnlockReplayed = true;

        _onPurchaseConfirmed?.Invoke();
        SetAvailable(false);
    }

    /// <summary>Persists <see cref="_persistentUnlockId"/> to save data, if one is configured. No-op otherwise.</summary>
    private void PersistUnlock()
    {
        if (string.IsNullOrEmpty(_persistentUnlockId) || SaveDataManager.Instance == null) return;
        SaveDataManager.Instance.UnlockWorldObject(_persistentUnlockId);
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
        UIController.Instance.ClosePlayerUI();
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
        UIController.Instance.ShowPlayerUI();
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
            PersistUnlock();
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
    private void ExecutePurchaseServerRpc()
    {
        // Runs only on the server — the correct, authoritative place to persist the unlock.
        PersistUnlock();

        // Record the purchase authoritatively so a late joiner gets it too, not just the clients
        // that happen to be connected right now.
        if (!string.IsNullOrEmpty(_persistentUnlockId))
        {
            _persistentUnlockReplayed = true;
            _netPurchased.Value = true;
        }

        ExecutePurchaseClientRpc();
    }

    /// <summary>
    /// Fires the purchase event and deactivates this interactable on all clients.
    /// </summary>
    [ClientRpc]
    private void ExecutePurchaseClientRpc()
    {
        // A persistent purchase must not be replayed again from _netPurchased later this session.
        if (!string.IsNullOrEmpty(_persistentUnlockId))
            _persistentUnlockReplayed = true;

        _onPurchaseConfirmed?.Invoke();
        SetAvailable(false);
    }

    /// <summary>
    /// Shows or hides this interactable. On the server this also writes the replicated availability
    /// so every client — current and future — converges on the same state; on a client it applies
    /// locally and is superseded by the server's value if the two ever disagree.
    /// </summary>
    public void SetAvailable(bool available)
    {
        // Write the replicated value BEFORE deactivating, so the change is queued while this
        // behaviour's GameObject is still active.
        if (IsSpawned && IsServer && _netAvailable.Value != available)
            _netAvailable.Value = available;

        ApplyAvailableLocal(available);
    }

    /// <summary>Applies availability to the local instance only, without touching networked state.</summary>
    private void ApplyAvailableLocal(bool available)
    {
        _availableLocal = available;
        _shopItem?.SetAvailable(available);

        if (gameObject.activeSelf != available)
            gameObject.SetActive(available);
    }

    /// <summary>
    /// Requests an availability change from any peer. The server propagates to all clients.
    /// Use this from server-only code when the change does not originate inside a ClientRpc.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SetAvailableServerRpc(bool available) => SetAvailable(available);

    // ─── Camera framing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Activates <see cref="_itemZoomCamera"/> using the transform already authored on it in the
    /// prefab. Unlike <see cref="WorldShopItemInteractable"/>'s equivalent method — which
    /// auto-frames a physical pickup item using renderer bounds — a purchase stand's camera is a
    /// fixed, designer-placed shot of the stand/kiosk itself, so it must always win outright and
    /// never be recomputed or skipped based on renderer/ray-camera lookups.
    /// </summary>
    private void ActivateZoomCamera()
    {
        if (_itemZoomCamera == null) return;
        _itemZoomCamera.gameObject.SetActive(true);
    }
}
