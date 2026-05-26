using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Serialization;

// LateUpdate must run after PlayerAnimationController (default order 0) so the world object
// is snapped to the body arm container after pitch bones have already been applied.
[DefaultExecutionOrder(1)]
public class PlayerPickupController : NetworkBehaviour
{
    public Transform holdPoint;
    public float holdSmoothness = 10f;

    public PickableObject HeldObject => _heldObject;
    private PickableObject _heldObject; // the actual world instance being carried
    private PickableObject _camEquippedItem;
    private PickableObject _bodyCurrentlyEquippedItem;

    public bool IsHoldingObject => HeldObject != null;
    private PlayerAnimationController _playerAnimationController;
    public PlayerAnimationController PlayerAnimationController => _playerAnimationController;
    private PlayerInteractionController _playerInteractionController;
    
    [SerializeField] ObjectContainer[] objectContainers;
    [FormerlySerializedAs("camObjectContainer")] [FormerlySerializedAs("objectContainerToUse")] 
    [SerializeField] private ObjectContainer rightArmCamObjectContainer; 
    [SerializeField] private ObjectContainer leftArmCamObjectContainer; 
    [FormerlySerializedAs("bodyObjectContainer")] [SerializeField] private ObjectContainer rightArmBodyObjectContainer; 
    [SerializeField] private ObjectContainer leftArmBodyObjectContainer; 

    public ObjectContainer RightArmCamObjectContainer => rightArmCamObjectContainer;
    public ObjectContainer RightArmBodyObjectContainer => rightArmBodyObjectContainer;
    public ObjectContainer LeftArmBodyObjectContainer => leftArmBodyObjectContainer;
    public ObjectContainer LeftArmCamObjectContainer => leftArmCamObjectContainer;


    private PlayerMovementController _playerMovementController;
    public PlayerMovementController PlayerMovementController => _playerMovementController;

    private NetworkVariable<int> itemEquippedIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>
    /// Broadcasts which world NetworkObject is currently held so non-owner clients
    /// can constrain it to the body arm container instead of the camera arm container.
    /// </summary>
    private NetworkVariable<NetworkObjectReference> _heldObjectRef = new NetworkVariable<NetworkObjectReference>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private float pickUpUseCooldownTimer = .2f;
    private bool pickUpCooldownComplete = false;

    public UnityAction OnPlaceObject;

    [SerializeField] Transform leftHandSocket;
    public Transform LeftHandSocket => leftHandSocket;
    
    private bool _canPickUpAndPlace = true;
    public bool CanPickUpAndPlace
    {
        get => _canPickUpAndPlace;
        set => _canPickUpAndPlace = value;
    }

    // Non-owner LateUpdate follow: world object tracks this target each frame.
    private PickableObject _followWorldObj;
    private Transform _followTarget;
    private NetworkTransform _followNT;

    private void Awake()
    {
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        objectContainers = GetComponentsInChildren<ObjectContainer>(true);
        itemEquippedIndex.OnValueChanged += OnItemValueChanged;
        _heldObjectRef.OnValueChanged += OnHeldObjectRefChanged;
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = gameObject.GetComponent<PlayerInteractionController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Late-join sync: if both NetworkVariables already hold valid pickup state,
        // reconstruct the body-arm follow immediately without waiting for OnValueChanged.
        if (!IsOwner && itemEquippedIndex.Value >= 0 && _heldObjectRef.Value.NetworkObjectId != 0)
        {
            // Trigger the item-equipped path so containers and body item are set up first,
            // then apply the world-object follow.
            OnItemValueChanged(-1, itemEquippedIndex.Value);
            ApplyBodyConstraint();
        }
    }

    private void Start()
    {
        if (!IsOwner)
        {
            foreach (var objectContainer in objectContainers)
            {
                objectContainer.SetClientLayers();
            }  
        }    
    }

