using UnityEngine;

[CreateAssetMenu(fileName = "New Pickable Item Data", menuName = "Pickable Items/Item Data")]
public class PickableItemData : ScriptableObject
{
    [SerializeField] private GameObject pickUpPrefab;
    public GameObject PickUpPrefab => pickUpPrefab;
    public bool canBeHung = false;
    
    [Header("Animation Data")]
    public bool usesTwoArms;
    public string pickupAnimBool;
    public bool canUsePlacementBoard = true;
}
