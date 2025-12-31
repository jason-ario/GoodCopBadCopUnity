using UnityEngine;

public class PickableObject : Interactable
{
    // Virtual methods allow overriding
    [SerializeField] MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
    }
    
    public virtual void OnPickedUp()
    {
        if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
        
        Destroy(gameObject);
    }
    
    public virtual void OnDropped() { }
    public virtual void OnEquipped() { }

    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    [SerializeField] AudioClip pickupSound;


    public override void Interact(PlayerInteractionController player)
    {
        player.pickupController.PickUpObject(this, ItemData);
    }
}