using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

public class SupplyBox : PickableObject
{
    public bool canPickUp = false;
    [SerializeField] Animation boxAnimation;
    [SerializeField] private GameObject contents;
    bool isOpen = false;

    /// <summary>Networked authoritative state of <see cref="canPickUp"/>. Synced to all clients.</summary>
    private NetworkVariable<bool> _networkCanPickUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Parent transform used to attach per-day items during delivery. Falls back to this transform if contents is unassigned.</summary>
    public Transform ContentsParent => contents != null ? contents.transform : transform;

    // ── Network Lifecycle ─────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCanPickUp.OnValueChanged += OnNetworkCanPickUpChanged;
        canPickUp = _networkCanPickUp.Value;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkCanPickUp.OnValueChanged -= OnNetworkCanPickUpChanged;
    }

    private void OnNetworkCanPickUpChanged(bool previous, bool current) => canPickUp = current;

    // ── Networked API ─────────────────────────────────────────────────────────

    /// <summary>Sets <see cref="canPickUp"/> on all clients via the server.</summary>
    public void SetCanPickUpNetworked(bool value)
    {
        if (IsServer)
            _networkCanPickUp.Value = value;
        else
            SetCanPickUpServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetCanPickUpServerRpc(bool value) => _networkCanPickUp.Value = value;

    /// <summary>Resets the box to its closed, non-pickable state for a fresh delivery.</summary>
    [ClientRpc]
    public void ResetForDeliveryClientRpc()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
    }

    /// <summary>Immediately enables interaction components on all clients, bypassing NetworkVariable latency.</summary>
    [ClientRpc]
    public void FinalizeDeliveryClientRpc()
    {
        SetInteractable(true);
        canPickUp = true;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        if (canPickUp)
        {
            base.Interact(player);
        }
        else
        {
            if (!isOpen)
            {
                OpenBox();
            }
        }
    }

    public override void SetInteractable(bool value)
    {
        base.SetInteractable(value);
        
        // Explicitly ensure the BoxCollider and HighlightEffect are enabled/disabled.
        // This directly addresses the requirement for the delivery finalization.
        if (TryGetComponent(out BoxCollider boxCollider))
        {
            boxCollider.enabled = value;
        }

        if (TryGetComponent(out HighlightEffect highlight))
        {
            highlight.enabled = value;
        }
    }

    void OpenBox()
    {
        isOpen = true;
        if (contents != null)
            contents.SetActive(true);
        if (boxAnimation != null)
            boxAnimation.Play();
    }
}
