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
    private NetworkVariable<int> itemEquippedIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
                objectContainer.UnequipItem();
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

        int itemIndex = objectContainerToUse.ItemIndex(itemData);
        itemEquippedIndex.Value = itemIndex;
        
        pickableObject.OnPickedUp();
    }

    public void DropObject()
    {
        if (heldObject == null) return;
        if (ObjectPlacer.Instance.IsActive == false) return;

        // Pass the index/ID of the held item so the server knows which prefab to spawn
        int itemIndex = ItemDatabase.Instance.GetItemIndex(heldObject);
        RequestDropServerRpc(itemIndex, ObjectPlacer.Instance.transform.position, ObjectPlacer.Instance.transform.rotation);

        foreach (var objectContainer in objectContainers)
        {
            objectContainer.UnequipItem();
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
