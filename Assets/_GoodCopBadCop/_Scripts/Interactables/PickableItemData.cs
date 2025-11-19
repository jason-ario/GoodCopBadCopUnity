using UnityEngine;

[CreateAssetMenu(fileName = "New Pickable Item Data", menuName = "Pickable Items/Item Data")]
public class PickableItemData : ScriptableObject
{
    public GameObject PickUpPrefab { get; set; }
}