    private void OnItemValueChanged(int previousValue, int newValue)
    {
        Debug.Log("Player Pickup Controller: OnItemValueChanged");
        if (newValue == -1)
        {
            foreach (var objectContainer in objectContainers)
            {
                objectContainer.UnequipItem(this);
                _playerAnimationController.SetLeftArmRigWeightSmooth(0, .2f);
                _playerAnimationController.SetRightArmRigWeightSmooth(0, .2f);
                _playerAnimationController.SetAimRigWeightSmooth(0, .2f);
            }

            return;
        }
        
        PickableItemData itemData = rightArmCamObjectContainer.GetItemData(newValue);

        // Route to the correct body container based on which hand the item belongs to.
        ObjectContainer targetBodyContainer = itemData.hand == PickableItemData.Hand.Left
            ? leftArmBodyObjectContainer
            : rightArmBodyObjectContainer;

        targetBodyContainer.EquipItem(itemData, this);

        _bodyCurrentlyEquippedItem = targetBodyContainer.CurrentlyEquippedItem;

        // Non-owner clients constrain the world object to the body arm so they see it there.
        // (Owner already constrained it to the camera arm in PickUpObject.)
        // Called here regardless of _heldObjectRef order — ApplyBodyConstraint guards
        // internally against missing refs and will no-op if _heldObjectRef hasn't arrived yet;
        // OnHeldObjectRefChanged will call it again once that variable syncs.
        if (!IsOwner)
            ApplyBodyConstraint();

        if (itemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(1, .2f);
            if (IsOwner)
                _playerAnimationController.CamRightArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().rightIKTarget;
            _playerAnimationController.RightArmIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().rightIKTarget;
        }
        else
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(0, .2f);
            _playerAnimationController.RightArmIKTarget = null;
        }

