using System.Collections.Generic;
using UnityEngine;

public class ItemDatabase : MonoBehaviour {
    public static ItemDatabase Instance;
    public List<PickableItemData> allItems; // Populate in inspector
    
    private void Awake() => Instance = this;

    public int GetItemIndex(PickableItemData item) => allItems.IndexOf(item);
    public PickableItemData GetItemByIndex(int index) => (index >= 0 && index < allItems.Count) ? allItems[index] : null;
}