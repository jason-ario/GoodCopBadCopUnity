using UnityEngine;

[CreateAssetMenu(fileName = "New Pickable Item Data", menuName = "Pickable Items/Item Data")]
public class PickableItemData : ScriptableObject
{
    [SerializeField] private GameObject pickUpPrefab;
    public GameObject PickUpPrefab => pickUpPrefab;
    public bool canBeHung = false;

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
    public bool canUsePlacementBoard = true;
    public bool useLeftIK;
    public bool useRightIK;
    public bool useAimIK;
}
