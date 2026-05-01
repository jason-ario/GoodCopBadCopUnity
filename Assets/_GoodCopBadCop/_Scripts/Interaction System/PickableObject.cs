using System;
using HighlightPlus;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;

[RequireComponent(typeof(ParentConstraint))]
[RequireComponent(typeof(NetworkTransform))]
public class PickableObject : Interactable
{
    // Virtual methods allow overriding
    MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;
    protected PlayerPickupController playerPickupController;
    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    [SerializeField] AudioClip pickupSound;
    [SerializeField] AudioClip putDownSound;
    private ParentConstraint _parentConstraint;
    private InteractableCollider[] interactableColliders = Array.Empty<InteractableCollider>();
    public UnityAction OnEquip;
    public UnityAction OnUnEquip;
    protected bool isUsing;

    public bool CanPickUpManually { get; set; } = true;

    /// <summary>
    /// The client ID of the player currently holding this object.
    /// Set to ulong.MaxValue when no one is holding it.
    /// </summary>
    private NetworkVariable<ulong> _holdingClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Returns true if another player (not the local client) is currently holding this object.</summary>
    public bool IsHeldByOtherPlayer => _holdingClientId.Value != ulong.MaxValue &&
                                       _holdingClientId.Value != NetworkManager.Singleton.LocalClientId;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _holdingClientId.OnValueChanged += OnHoldingClientChanged;

        // Sync collider state to the current network value on late-joining clients.
        SetInteractable(_holdingClientId.Value == ulong.MaxValue);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _holdingClientId.OnValueChanged -= OnHoldingClientChanged;
    }

    private void OnHoldingClientChanged(ulong previous, ulong current)
    {
        SetInteractable(current == ulong.MaxValue);
    }

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        interactableColliders = GetComponentsInChildren<InteractableCollider>(true);
        _parentConstraint = GetComponent<ParentConstraint>();
    }

    /// <summary>Registers the caller as the player holding this object on the server.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ClaimHolderServerRpc(ServerRpcParams rpcParams = default)
    {
        _holdingClientId.Value = rpcParams.Receive.SenderClientId;
    }

    /// <summary>Clears the holder registration on the server, making the object free to grab.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReleaseHolderServerRpc()
    {
        _holdingClientId.Value = ulong.MaxValue;
    }

    /// <summary>Transfers NetworkObject ownership to the requesting client.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestOwnershipServerRpc(ServerRpcParams rpcParams = default)
    {
        NetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
    }

    /// <summary>Returns NetworkObject ownership back to the server.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReleaseOwnershipServerRpc()
    {
        NetworkObject.RemoveOwnership();
    }

    /// <summary>
    /// Sets the world position and rotation on the server before releasing ownership,
    /// so NetworkTransform propagates the correct drop location to all clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DropServerRpc(Vector3 position, Quaternion rotation)
    {
        transform.position = position;
        transform.rotation = rotation;
        NetworkObject.RemoveOwnership();
    }

    public virtual void OnPickedUp()
    {
        if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
        // Do NOT despawn — reparenting is handled by PlayerPickupController
    }

    public virtual void OnDropped()
    {
        if (putDownSound != null)
        {
            SFXController.Instance.Play(putDownSound);
        } else if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
    }

    public void SetParent(Transform parent)
    {
        ConstraintSource source = new ConstraintSource();
        RemoveParent();
        source.sourceTransform = parent;
        source.weight = 1;
        _parentConstraint.AddSource(source);
        _parentConstraint.constraintActive = true;
    }

    public void RemoveParent()
    {
        transform.parent = null;
        if (_parentConstraint.sourceCount > 0)
        {
            _parentConstraint.RemoveSource(0);
        }
    }

    public virtual void OnEquipped(PlayerPickupController player)
    {
        SetInteractable(false);

        playerPickupController = player;

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, true);
        }
        
        OnEquip?.Invoke();
    }
    
    public virtual void OnUnequip(PlayerPickupController player)
    {
        SetInteractable(true);
        
        if (isUsing)
        {
            OnStopUse();
        }

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, false);
        }

        if (itemData.usesTwoArms)
        {
            player.PlayerAnimationController.DisableLeftArmMask();
            player.PlayerAnimationController.DisableRightArmMask();
        }
        
        OnUnEquip?.Invoke();
    }
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        if (!CanPickUpManually) return;
        if (IsHeldByOtherPlayer) return;
        player.pickupController.PickUpObject(this);
    }

    public void SetInteractable(bool value)
    {
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = value;
        }

        if (interactableColliders.Length <= 0) return;
        foreach (var interactableCollider in interactableColliders)
        {
            interactableCollider.GetComponent<Collider>().enabled = value;
        }
    }

    public virtual void OnStartUse()
    {
        isUsing = true;
    }
    
    public virtual void OnBodyStartUse()
    {
        
    }

    public virtual void OnBodyStopUse()
    {
    }
    
    
    public virtual void OnStopUse()
    {
        isUsing = false;
    }

    public void OnDroppedFromBody()
    {
      
    }


    public void SetPlacementClone()
    {
        _parentConstraint.enabled = false;
    }
}