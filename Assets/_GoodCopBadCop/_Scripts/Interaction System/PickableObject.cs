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

    /// <summary>Returns true if any player is currently holding this object.</summary>
    public bool IsHeld => _holdingClientId.Value != ulong.MaxValue;

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

    /// <summary>
    /// Transfers NetworkObject ownership to the requesting client and simultaneously
    /// registers them as the holder, eliminating the need for a separate ClaimHolderServerRpc call.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestOwnershipServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        NetworkObject.ChangeOwnership(clientId);
        _holdingClientId.Value = clientId;
    }

    /// <summary>Returns NetworkObject ownership back to the server.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReleaseOwnershipServerRpc()
    {
        NetworkObject.RemoveOwnership();
    }

    /// <summary>
    /// Places this object into a slot that is a child of another NetworkObject (e.g. a folder slot).
    /// NT stays disabled on all clients; the ParentConstraint is applied to the resolved slot so
    /// the document follows the folder everywhere — on the server and all observer clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PlaceInSlotServerRpc(NetworkObjectReference slotOwnerRef, string slotRelativePath, Vector3 position, Quaternion rotation)
    {
        RemoveParent();
        transform.position = position;
        transform.rotation = rotation;

        // Prevent NGO from fighting the slot constraint by re-syncing the scene-hierarchy parent.
        NetworkObject.AutoObjectParentSync = false;

        NetworkObject.RemoveOwnership();

        // Disable NT on server — the ParentConstraint will drive position on all clients.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        PlaceInSlotClientRpc(slotOwnerRef, slotRelativePath, position, rotation);
    }

    /// <summary>
    /// Received on all clients. Resolves the slot transform and either registers the document
    /// with FolderController for lag-free LateUpdate following, or falls back to SetParent
    /// (ParentConstraint) for non-folder slot owners.
    /// </summary>
    [ClientRpc]
    private void PlaceInSlotClientRpc(NetworkObjectReference slotOwnerRef, string slotRelativePath, Vector3 position, Quaternion rotation)
    {
        RemoveParent();
        transform.position = position;
        transform.rotation = rotation;

        // Prevent NGO from re-syncing the scene-hierarchy parent and fighting the constraint.
        NetworkObject.AutoObjectParentSync = false;

        // Keep NT disabled — position is driven on all clients without NetworkTransform.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        // Stay kinematic while in the slot; AutoObjectParentSync stays false so
        // NGO does not try to replicate the local constraint-driven parent.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        if (!slotOwnerRef.TryGet(out NetworkObject slotOwner)) return;

        Transform slot = string.IsNullOrEmpty(slotRelativePath)
            ? slotOwner.transform
            : slotOwner.transform.Find(slotRelativePath);

        if (slot == null)
        {
            Debug.LogWarning($"PlaceInSlotClientRpc: could not find slot '{slotRelativePath}' on {slotOwner.name}");
            return;
        }

        // Prefer LateUpdate-based following when the slot owner is a FolderController.
        // FolderController.LateUpdate runs at execution order 1 — after PlayerPickupController
        // (order 0) has moved the folder — so documents are always in sync with zero lag.
        // ParentConstraint evaluation happens before LateUpdate and would always be one
        // frame behind when the folder is held, so we skip SetParent in this case.
        FolderController folder = slotOwner.GetComponent<FolderController>();
        if (folder != null)
        {
            folder.RegisterLocalDocument(this, slot);
        }
        else
        {
            SetParent(slot);
        }
    }

    /// <summary>
    /// Sets the world position and rotation on the server before releasing ownership,
    /// so NetworkTransform propagates the correct drop location to all clients.
    /// After setting the authoritative transform, a ClientRpc broadcasts the drop position
    /// to every client so they can position the object correctly before re-enabling NT —
    /// preventing NT from snapping to its stale interpolation buffer (the held position at
    /// pickup time, which may differ if the character moved while carrying the object).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DropServerRpc(Vector3 position, Quaternion rotation)
    {
        RemoveParent();

        transform.position = position;
        transform.rotation = rotation;

        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;

        NetworkObject.RemoveOwnership();

        // Tell every client the exact drop position so they set it before re-enabling NT.
        DropBroadcastClientRpc(position, rotation);
    }

    /// <summary>
    /// Received on all clients after a free drop. Sets the drop position before re-enabling NT
    /// so NT never has a chance to interpolate from its stale pre-pickup buffer position.
    /// Also restores physics and parent sync that were suppressed during the hold.
    /// </summary>
    [ClientRpc]
    private void DropBroadcastClientRpc(Vector3 position, Quaternion rotation)
    {
        RemoveParent();
        transform.position = position;
        transform.rotation = rotation;

        NetworkObject.AutoObjectParentSync = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;
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

    /// <summary>
    /// Activates the ParentConstraint to track the given transform with zero offset.
    /// Call this after pre-positioning the object at the source world transform so
    /// the constraint locks in at exactly the source location.
    /// </summary>
    public void SetParent(Transform parent)
    {
        ConstraintSource source = new ConstraintSource();
        RemoveParent();
        source.sourceTransform = parent;
        source.weight = 1;
        _parentConstraint.AddSource(source);
        _parentConstraint.SetTranslationOffset(0, Vector3.zero);
        _parentConstraint.SetRotationOffset(0, Vector3.zero);
        _parentConstraint.constraintActive = true;
    }

    /// <summary>Deactivates the ParentConstraint and removes all sources.</summary>
    public void RemoveParent()
    {
        transform.parent = null;
        _parentConstraint.constraintActive = false;
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

        // IsHeldByOtherPlayer relies on a NetworkVariable that only updates after a server
        // round-trip. To close the race window where two players click the same object in the
        // same frame, also reject the interaction if the object's colliders are already
        // disabled — they are disabled locally and immediately in PickUpObject before the RPC
        // lands, acting as an optimistic lock visible to both players on the same client.
        if (IsHeldByOtherPlayer) return;
        if (!IsInteractable()) return;

        player.pickupController.PickUpObject(this);
    }

    /// <summary>
    /// Returns true if at least one of this object's own colliders is currently enabled,
    /// meaning no one has yet claimed an optimistic local lock on it.
    /// </summary>
    private bool IsInteractable()
    {
        foreach (Collider col in GetComponents<Collider>())
        {
            if (col.enabled) return true;
        }

        if (interactableColliders.Length > 0)
        {
            foreach (var ic in interactableColliders)
            {
                if (ic.GetComponent<Collider>().enabled) return true;
            }
        }

        return false;
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
            if (interactableCollider == null) continue;
            // Disable both the physics Collider and the InteractableCollider MonoBehaviour.
            // PlayerInteractionController resolves the Interactable from InteractableCollider
            // and gates on interactable.enabled — if the collider component itself is disabled
            // the raycast misses it, but disabling the MonoBehaviour is a belt-and-suspenders
            // guard that also prevents hover-highlighting non-interactable child objects.
            interactableCollider.enabled = value;
            interactableCollider.GetComponent<Collider>().enabled = value;
        }
    }

    /// <summary>
    /// Rebuilds the cached interactable collider list from current children.
    /// Call this after a child object is detached so stale despawned references are cleared.
    /// </summary>
    protected void RefreshInteractableColliders()
    {
        interactableColliders = GetComponentsInChildren<InteractableCollider>(true);
    }

    /// <summary>
    /// Replaces the interactable collider cache with an explicit array.
    /// Use this when the default GetComponentsInChildren scan would include colliders
    /// from child objects that manage their own interactability independently.
    /// </summary>
    protected void OverrideInteractableColliders(InteractableCollider[] colliders)
    {
        interactableColliders = colliders;
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