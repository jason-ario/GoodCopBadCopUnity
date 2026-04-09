using System;
using System.Collections;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Serialization;

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

    private void Awake()
    {
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        objectContainers = GetComponentsInChildren<ObjectContainer>(true);
        itemEquippedIndex.OnValueChanged += OnItemValueChanged;
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = gameObject.GetComponent<PlayerInteractionController>();
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
        
        rightArmBodyObjectContainer.EquipItem(itemData, this);

        _bodyCurrentlyEquippedItem = rightArmBodyObjectContainer.CurrentlyEquippedItem;

        if (itemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamRightArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().rightIKTarget;
            _playerAnimationController.RightArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().rightIKTarget;
        }
        else
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(0, .2f);
            _playerAnimationController.RightArmRigIKTarget = null;
        }

        if (itemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamLeftArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().leftIKTarget;
            _playerAnimationController.LeftArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().leftIKTarget;
        }
        else
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(0, .2f);
            _playerAnimationController.LeftArmRigIKTarget = null;
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
            // Drop with E or right-click
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
                
                if (ObjectPlacer.Instance.IsActive || ObjectPlacer.Instance.deactivatedThisFrame)
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

    public void PickUpObject(PickableObject pickableObject)
    {
        if (HeldObject != null)
        {
            return;
        }
        
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

            // Match the slot's local transform, then hide the slot placeholder
            ObjectContainer currentObjectContainer = itemData.hand == PickableItemData.Hand.Right ? rightArmCamObjectContainer : leftArmCamObjectContainer;

            PickableObject itemInContainer = null;
            foreach (var item in currentObjectContainer.ItemsHeld)
            {
                if (item.ItemData == itemData)
                {
                    itemInContainer = item;
                }
            }

            pickableObject.SetParent(itemInContainer.transform);
            pickableObject.transform.position = itemInContainer.transform.position;
            pickableObject.transform.rotation = itemInContainer.transform.rotation;
        }

        if (itemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamRightArmRigIKTarget = pickableObject.GetComponent<IkTargets>()?.rightIKTarget ?? _camEquippedItem?.GetComponent<IkTargets>().rightIKTarget;
            _playerAnimationController.RightArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().rightIKTarget;
        }
        if (itemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamLeftArmRigIKTarget = pickableObject.GetComponent<IkTargets>()?.leftIKTarget ?? _camEquippedItem?.GetComponent<IkTargets>().leftIKTarget;
            _playerAnimationController.LeftArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().leftIKTarget;
        }

        if (itemData.useAimIK)
        {
            _playerAnimationController.SetAimRigWeightSmooth(1, .2f);
        }

        pickableObject.OnPickedUp();
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

        GameObject placementItem = ObjectPlacer.Instance.GetPickableObject(_heldObject.ItemData).gameObject;
        DisableArmIKs();

        if (_heldObject != null)
        {
            Vector3 dropPos = dropPoint != null ? dropPoint.position : placementItem.transform.position;
            Quaternion dropRot = dropPoint != null ? dropPoint.rotation : placementItem.transform.rotation;

            // Return to the world
            _heldObject.RemoveParent();
            _heldObject.transform.position = dropPos;
            _heldObject.transform.rotation = dropRot;
            
            if (dropPoint != null)
            {
                _heldObject.SetParent(dropPoint);
            }
            
            _heldObject.OnDropped();
        }

        rightArmBodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }

        _camEquippedItem = null;
        _bodyCurrentlyEquippedItem = null;
        _heldObject = null;
        _heldObject = null;
        itemEquippedIndex.Value = -1;
        _playerAnimationController.DisableRightArmMask();
        ObjectPlacer.Instance.DeactivatePlacer();
    }

    void DisableArmIKs()
    {
        if (_heldObject.ItemData.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(0,.25f);
            _playerAnimationController.LeftArmRigIKTarget = null;
        }
        
        if (_heldObject.ItemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(0,.25f);
            _playerAnimationController.RightArmRigIKTarget = null;
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

}
