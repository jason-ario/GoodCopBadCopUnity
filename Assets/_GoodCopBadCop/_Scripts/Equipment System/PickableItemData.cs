using UnityEngine;

[CreateAssetMenu(fileName = "New Pickable Item Data", menuName = "Pickable Items/Item Data")]
public class PickableItemData : ScriptableObject
{
    [SerializeField] private GameObject pickUpPrefab;
    public GameObject PickUpPrefab => pickUpPrefab;
    public bool canBeHung = false;

    [Tooltip("When false, this item cannot be charged/thrown via ThrowController (e.g. stamps). Held/placed normally as usual.")]
    public bool canBeThrown = true;

    [Tooltip("When false, this item can never be stowed on the body and does NOT occupy an inventory " +
             "hotbar slot (e.g. the supply box). It can be picked up even with a full inventory, but " +
             "while carried the hotbar keys and scroll wheel are locked out — the only way to get it " +
             "out of your hands is to place or drop it.")]
    public bool canBeStowed = true;

    public enum Hand
    {
        Left,
        Right
    }

    public Hand hand;
    
    [Header("Audio")]
    [SerializeField] private AudioClip pickupSound;
    public AudioClip PickupSound => pickupSound;

    [Tooltip("Played whenever this item is placed down (e.g. via DropObject/PlacementFeedback) — " +
             "regardless of whether the placement turns out to be a correct one for whatever system " +
             "evaluates that (e.g. a mail package landing in ANY bin/cubby, right or wrong; sorting " +
             "outcomes like MailPackageItem's success chime play separately, on top of this, only " +
             "when the sort is actually correct). Leave empty for a silent placement.")]
    [SerializeField] private AudioClip placementSound;
    public AudioClip PlacementSound => placementSound;

    [Tooltip("Volume for placementSound.")]
    [SerializeField] private float placementSoundVolume = 1f;
    public float PlacementSoundVolume => placementSoundVolume;

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
