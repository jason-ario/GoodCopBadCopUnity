using Unity.Netcode;
using UnityEngine;

/// <summary>
/// World interactable that opens the Guard Purchase Screen when the player interacts with it.
/// Uses NetworkVariables to synchronise purchase and arrival state across all clients.
/// The guard arrives at the start of the next in-game day after the purchase.
/// </summary>
public class GuardPurchasePoint : Interactable
{
    [Header("Purchase Settings")]
    [Tooltip("Cost in coupons to purchase a guard.")]
    [SerializeField] private int _guardPrice = 50;

    [Header("Scene References")]
    [Tooltip("Child GameObject to activate when the guard arrives the next day.")]
    [SerializeField] private GameObject _suspectSoldier;
    [Tooltip("Child GameObject representing the purchase post — deactivated when the guard arrives.")]
    [SerializeField] private GameObject _guardPurchasePost;

    private readonly NetworkVariable<bool> _guardPurchased = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _guardArrived = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _guardPurchased.OnValueChanged += OnGuardPurchasedChanged;
        _guardArrived.OnValueChanged += OnGuardArrivedChanged;

        // Apply state immediately for late-joining clients.
        if (_guardArrived.Value)
            ApplyArrivedState();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
        else
            Debug.LogError("[GuardPurchasePoint] ShiftManager.Instance is null on network spawn.", this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _guardPurchased.OnValueChanged -= OnGuardPurchasedChanged;
        _guardArrived.OnValueChanged -= OnGuardArrivedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_guardPurchased.Value)
            UIController.Instance.OpenGuardPurchaseScreenHired();
        else
            UIController.Instance.OpenGuardPurchaseScreen(_guardPrice, OnPurchaseConfirmed);
    }

    private void OnPurchaseConfirmed()
    {
        GlobalHostVariables.Instance.SubtractMoneyFromClient(_guardPrice);
        SetGuardPurchasedServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetGuardPurchasedServerRpc()
    {
        if (!_guardPurchased.Value)
            _guardPurchased.Value = true;
    }

    private void OnGuardPurchasedChanged(bool previousValue, bool newValue)
    {
        if (!newValue) return;
        UIController.Instance.ShowShopNotification("Guard hired! Will arrive tomorrow.");
    }

    /// <summary>Called on all clients at the start of each day. Sets arrival state on the server.</summary>
    private void OnDayStart()
    {
        if (!IsServer) return;
        if (_guardPurchased.Value && !_guardArrived.Value)
            _guardArrived.Value = true;
    }

    private void OnGuardArrivedChanged(bool previousValue, bool newValue)
    {
        if (!newValue) return;
        ApplyArrivedState();
    }

    private void ApplyArrivedState()
    {
        if (_suspectSoldier != null)
            _suspectSoldier.SetActive(true);

        if (_guardPurchasePost != null)
            _guardPurchasePost.SetActive(false);
    }
}
