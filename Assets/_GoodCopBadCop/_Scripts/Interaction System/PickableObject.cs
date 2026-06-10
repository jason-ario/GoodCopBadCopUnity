using System;
using HighlightPlus;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;

[RequireComponent(typeof(ParentConstraint))]
public class PickableObject : Interactable
{
    // Virtual methods allow overriding
    protected MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;
    protected PlayerPickupController playerPickupController;
    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    private ParentConstraint _parentConstraint;
    private SocketFollow _socketFollow;
    private InteractableCollider[] interactableColliders = Array.Empty<InteractableCollider>();
    public UnityAction OnEquip;
    public UnityAction OnUnEquip;
    protected bool isUsing;

    public bool CanPickUpManually { get; set; } = true;

    /// <summary>
    /// When true, colliders are permanently disabled regardless of holder network-variable
    /// changes. Use <see cref="LockInteractable"/> / <see cref="UnlockInteractable"/> to
    /// set this — for tutorial-only scenarios where an object must stay non-interactable
    /// after being filed into a folder.
    /// </summary>
    private bool _interactableLocked;

    /// <summary>
    /// Permanently disables this object's colliders so that no subsequent holder
    /// network-variable change can re-enable them. Call <see cref="UnlockInteractable"/>
    /// to restore normal behaviour.
    /// </summary>
    public void LockInteractable()
    {
        _interactableLocked = true;
        SetInteractable(false);
    }

    /// <summary>Clears the permanent interactable lock and re-enables colliders.</summary>
    public void UnlockInteractable()
    {
        _interactableLocked = false;
        SetInteractable(true);
    }

    /// <summary>
    /// The client ID of the player currently holding this object.
    /// Set to ulong.MaxValue when no one is holding it.
    /// </summary>
    private NetworkVariable<ulong> _holdingClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Authoritative interactable state set by tutorial logic.
    /// Stored as a NetworkVariable so late-joining clients inherit the correct state.
    /// -1 = unset (defer to _holdingClientId logic), 0 = forced off, 1 = forced on.
    /// </summary>
    private NetworkVariable<int> _networkInteractableOverride = new NetworkVariable<int>(
        -1,
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
        _holdingClientId.OnValueChanged             += OnHoldingClientChanged;
        _networkInteractableOverride.OnValueChanged += OnNetworkInteractableOverrideChanged;

        // Apply tutorial override first; fall back to holder-based logic if unset.
        ApplyNetworkInteractableState();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _holdingClientId.OnValueChanged             -= OnHoldingClientChanged;
        _networkInteractableOverride.OnValueChanged -= OnNetworkInteractableOverrideChanged;
    }

    private void OnHoldingClientChanged(ulong previous, ulong current)
    {
        if (_interactableLocked) return;
        // Only apply holder-based logic when no tutorial override is active.
        if (_networkInteractableOverride.Value == -1)
            SetInteractable(current == ulong.MaxValue);
    }

    private void OnNetworkInteractableOverrideChanged(int previous, int current)
        => ApplyNetworkInteractableState();

    private void ApplyNetworkInteractableState()
    {
        if (_networkInteractableOverride.Value != -1)
        {
            _interactableLocked = _networkInteractableOverride.Value == 0;
            SetInteractable(_networkInteractableOverride.Value == 1);
        }
        else
        {
            _interactableLocked = false;
            SetInteractable(_holdingClientId.Value == ulong.MaxValue);
        }
    }

