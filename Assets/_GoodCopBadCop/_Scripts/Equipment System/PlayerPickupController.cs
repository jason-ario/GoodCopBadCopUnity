using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class PlayerPickupController : NetworkBehaviour
{
    public Transform holdPoint;
    public float holdSmoothness = 10f;

    private PickableItemData _heldObject;
    public PickableItemData HeldObject => _heldObject; 
    private PickableObject _camEquippedItem;
    private PickableObject _bodyCurrentlyEquippedItem;
    
    public bool IsHoldingObject => _heldObject != null;
    private PlayerAnimationController _playerAnimationController;
    public PlayerAnimationController PlayerAnimationController => _playerAnimationController;
    
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
    
    private void Awake()
    {
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        objectContainers = GetComponentsInChildren<ObjectContainer>(true);
        itemEquippedIndex.OnValueChanged += OnItemValueChanged;
        _playerMovementController = GetComponent<PlayerMovementController>();
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
            }

            return;
        }
        
        PickableItemData itemData = camObjectContainer.GetItemData(newValue);
        
        foreach (var objectContainer in objectContainers)
        {
            objectContainer.EquipItem(itemData, this);
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
                
                /*
                if (ObjectPlacer.Instance.deactivatedThisFrame == false)
                {
                    Debug.Log("ObjectPlacer is not deactivated");
                    return;
                }*/

                if (_heldObject.canUsePlacementBoard == false)
                {
                    Debug.Log("Can't use placement board");
                    return;
                }
                
                Debug.Log("Drop Object");
                DropObject();
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
    }

    void StopUsingObject()
    {
        if (camObjectContainer.CurrentlyEquippedItem != null)
        {
            camObjectContainer.CurrentlyEquippedItem.OnStopUse();
        }
    }

    public void PickUpObject(PickableObject pickableObject, PickableItemData itemData)
    {
        // Drop existing object if holding something already
        if (_heldObject != null)
        {
            return;
        }
        
        _heldObject = itemData;
        ObjectPlacer.Instance.SetItem(itemData);
        
        int itemIndex = camObjectContainer.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;
        
        _camEquippedItem = camObjectContainer.CurrentlyEquippedItem;
        _bodyCurrentlyEquippedItem = bodyObjectContainer.CurrentlyEquippedItem;
        
        if (itemData.useRightIK)
        {
            _playerAnimationController.SetRightArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamRightArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().rightIKTarget;
            _playerAnimationController.RightArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().rightIKTarget;
        }
        if (itemData.useLeftIK)
        {
            Debug.Log("Picking up left arm");
            _playerAnimationController.SetLeftArmRigWeightSmooth(1, .2f);
            _playerAnimationController.CamLeftArmRigIKTarget = _camEquippedItem.GetComponent<IkTargets>().leftIKTarget;
            _playerAnimationController.LeftArmRigIKTarget = _bodyCurrentlyEquippedItem.GetComponent<IkTargets>().leftIKTarget;
            
        }
        
        pickableObject.OnPickedUp();
        StartCoroutine(PickUpCoolDown());
    }
    
    IEnumerator PickUpCoolDown()
    {
        yield return new WaitForSeconds(pickUpUseCooldownTimer);
        pickUpCooldownComplete = true;
    }

    public void PickUpObject(PickableItemData itemData)
    {
        // Drop existing object if holding something already
        if (_heldObject != null)
        {
            return;
        }
        

        _heldObject = itemData;
        ObjectPlacer.Instance.SetItem(itemData);

        int itemIndex = camObjectContainer.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;
        _camEquippedItem = camObjectContainer.CurrentlyEquippedItem;
        _bodyCurrentlyEquippedItem = bodyObjectContainer.CurrentlyEquippedItem;
    }

    public void DropObject(Transform dropPoint = null, bool doSpawn = true)
    {
        pickUpCooldownComplete = false;

        // Pass the index/ID of the held item so the server knows which prefab to spawn
        int itemIndex = ItemDatabase.Instance.GetItemIndex(_heldObject);
        GameObject placementItem = ObjectPlacer.Instance.GetPickableObject(_heldObject).gameObject; 
        DisableArmIKs();

        if (doSpawn)
        {
            if (dropPoint != null)
            {
                RequestDropServerRpc(itemIndex, dropPoint.transform.position, dropPoint.transform.rotation);
            }
            else
            {
                RequestDropServerRpc(itemIndex, placementItem.transform.position, placementItem.transform.rotation);
            }
        }
   
        camObjectContainer.CurrentlyEquippedItem.OnDropped();
        bodyObjectContainer.CurrentlyEquippedItem.OnDroppedFromBody();

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }

        _camEquippedItem = null;
        _bodyCurrentlyEquippedItem = null;
        _heldObject = null;
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
