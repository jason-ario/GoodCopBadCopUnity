using System;
using Unity.Netcode;
using UnityEngine;

public class ObjectContainer : MonoBehaviour
{
    [SerializeField] private PickableObject[] itemsHeld;
    public PickableObject[] ItemsHeld => itemsHeld;
    private PickableObject currentlyEquippedItem;
    public PickableObject CurrentlyEquippedItem => currentlyEquippedItem;
    public bool overrideLayer = true;

    public enum ParentContainerType
    {
        BODY,
        ARMS
    }
    
    public ParentContainerType parentContainerType;
    
    private void Awake()
    {
        itemsHeld = GetComponentsInChildren<PickableObject>(true);

        if (overrideLayer)
        {
            string layerName = parentContainerType == ParentContainerType.ARMS ? "Arms" : "Body";
            int layer = LayerMask.NameToLayer(layerName);

            foreach (var itemHeld in itemsHeld)
            {
                SetLayerRecursively(itemHeld.gameObject, layer);
            }
        }
    }

    public void SetClientLayers()
    {
        string layerName = "Default";
        int layer = LayerMask.NameToLayer(layerName);
        foreach (var itemHeld in itemsHeld)
        {
            SetLayerRecursively(itemHeld.gameObject, layer);
        }
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
    
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    
    public void EquipItem(PickableItemData itemData, PlayerPickupController playerPickupController, PickableObject equipThis = null)
    {
        if (equipThis != null)
        {
            currentlyEquippedItem = equipThis;
            equipThis.OnEquipped(playerPickupController);
            ObjectPlacer.Instance.SetItem(itemData);
            return;
        }
        
        for (var i = 0; i < itemsHeld.Length; i++)
        {
            var itemHeld = itemsHeld[i];
            if (itemData == itemHeld.ItemData)
            {
                // Found matching item, equip it
                if (currentlyEquippedItem != null)
                {
                    currentlyEquippedItem.gameObject.SetActive(false);
                }

                currentlyEquippedItem = itemHeld;
                itemHeld.gameObject.SetActive(true);
                if (playerPickupController != null)
                {
                    itemHeld.OnEquipped(playerPickupController);
                }
                return;
            }
        }
    }
    
    public void UnequipItem(PlayerPickupController playerPickupController, bool deactivate = false)
    {
        // Found matching item, equip it
        if (currentlyEquippedItem != null)
        {
            currentlyEquippedItem.OnUnequip(playerPickupController);
            if(deactivate) currentlyEquippedItem.gameObject.SetActive(false);
        }
            
        currentlyEquippedItem = null;
    }

    public int ItemIndex(PickableItemData itemData)
    {
        for (int i = 0; i < itemsHeld.Length; i++)
        {
            if (itemsHeld[i].ItemData == itemData)
            {
                return i;
            }
        }

        return -1;
    }

    public PickableItemData GetItemData(int newValue)
    {
        return itemsHeld[newValue].ItemData;
    }
}