    /// <summary>
    /// Sets interactability on all clients via the server so the state persists
    /// for late-joiners. Prefer this over <see cref="SetInteractable"/> for tutorial gates.
    /// </summary>
    public void SetInteractableNetworked(bool value)
    {
        if (IsServer)
        {
            _networkInteractableOverride.Value = value ? 1 : 0;
        }
        else
        {
            // Guard: RPCs require the NetworkObject to be spawned. If called before
            // NGO has registered this scene object on the client (e.g. during
            // StartGameClientRpc → StartCampaign), skip silently — the server will
            // set the authoritative NetworkVariable value regardless.
            if (!IsSpawned) return;
            SetInteractableNetworkedServerRpc(value);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetInteractableNetworkedServerRpc(bool value)
        => _networkInteractableOverride.Value = value ? 1 : 0;

    /// <summary>
    /// Permanently disables interactability on all clients via the server.
    /// Use <see cref="UnlockInteractableNetworked"/> to restore normal behaviour.
    /// </summary>
    public void LockInteractableNetworked()
    {
        if (IsServer)
            _networkInteractableOverride.Value = 0;
        else
            LockInteractableNetworkedServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void LockInteractableNetworkedServerRpc()
        => _networkInteractableOverride.Value = 0;

    /// <summary>
    /// Clears the networked interactable lock and restores holder-based logic on all clients.
    /// </summary>
    public void UnlockInteractableNetworked()
    {
        if (IsServer)
            _networkInteractableOverride.Value = -1;
        else
            UnlockInteractableNetworkedServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnlockInteractableNetworkedServerRpc()
        => _networkInteractableOverride.Value = -1;

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        interactableColliders = GetComponentsInChildren<InteractableCollider>(true);
        _parentConstraint = GetComponent<ParentConstraint>();
        _socketFollow = GetComponent<SocketFollow>();
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
    /// Asks the server to despawn this object. Safe to call from any client —
    /// the server validates the object is still spawned before despawning.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DespawnServerRpc()
    {
        NetworkHelper.Despawn(NetworkObject);
    }

    /// <summary>
    /// Places this object into a slot that is a child of another NetworkObject (e.g. a folder slot).
    /// NT stays disabled on all clients; the ParentConstraint is applied to the resolved slot so
    /// the document follows the folder everywhere — on the server and all observer clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void PlaceInSlotServerRpc(NetworkObjectReference slotOwnerRef, string slotRelativePath, Vector3 position, Quaternion rotation)
    {
        PlaceInSlotFromServer(slotOwnerRef, slotRelativePath, position, rotation);
    }

    /// <summary>
    /// Server-only equivalent of PlaceInSlotServerRpc. Performs the server-side slot work and
    /// broadcasts PlaceInSlotClientRpc to all clients. Call this when already executing on the
    /// server (e.g. from within another ServerRpc or any server-only method) to avoid the
    /// ServerRpc code-gen wrapper suppressing the subsequent ClientRpc broadcast.
    /// </summary>
    public void PlaceInSlotFromServer(NetworkObjectReference slotOwnerRef, string slotRelativePath, Vector3 position, Quaternion rotation)
    {
        Debug.Assert(IsServer, $"[PickableObject] PlaceInSlotFromServer called on non-server for {name}");

        RemoveParent();

        // Prevent NGO from fighting the slot constraint by re-syncing the scene-hierarchy parent.
        NetworkObject.AutoObjectParentSync = false;

        // Escape any NGO-established parent hierarchy (e.g. when spawned as a notebook child
        // via TrySetParent). Must be done after disabling AutoObjectParentSync so NGO does not
        // replicate this local detach — each client handles it independently in the ClientRpc.
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: true);

        transform.position = position;
        transform.rotation = rotation;

        NetworkObject.RemoveOwnership();

        // Disable NT on server — the ParentConstraint will drive position on all clients.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        Debug.Log($"[PickableObject] PlaceInSlotFromServer: broadcasting PlaceInSlotClientRpc for {name} slotPath='{slotRelativePath}'");
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
        Debug.Log($"[PlaceInSlotClientRpc] Received on client {NetworkManager.Singleton.LocalClientId} for {name} | slotRelativePath='{slotRelativePath}' | currentParent={transform.parent?.name ?? "none"}");

        RemoveParent();

        // Prevent NGO from re-syncing the scene-hierarchy parent and fighting the constraint.
        NetworkObject.AutoObjectParentSync = false;

        // Escape any NGO-established parent hierarchy (e.g. when this page was spawned as a
        // child of the notebook via TrySetParent). Must happen after disabling AutoObjectParentSync
        // so NGO treats this as a local-only detach and does not replicate it.
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: true);

        transform.position = position;
        transform.rotation = rotation;

        // Keep NT disabled — position is driven on all clients without NetworkTransform.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        // Stay kinematic while in the slot; AutoObjectParentSync stays false so
        // NGO does not try to replicate the local constraint-driven parent.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        if (!slotOwnerRef.TryGet(out NetworkObject slotOwner))
        {
            Debug.LogError($"[PlaceInSlotClientRpc] Could not resolve slotOwnerRef on client {NetworkManager.Singleton.LocalClientId} for {name}");
            return;
        }

        Transform slot = string.IsNullOrEmpty(slotRelativePath)
            ? slotOwner.transform
            : slotOwner.transform.Find(slotRelativePath);

        if (slot == null)
        {
            Debug.LogWarning($"PlaceInSlotClientRpc: could not find slot '{slotRelativePath}' on {slotOwner.name}");
            return;
        }

        // Use SocketFollow for folder slots so documents track the slot at execution order 2,
        // after PlayerPickupController (order 1) has already moved the folder to its final
        // pitched position. ParentConstraint evaluates before LateUpdate and would lag one frame.
        FolderController folder = slotOwner.GetComponent<FolderController>();
        if (folder != null)
        {
            Debug.Log($"[PlaceInSlotClientRpc] SocketFollow {name} → slot={slot.name} on client {NetworkManager.Singleton.LocalClientId}");
            SetSocketFollow(slot);
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
        ClearSocketFollow();
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
        if (itemData != null && itemData.PickupSound != null)
        {
            SFXController.Instance.PlayAtPosition(itemData.PickupSound, transform.position);
        }
        // Do NOT despawn — reparenting is handled by PlayerPickupController
    }

    public virtual void OnDropped()
    {
        if (itemData != null && itemData.PickupSound != null)
        {
            SFXController.Instance.PlayAtPosition(itemData.PickupSound, transform.position);
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
        _parentConstraint.constraintActive = false;
        if (_parentConstraint.sourceCount > 0)
        {
            _parentConstraint.RemoveSource(0);
        }
    }

    /// <summary>
    /// Enables the <see cref="SocketFollow"/> component on this object and points it at
    /// <paramref name="slot"/> so it tracks the slot's world transform every LateUpdate.
    /// Creates the component lazily if the prefab does not already have one.
    /// </summary>
    public void SetSocketFollow(Transform slot)
    {
        if (_socketFollow == null)
            _socketFollow = gameObject.AddComponent<SocketFollow>();

        _socketFollow.SetTarget(slot);
        _socketFollow.enabled = true;
    }

    /// <summary>
    /// Enables the <see cref="SocketFollow"/> component and configures it to follow
    /// <paramref name="source"/> using a fixed local-space offset rather than reading a
    /// child transform's world position. Position is computed via
    /// <c>source.TransformPoint(localPosition)</c> and rotation via
    /// <c>source.rotation * localRotation</c>, so the result always reflects the source's
    /// definitive per-frame transform — including pitch applied after animation rigging.
    /// Creates the component lazily if the prefab does not already have one.
    /// </summary>
    public void SetSocketFollowWithLocalOffset(Transform source, Vector3 localPosition, Quaternion localRotation)
    {
        if (_socketFollow == null)
            _socketFollow = gameObject.AddComponent<SocketFollow>();

        _socketFollow.SetTargetWithLocalOffset(source, localPosition, localRotation);
        _socketFollow.enabled = true;
    }

    /// <summary>
    /// Disables and clears the <see cref="SocketFollow"/> component so this object stops
    /// tracking a folder slot. Call this on drop or when the slot is no longer valid.
    /// </summary>
    public void ClearSocketFollow()
    {
        if (_socketFollow == null) return;
        _socketFollow.SetTarget(null);
        _socketFollow.enabled = false;
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
        if (!_interactableLocked)
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

    public virtual void SetInteractable(bool value)
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