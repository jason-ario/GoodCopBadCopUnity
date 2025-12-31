using System;
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

    private void Awake()
    {
        _playerAnimationController = GetComponent<PlayerAnimationController>();
        objectContainers = GetComponentsInChildren<ObjectContainer>(true);
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
            if (Input.GetMouseButtonDown(1))
            {
                DropObject();
            }
            
            if (Input.GetMouseButtonDown(0))
            {
                UseObject();
            }
            
            if (Input.GetMouseButtonUp(0))
            {
                StopUsingObject();
            }
        }
    }

    void UseObject()
    {
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

        if (itemData.usesTwoArms)
        {
            _playerAnimationController.EnableHoldObjectTwoArmsMask();
        }
        else
        {
            _playerAnimationController.EnableHoldObjectMask();
        }
        
        foreach (var objectContainer in objectContainers)
        {
            objectContainer.EquipItem(itemData, this);
        }
        
        pickableObject.OnPickedUp();
    }

    public void DropObject()
    {
        if (heldObject == null) return;
        if (ObjectPlacer.Instance.IsActive == false) return;

        // Call the server to handle the actual spawning
        RequestDropServerRpc(ObjectPlacer.Instance.transform.position, ObjectPlacer.Instance.transform.rotation);

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(heldObject);
        }
        _playerAnimationController.DisableHoldObjectMask();

        heldObject = null;
        ObjectPlacer.Instance.DeactivatePlacer();
    }

    [ServerRpc]
    private void RequestDropServerRpc(Vector3 position, Quaternion rotation)
    {
        // 1. Instantiate the prefab (must have a NetworkObject component)
        GameObject spawnedPickup = Instantiate(heldObject.PickUpPrefab, position, rotation);

        // 2. Spawn it on the network
        NetworkObject netObj = spawnedPickup.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
    }
}
