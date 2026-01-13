using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerPickupController : NetworkBehaviour
{
    public Transform holdPoint;
    public float holdSmoothness = 10f;

    private PickableItemData heldObject;
    public PickableItemData HeldObject => heldObject; 

    public bool IsHoldingObject => heldObject != null;
    private PlayerAnimationController _playerAnimationController;
    public PlayerAnimationController PlayerAnimationController => _playerAnimationController;
    
    [SerializeField] ObjectContainer[] objectContainers;
    [SerializeField] private ObjectContainer objectContainerToUse; 
    public ObjectContainer ObjectContainer => objectContainerToUse;
    private NetworkVariable<int> itemEquippedIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private float pickUpUseCooldownTimer = .2f;
    private bool pickUpCooldownComplete = false;
    
    private void Awake()
    {
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        objectContainers = GetComponentsInChildren<ObjectContainer>(true);
        itemEquippedIndex.OnValueChanged += OnItemValueChanged;
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
        
        PickableItemData itemData = objectContainerToUse.GetItemData(newValue);
        
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

        if (heldObject != null)
        {
            // Drop with E or right-click
            if (Input.GetMouseButtonUp(1) && !Input.GetMouseButton(0))
            {
                if (heldObject == null) return;
                if (ObjectPlacer.Instance.deactivatedThisFrame == false) return;
                if (heldObject.canUsePlacementBoard == false) return;
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
        if (Input.GetMouseButtonDown(1)) return;
        if (heldObject == null) return;
        if(pickUpCooldownComplete == false) return;
        
        if (objectContainerToUse.CurrentlyEquippedItem != null)
        {
            objectContainerToUse.CurrentlyEquippedItem.OnStartUse();
        }
    }

    void StopUsingObject()
    {
        if (objectContainerToUse.CurrentlyEquippedItem != null)
        {
            objectContainerToUse.CurrentlyEquippedItem.OnStopUse();
        }
    }

    public void PickUpObject(PickableObject pickableObject, PickableItemData itemData)
    {
        // Drop existing object if holding something already
        if (heldObject != null)
        {
            return;
        }
        
        heldObject = itemData;
        ObjectPlacer.Instance.SetItem(itemData);

        int itemIndex = objectContainerToUse.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;
        
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
        if (heldObject != null)
        {
            return;
        }
        

        heldObject = itemData;
        ObjectPlacer.Instance.SetItem(itemData);

        int itemIndex = objectContainerToUse.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;
    }

    public void DropObject(Transform dropPoint = null, bool doSpawn = true)
    {
        pickUpCooldownComplete = false;

        // Pass the index/ID of the held item so the server knows which prefab to spawn
        int itemIndex = ItemDatabase.Instance.GetItemIndex(heldObject);
        GameObject placementItem = ObjectPlacer.Instance.GetPickableObject(heldObject).gameObject;

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
   
        objectContainerToUse.CurrentlyEquippedItem.OnDropped();

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(this);
        }
        
        heldObject = null;
        itemEquippedIndex.Value = -1;
        _playerAnimationController.DisableHoldObjectMask();
        ObjectPlacer.Instance.DeactivatePlacer();
    }

    [ServerRpc]
    private void RequestDropServerRpc(int itemIndex, Vector3 position, Quaternion rotation)
    {
        // Get the actual data/prefab on the server side using the index
        PickableItemData data = ItemDatabase.Instance.GetItemByIndex(itemIndex);
        if (data == null || data.PickUpPrefab == null) return;

        GameObject spawnedPickup = Instantiate(data.PickUpPrefab, position, rotation);

        NetworkObject netObj = spawnedPickup.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
    }
}
