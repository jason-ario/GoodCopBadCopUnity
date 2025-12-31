using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerPickupController : NetworkBehaviour
{
    public Transform holdPoint;
    public float holdSmoothness = 10f;

    private PickableItemData heldObject;
    
    public bool IsHoldingObject => heldObject != null;
    private PlayerAnimationController _playerAnimationController;
    public PickableItemData HeldObject => heldObject;
    
    [SerializeField] ObjectContainer[] objectContainers;

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
        }
    }

    public void PickUpObject(PickableObject obj)
    {
        // Drop existing object if holding something already
        if (heldObject != null)
        {
            DropObject();
        }
        
        heldObject = obj.ItemData;

        // Notify item-specific logic
        obj.OnPickedUp();
        ObjectPlacer.Instance.SetItem(obj.ItemData);
        
        Destroy(obj.gameObject);
        //Despawn
        _playerAnimationController.EnableHoldObjectMask();
        
        foreach (var objectContainer in objectContainers)
        {
            objectContainer.EquipItem(obj.ItemData);
        }
    }

    public void PickUpObject(PickableObject pickableObject, PickableItemData itemData)
    {
        // Drop existing object if holding something already
        if (heldObject != null)
        {
            DropObject();
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
            objectContainer.EquipItem(itemData);
        }
        
        pickableObject.OnPickedUp();
    }

    public void DropObject()
    {
        if (heldObject == null) return;

        GameObject spawnedPickup = Instantiate(heldObject.PickUpPrefab, holdPoint.position, Quaternion.identity);

        spawnedPickup.transform.position = ObjectPlacer.Instance.transform.position;
        spawnedPickup.transform.rotation = ObjectPlacer.Instance.transform.rotation;

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem(heldObject);
        }
        _playerAnimationController.DisableHoldObjectMask();

        heldObject = null;
        ObjectPlacer.Instance.DeactivatePlacer();
    }
}
