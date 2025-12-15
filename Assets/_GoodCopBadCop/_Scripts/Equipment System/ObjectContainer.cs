using System;
using UnityEngine;

public class ObjectContainer : MonoBehaviour
{
    private PickableObject[] itemsHeld;
    public PickableObject[] ItemsHeld => itemsHeld;
    private PickableObject currentlyEquippedItem;
    
    public enum ParentContainerType
    {
        BODY,
        ARMS
    }
    
    public ParentContainerType parentContainerType;
    
    private void Awake()
    {
        itemsHeld = GetComponentsInChildren<PickableObject>(true);

        string layerName = parentContainerType == ParentContainerType.ARMS ? "Arms" : "Body";
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
    
    public void EquipItem(PickableItemData itemData)
    {
        foreach (var itemHeld in itemsHeld)
        {
            if (itemData == itemHeld.ItemData)
            {
                // Found matching item, equip it
                if (currentlyEquippedItem != null)
                {
                    currentlyEquippedItem.gameObject.SetActive(false);
                }
            
                currentlyEquippedItem = itemHeld;
                itemHeld.gameObject.SetActive(true);
                itemHeld.OnEquipped();
                return;
            }
        }
    }

    public void UnequipItem(PickableItemData item)
    {
        // Found matching item, equip it
        if (currentlyEquippedItem != null)
        {
            currentlyEquippedItem.gameObject.SetActive(false);
        }
            
        currentlyEquippedItem = null;
    }
}