        if (itemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(1, .2f);
            if (IsOwner)
                _playerAnimationController.CamLeftArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().leftIKTarget;
            _playerAnimationController.LeftArmIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().leftIKTarget;
        }
        else
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(0, .2f);
            _playerAnimationController.LeftArmIKTarget = null;
        }
        
        if (itemData.useAimIK)
        {
            _playerAnimationController.SetAimRigWeightSmooth(1, .2f);
        }
        else
        {
            _playerAnimationController.SetAimRigWeightSmooth(0, .2f);
        }
        
        if (itemData.usesTwoArms)
        {
            _playerAnimationController.EnableHoldObjectTwoArmsMask();
        }
        else
        {
            _playerAnimationController.EnableRightArmMask();
        }
    }

    void Update()
    {
        if (IsLocalPlayer == false)
        {
            return;
        }

        if(CanPickUpAndPlace == false) return;

        if (HeldObject != null)
        {
            // Release right-click: place the held object only if the placer is active and in range
            if (Input.GetMouseButtonUp(1) && !Input.GetMouseButton(0))
            {
                if (HeldObject == null)
                {
                    Debug.Log("HeldObject is null");
                    return;
                }

                if (HeldObject.ItemData.canUsePlacementBoard == false)
                {
                    Debug.Log("Can't use placement board");
                    return;
                }
                
                if ((ObjectPlacer.Instance.IsActive && ObjectPlacer.Instance.IsInRange) || (ObjectPlacer.Instance.deactivatedThisFrame && ObjectPlacer.Instance.WasInRangeWhenDeactivated))
                {
                    DropObject();
                }
            }
            
            if (Input.GetMouseButtonUp(0) && pickUpCooldownComplete)
            {
                StopUsingObject();
            }
        }
    }

    private void LateUpdate()
    {
        SyncWorldObjectToBody();
    }

    /// <summary>
    /// Snaps the held world object to the body arm target's current world transform.
    /// Called both from LateUpdate and explicitly by PlayerAnimationController after
    /// bone pitch manipulation, so the held object always reflects the final bone state
    /// regardless of script execution order.
    /// </summary>
    public void SyncWorldObjectToBody()
    {
        if (_followWorldObj == null || _followTarget == null) return;

        // NT being re-enabled signals a drop has occurred (from DropServerRpc on the server,
        // or DropBroadcastClientRpc on clients). Stop the follow immediately so we don't
        // overwrite the drop position that NT is about to broadcast.
        if (_followNT != null && _followNT.enabled)
        {
            _followWorldObj = null;
            _followTarget   = null;
            _followNT       = null;
            return;
        }

        _followWorldObj.transform.position = _followTarget.position;
        _followWorldObj.transform.rotation = _followTarget.rotation;
    }

    public void TryUseObject()
    {
        if (Input.GetMouseButtonDown(1)) return;
        if (HeldObject == null) return;
        if(pickUpCooldownComplete == false) return;
        _heldObject.OnStartUse();
        
        RequestBodyUseServerRpc();
    }

    [ServerRpc]
    private void RequestBodyUseServerRpc()
    {
        RequestBodyUseClientRpc();
    }

    [ClientRpc]
    private void RequestBodyUseClientRpc()
    {
        if (rightArmBodyObjectContainer.CurrentlyEquippedItem != null)
        {
            rightArmBodyObjectContainer.CurrentlyEquippedItem.OnBodyStartUse();
        }
        
        if (leftArmBodyObjectContainer.CurrentlyEquippedItem != null)
        {
            leftArmBodyObjectContainer.CurrentlyEquippedItem.OnBodyStartUse();
        }
    }

    void StopUsingObject()
    {
        if(_heldObject != null)
        {
            _heldObject.OnStopUse();
        }

        RequestBodyStopUseServerRpc();
    }

    [ServerRpc]
    private void RequestBodyStopUseServerRpc()
    {
        RequestBodyStopUseClientRpc();
    }

    [ClientRpc]
    private void RequestBodyStopUseClientRpc()
    {
        if (rightArmBodyObjectContainer.CurrentlyEquippedItem != null)
        {
            rightArmBodyObjectContainer.CurrentlyEquippedItem.OnBodyStopUse();
        }
        
               
        if (leftArmBodyObjectContainer.CurrentlyEquippedItem != null)
        {
            leftArmBodyObjectContainer.CurrentlyEquippedItem.OnBodyStopUse();
        }
    }

    public void SpawnAndPickUp(PickableItemData itemData, Transform spawnPos)
    {
        if (!IsOwner) return;
        if (HeldObject != null) return;
        if (itemData == null || itemData.PickUpPrefab == null) return;

        int itemIndex = ItemDatabase.Instance.GetItemIndex(itemData);
        if (itemIndex < 0)
        {
            Debug.LogError($"Could not find item index for {itemData.name}");
            return;
        }

        SpawnAndPickUpServerRpc(itemIndex, spawnPos.position, spawnPos.rotation);
    }
    
    [ServerRpc]
    private void SpawnAndPickUpServerRpc(int itemIndex, Vector3 position, Quaternion rotation, ServerRpcParams rpcParams = default)
    {
        PickableItemData itemData = ItemDatabase.Instance.GetItemByIndex(itemIndex);
        if (itemData == null || itemData.PickUpPrefab == null)
        {
            Debug.LogError($"SpawnAndPickUpServerRpc failed: invalid item index {itemIndex}");
            return;
        }

        GameObject spawnedObject = Instantiate(itemData.PickUpPrefab, position, rotation);

        NetworkObject networkObject = spawnedObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError($"Spawned pickup prefab {itemData.name} has no NetworkObject component.");
            Destroy(spawnedObject);
            return;
        }

        networkObject.Spawn(true);

        ulong ownerClientId = rpcParams.Receive.SenderClientId;

        SpawnAndPickUpClientRpc(
            new NetworkObjectReference(networkObject),
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { ownerClientId }
                }
            }
        );
    }

    [ClientRpc]
    private void SpawnAndPickUpClientRpc(NetworkObjectReference objectRef, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        if (objectRef.TryGet(out NetworkObject networkObject))
        {
            PickableObject pickableObject = networkObject.GetComponent<PickableObject>();
            if (pickableObject == null)
            {
                Debug.LogError("Spawned network object does not have a PickableObject component.");
                return;
            }

            PickUpObject(pickableObject);
        }
        else
        {
            Debug.LogWarning("Failed to resolve spawned pickup NetworkObjectReference on client.");
        }
    }

    /// <summary>
    /// Initiates a shop purchase: deducts coupons on the server and spawns + equips the item.
    /// Validates ownership, held state, item data, and available funds before proceeding.
    /// </summary>
    public void PurchaseAndPickUp(PickableItemData itemData, int price, Transform spawnPos)
    {
        if (!IsOwner) return;
        if (HeldObject != null) return;
        if (itemData == null || itemData.PickUpPrefab == null) return;

        int itemIndex = ItemDatabase.Instance.GetItemIndex(itemData);
        if (itemIndex < 0)
        {
            Debug.LogError($"PlayerPickupController: Could not find item index for {itemData.name}");
            return;
        }

        PurchaseAndPickUpServerRpc(itemIndex, price, spawnPos.position, spawnPos.rotation);
    }

    [ServerRpc]
    private void PurchaseAndPickUpServerRpc(int itemIndex, int price, Vector3 position, Quaternion rotation, ServerRpcParams rpcParams = default)
    {
        if (GlobalHostVariables.Instance == null)
        {
            Debug.LogError("PurchaseAndPickUpServerRpc: GlobalHostVariables not found.");
            return;
        }

        bool spent = GlobalHostVariables.Instance.SubtractMoney(price);
        if (!spent)
        {
            PurchaseFailedClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            });
            return;
        }

        PickableItemData itemData = ItemDatabase.Instance.GetItemByIndex(itemIndex);
        if (itemData == null || itemData.PickUpPrefab == null)
        {
            Debug.LogError($"PurchaseAndPickUpServerRpc: invalid item index {itemIndex}");
            // Refund since we already subtracted.
            GlobalHostVariables.Instance.AddMoney(price);
            return;
        }

        GameObject spawnedObject = Instantiate(itemData.PickUpPrefab, position, rotation);
        NetworkObject networkObject = spawnedObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError($"Purchased pickup prefab {itemData.name} has no NetworkObject component.");
            Destroy(spawnedObject);
            GlobalHostVariables.Instance.AddMoney(price);
            return;
        }

        // NGO only supports nested NetworkObjects for scene-placed objects, not dynamically spawned ones.
        // The inline ExamPage children in the notebook prefab have a different globalObjectIdHash
        // from the standalone registered page prefabs — clients can't match them and their spawn fails.
        // SpawnAndWirePages instantiates from the registered prefab assets instead, so clients can
        // create matching instances and RPCs/ClientRpcs on those NetworkObjects are delivered correctly.
        ExamNotebook notebook = networkObject.GetComponent<ExamNotebook>();
        if (notebook != null)
        {
            networkObject.Spawn(true);

            var spawnedPages = notebook.SpawnAndWirePages();

            if (spawnedPages.Count > 0)
            {
                var pageRefs = new NetworkObjectReference[spawnedPages.Count];
                for (int i = 0; i < spawnedPages.Count; i++)
                    pageRefs[i] = new NetworkObjectReference(spawnedPages[i]);
                notebook.SetPageReferencesClientRpc(pageRefs);
            }
        }
        else
        {
            // Non-notebook object — plain spawn, no nested NetworkObject handling needed.
            networkObject.Spawn(true);
        }

        SpawnAndPickUpClientRpc(
            new NetworkObjectReference(networkObject),
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            }
        );
    }

    [ClientRpc]
    private void PurchaseFailedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;
        UIController.Instance.ShowShopNotification("Not enough coupons!");
    }

    public void PickUpObject(PickableObject pickableObject)
    {
        if (HeldObject != null)
        {
            return;
        }

        // Disable colliders immediately as an optimistic lock so no other player's raycast
        // can pick up the same object during the network round-trip for ClaimHolderServerRpc.
        // OnEquipped will call SetInteractable(false) again once constraints are established,
        // and DropBroadcastClientRpc / OnUnequip restore it via SetInteractable(true).
        pickableObject.SetInteractable(false);

        // Transfer ownership to this client so NetworkTransform replicates from our side.
        // Ownership RPC also claims the holder in one round-trip (no separate ClaimHolderServerRpc needed).
        pickableObject.RequestOwnershipServerRpc();
        
        PickableItemData itemData = pickableObject.ItemData;
        _heldObject = pickableObject;
        ObjectPlacer.Instance.SetItem(itemData);

        int itemIndex = rightArmCamObjectContainer.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;

        _camEquippedItem = pickableObject;


        if (itemData.hand == PickableItemData.Hand.Right)
        {
            _bodyCurrentlyEquippedItem = rightArmBodyObjectContainer.CurrentlyEquippedItem;
            rightArmCamObjectContainer.EquipItem(itemData, this, pickableObject);
        }
        else
        {
            _bodyCurrentlyEquippedItem = leftArmBodyObjectContainer.CurrentlyEquippedItem;
            leftArmBodyObjectContainer.EquipItem(itemData, this, pickableObject);
        }
        
        

        // Reparent the real world object into the cam container slot
        if (_camEquippedItem != null)
        {
            // Disable any physics so it follows the hand cleanly
            Rigidbody rb = pickableObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            ObjectContainer currentObjectContainer = itemData.hand == PickableItemData.Hand.Right ? rightArmCamObjectContainer : leftArmCamObjectContainer;
            PickableObject itemInContainer = null;
            
            foreach (var item in currentObjectContainer.ItemsHeld)
            {
                if (item.ItemData == itemData)
                    itemInContainer = item;
            }

            // When the picked-up object is a world item (e.g. newspaper) its ItemData is not
            // pre-registered in ItemsHeld, so the loop above finds nothing. In that case the
            // object itself is the slot — fall back to using it directly as its own container.
            itemInContainer ??= pickableObject;

            // Use ParentConstraint to track the slot continuously on all clients.
            // AutoObjectParentSync is disabled so NGO does not replicate the local
            // parent change; each client sets its own arm-appropriate constraint independently.
            // Pre-position the object at the slot's world transform so the constraint
            // activates with a zero offset rather than snapping from wherever it was.
            // Clear any active SocketFollow before establishing the ParentConstraint.
            // If this object was sitting in a folder slot, SocketFollow (execution order 2)
            // runs after ParentConstraint and overrides the hand position every LateUpdate,
            // making the object appear stuck in the folder. Clearing it here is an optimistic
            // local fix; FolderItem.OnEquipped broadcasts the clear to all other clients.
            pickableObject.ClearSocketFollow();
            pickableObject.NetworkObject.AutoObjectParentSync = false;
            pickableObject.transform.position = itemInContainer.transform.position;
            pickableObject.transform.rotation = itemInContainer.transform.rotation;
            pickableObject.SetParent(itemInContainer.transform);
        }

        if (itemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamRightArmRigIKTarget = pickableObject.GetComponent<IkTargets>()?.rightIKTarget ?? _camEquippedItem?.GetComponent<IkTargets>().rightIKTarget;
            _playerAnimationController.RightArmIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().rightIKTarget;
        }
        if (itemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamLeftArmRigIKTarget = pickableObject.GetComponent<IkTargets>()?.leftIKTarget ?? _camEquippedItem?.GetComponent<IkTargets>().leftIKTarget;
            _playerAnimationController.LeftArmIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().leftIKTarget;
        }

        if (itemData.useAimIK)
        {
            _playerAnimationController.SetAimRigWeightSmooth(1, .2f);
        }

        pickableObject.OnPickedUp();
        _heldObjectRef.Value = new NetworkObjectReference(pickableObject.NetworkObject);

        // Disable NetworkTransform while held — ParentConstraint drives position on all
        // clients (camera arm for the owner, body arm for observers), so interpolation
        // from NetworkTransform would only cause lag.
        NetworkTransform nt = pickableObject.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        StartCoroutine(PickUpCoolDown());
    }
    
    IEnumerator PickUpCoolDown()
    {
        yield return new WaitForSeconds(pickUpUseCooldownTimer);
        pickUpCooldownComplete = true;
    }
    
    public void DropObject(Transform dropPoint = null)
    {
        OnPlaceObject?.Invoke();
        pickUpCooldownComplete = false;

        if (_heldObject == null)
        {
            ObjectPlacer.Instance.DeactivatePlacer();
            return;
        }

        GameObject placementItem = ObjectPlacer.Instance.GetPickableObject(_heldObject.ItemData)?.gameObject;
        DisableArmIKs();
        
        if (ObjectPlacer.Instance.PlacementBoard != null)
        {
            ObjectPlacer.Instance.PlacementBoard.OnPlaced(_heldObject);
        }
        
        if (_heldObject != null)
        {
            Vector3 dropPos = dropPoint != null ? dropPoint.position : (placementItem != null ? placementItem.transform.position : _heldObject.transform.position);
            Quaternion dropRot = dropPoint != null ? dropPoint.rotation : (placementItem != null ? placementItem.transform.rotation : _heldObject.transform.rotation);

            // Remove the ParentConstraint and restore sync locally first.
            _heldObject.RemoveParent();
            _heldObject.NetworkObject.AutoObjectParentSync = true;
            _heldObject.transform.position = dropPos;
            _heldObject.transform.rotation = dropRot;

            if (dropPoint != null)
            {
                // Placing the document into a folder slot: instead of re-enabling NT (which
                // would fight the ParentConstraint and leave observers without a constraint),
                // broadcast a slot-constraint to every client so the document follows the
                // folder on all machines while NT stays disabled.
                NetworkObject slotOwnerNetObj = dropPoint.GetComponentInParent<NetworkObject>();
                if (slotOwnerNetObj != null)
                {
                    // Build the relative path of the slot inside its NetworkObject's hierarchy.
                    string slotPath = GetRelativePath(slotOwnerNetObj.transform, dropPoint);
                    _heldObject.PlaceInSlotServerRpc(
                        new NetworkObjectReference(slotOwnerNetObj),
                        slotPath,
                        dropPos,
                        dropRot);

                    // Register with FolderController on the server so it can despawn this
                    // document later. InteractWithItem only runs on the local client, so the
                    // server-side documents list on FolderController is always empty otherwise.
                    FolderController folder = slotOwnerNetObj.GetComponent<FolderController>();
                    if (folder != null)
                        folder.RegisterDocumentServerRpc(new NetworkObjectReference(_heldObject.NetworkObject));
                }
                else
                {
                    // Fallback: treat as a normal drop if the slot has no NetworkObject ancestor.
                    _heldObject.DropServerRpc(dropPos, dropRot);
                }

                _heldObject.SetParent(dropPoint);
            }
            else
            {
                // Send drop position to the server. DropServerRpc sets the authoritative
                // transform and re-enables NetworkTransform there — do NOT re-enable NT here
                // on the client, because NT would immediately interpolate back toward the
                // last server-known position (the held slot) before the RPC lands.
                _heldObject.DropServerRpc(dropPos, dropRot);
            }

            _heldObject.ReleaseHolderServerRpc();
            _heldObject.OnDropped();
        }

        rightArmBodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();
        leftArmBodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }
        
        _camEquippedItem = null;
        _bodyCurrentlyEquippedItem = null;
        _heldObject = null;
        _heldObject = null;
        _heldObjectRef.Value = default;
        itemEquippedIndex.Value = -1;
        _playerAnimationController.DisableRightArmMask();
        
        ObjectPlacer.Instance.DeactivatePlacer();
    }

    /// <summary>
    /// Fired on all clients when the held object reference changes.
    /// Non-owner clients use this to remove the body-arm constraint on drop,
    /// and as a secondary pickup path if _heldObjectRef arrives before itemEquippedIndex.
    /// </summary>
    private void OnHeldObjectRefChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
    {
        if (IsOwner) return;

        bool wasHolding = previousValue.NetworkObjectId != 0;
        bool isHolding  = newValue.NetworkObjectId != 0;

        if (wasHolding && !isHolding)
        {
            // Dropped — remove the body constraint using the previous ref
            RemoveBodyConstraint(previousValue);
        }
        else if (isHolding)
        {
            // Picked up — try to apply now; if body item isn't ready yet,
            // OnItemValueChanged will call ApplyBodyConstraint once it is.
            ApplyBodyConstraint();
        }
    }

    /// <summary>
    /// Registers the world PickableObject and its body-arm target so LateUpdate can
    /// snap it to the target's world transform every frame.
    /// LateUpdate runs after animation, so the target bone is already at its final
    /// position for this frame — no constraint evaluation delay and no parenting side-effects.
    /// </summary>
    private void ApplyBodyConstraint()
    {
        if (_bodyCurrentlyEquippedItem == null) return;
        if (!_heldObjectRef.Value.TryGet(out NetworkObject netObj)) return;

        PickableObject worldObj = netObj.GetComponent<PickableObject>();
        if (worldObj == null) return;

        NetworkTransform nt = worldObj.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        Rigidbody rb = worldObj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        netObj.AutoObjectParentSync = false;

        _followWorldObj = worldObj;
        _followTarget   = _bodyCurrentlyEquippedItem.transform;
        _followNT       = worldObj.GetComponent<NetworkTransform>();
    }

    /// <summary>
    /// Clears the LateUpdate follow fields. All object-level cleanup (RemoveParent, isKinematic,
    /// AutoObjectParentSync) is handled by DropBroadcastClientRpc (free drop) or
    /// PlaceInSlotClientRpc (slot placement) so this method never races with those RPCs.
    /// </summary>
    private void RemoveBodyConstraint(NetworkObjectReference objectRef)
    {
        _followWorldObj = null;
        _followTarget   = null;
        _followNT       = null;
    }

    /// <summary>
    /// Builds a slash-separated relative path from <paramref name="root"/> down to
    /// <paramref name="target"/>, e.g. "Body/Slots/IDCardSlot".
    /// Returns an empty string when target == root.
    /// </summary>
    private static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root) return string.Empty;

        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return string.Join("/", parts);
    }

    void DisableArmIKs(PickableObject target = null)
    {
        PickableObject source = target ?? _heldObject;
        if (source == null) return;

        if (source.ItemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(0,.25f);
            _playerAnimationController.LeftArmIKTarget = null;
        }
        
        if (source.ItemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(0,.25f);
            _playerAnimationController.RightArmIKTarget = null;
        }
    }

    [ServerRpc]
    private void RequestDropServerRpc(int itemIndex, Vector3 position, Quaternion rotation)
    {
        Debug.Log("RequestDropServerRpc");
        // Get the actual data/prefab on the server side using the index
        PickableItemData data = ItemDatabase.Instance.GetItemByIndex(itemIndex);
        if (data == null || data.PickUpPrefab == null)
        {
            Debug.LogError("Failed to find prefab for item index: " + itemIndex);
            return;
        }

        GameObject spawnedPickup = Instantiate(data.PickUpPrefab, position, rotation);

        NetworkObject netObj = spawnedPickup.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
    }

    public void DestroyEquippedItem()
    {
        if (_heldObject == null) return;

        PickableObject heldObject = _heldObject;

        // Unequip from all containers before despawning
        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }

        rightArmBodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();
        leftArmBodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();
        rightArmCamObjectContainer.CurrentlyEquippedItem?.OnDropped();

        // Clear state before despawn so DropObject is never called on a destroyed object
        _camEquippedItem = null;
        _bodyCurrentlyEquippedItem = null;
        heldObject.ReleaseHolderServerRpc();
        _heldObject = null;
        _heldObjectRef.Value = default;
        itemEquippedIndex.Value = -1;

        DisableArmIKs(heldObject);
        _playerAnimationController.DisableRightArmMask();
        ObjectPlacer.Instance.DeactivatePlacer();
        OnPlaceObject?.Invoke();
        pickUpCooldownComplete = false;

        heldObject.DespawnServerRpc();
    }
}
