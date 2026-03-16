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

    private PickableItemData _heldObject;
    public PickableItemData HeldObject => _heldObject;
    private PickableObject _heldPickableObject; // the actual world instance being carried
    private PickableObject _camEquippedItem;
    private PickableObject _bodyCurrentlyEquippedItem;
    
    public bool IsHoldingObject => _heldObject != null;
    private PlayerAnimationController _playerAnimationController;
    public PlayerAnimationController PlayerAnimationController => _playerAnimationController;
    private PlayerInteractionController _playerInteractionController;
    
    [SerializeField] ObjectContainer[] objectContainers;
    [FormerlySerializedAs("objectContainerToUse")] [SerializeField] private ObjectContainer camObjectContainer; 
    [SerializeField] private ObjectContainer bodyObjectContainer; 
    public ObjectContainer CamObjectContainer => camObjectContainer;
    public ObjectContainer BodyObjectContainer => bodyObjectContainer;

    private PlayerMovementController _playerMovementController;
    public PlayerMovementController PlayerMovementController => _playerMovementController;

    private NetworkVariable<int> itemEquippedIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private float pickUpUseCooldownTimer = .2f;
    private bool pickUpCooldownComplete = false;

    public UnityAction OnPlaceObject;

    
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
        
        PickableItemData itemData = camObjectContainer.GetItemData(newValue);
        
        bodyObjectContainer.EquipItem(itemData, this);

        _bodyCurrentlyEquippedItem = bodyObjectContainer.CurrentlyEquippedItem;

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
            _playerAnimationController.EnableHoldObjectMask();
        }
    }

    void Update()
    {
        if (IsLocalPlayer == false)
        {
            return;
        }

        if(CanPickUpAndPlace == false) return;

        if (_heldObject != null)
        {
            // Drop with E or right-click
            if (Input.GetMouseButtonUp(1) && !Input.GetMouseButton(0))
            {
                if (_heldObject == null)
                {
                    Debug.Log("HeldObject is null");
                    return;
                }

                if (_heldObject.canUsePlacementBoard == false)
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
        Debug.Log("Try use object");

        if (Input.GetMouseButtonDown(1)) return;
        if (_heldObject == null) return;
        if(pickUpCooldownComplete == false) return;
        
        if (camObjectContainer.CurrentlyEquippedItem != null)
        {
            camObjectContainer.CurrentlyEquippedItem.OnStartUse();
        }
        
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
        if (bodyObjectContainer.CurrentlyEquippedItem != null)
        {
            bodyObjectContainer.CurrentlyEquippedItem.OnBodyStartUse();
        }
    }

    void StopUsingObject()
    {
        if (camObjectContainer.CurrentlyEquippedItem != null)
        {
            camObjectContainer.CurrentlyEquippedItem.OnStopUse();
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
        if (bodyObjectContainer.CurrentlyEquippedItem != null)
        {
            bodyObjectContainer.CurrentlyEquippedItem.OnBodyStopUse();
        }
    }

    public void PickUpObject(PickableObject pickableObject)
    {
        if (_heldObject != null)
        {
            return;
        }
        
        PickableItemData itemData = pickableObject.ItemData;

        _heldObject = itemData;
        _heldPickableObject = pickableObject;
        ObjectPlacer.Instance.SetItem(itemData);

        int itemIndex = camObjectContainer.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;

        _camEquippedItem = pickableObject;
        _bodyCurrentlyEquippedItem = bodyObjectContainer.CurrentlyEquippedItem;
        camObjectContainer.EquipItem(itemData, this, pickableObject);

        // Reparent the real world object into the cam container slot
        if (_camEquippedItem != null)
        {
            // Disable any physics so it follows the hand cleanly
            Rigidbody rb = pickableObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // Match the slot's local transform, then hide the slot placeholder
            
            PickableObject itemInContainer = null;
            foreach (var item in camObjectContainer.ItemsHeld)
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
            Debug.Log("Picking up left arm");
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

        GameObject placementItem = ObjectPlacer.Instance.GetPickableObject(_heldObject).gameObject;
        DisableArmIKs();

        if (_heldPickableObject != null)
        {
            Vector3 dropPos = dropPoint != null ? dropPoint.position : placementItem.transform.position;
            Quaternion dropRot = dropPoint != null ? dropPoint.rotation : placementItem.transform.rotation;

            // Return to the world
            _heldPickableObject.RemoveParent();
            _heldPickableObject.transform.position = dropPos;
            _heldPickableObject.transform.rotation = dropRot;

            // Re-enable physics
            Rigidbody rb = _heldPickableObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            _heldPickableObject.OnDropped();
        }

        bodyObjectContainer.CurrentlyEquippedItem?.OnDroppedFromBody();

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }

        _camEquippedItem = null;
        _bodyCurrentlyEquippedItem = null;
        _heldObject = null;
        _heldPickableObject = null;
        itemEquippedIndex.Value = -1;
        _playerAnimationController.DisableHoldObjectMask();
        ObjectPlacer.Instance.DeactivatePlacer();
    }

    void DisableArmIKs()
    {
        if (_heldObject.useLeftIK)
        {
            _playerAnimationController.SetLeftArmRigWeightSmooth(0,.25f);
            _playerAnimationController.LeftArmRigIKTarget = null;
        }
        
        if (_heldObject.useRightIK)
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
