using UnityEngine;

[CreateAssetMenu(fileName = "New Pickable Item Data", menuName = "Pickable Items/Item Data")]
public class PickableItemData : ScriptableObject
{
    [SerializeField] private GameObject pickUpPrefab;
    public GameObject PickUpPrefab => pickUpPrefab;
    public bool canBeHung = false;

    [Tooltip("When false, this item cannot be charged/thrown via ThrowController (e.g. stamps). Held/placed normally as usual.")]
    public bool canBeThrown = true;

    public enum Hand
    {
        Left,
        Right
    }

    public Hand hand;
    
    [Header("Audio")]
    [SerializeField] private AudioClip pickupSound;
    public AudioClip PickupSound => pickupSound;

    [Header("Animation Data")]
    public bool usesTwoArms;
    public string pickupAnimBool;
    public bool cantUsePlacementBoard = false;
    public bool useLeftIK;
    public bool useRightIK;
    public bool useAimIK;

    [Header("UI")]
    [Tooltip("Icon displayed in the inventory HUD slot when this item is carried.")]
    [SerializeField] private Sprite icon;
    public Sprite Icon => icon;

    [Tooltip("Optional flavor/description text shown in the shop purchase popup when this item is for sale. Leave empty to hide the description in the popup.")]
    [TextArea]
    [SerializeField] private string description;
    public string Description => description;
}
