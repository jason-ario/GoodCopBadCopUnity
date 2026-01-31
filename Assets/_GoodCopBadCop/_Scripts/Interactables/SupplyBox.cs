using UnityEngine;

public class SupplyBox : PickableObject
{ 
    public bool canPickUp = false;
    [SerializeField] Animation boxAnimation;
    [SerializeField] private GameObject contents;
    bool isOpen = false;
    
    public override void Interact(PlayerInteractionController player)
    {
        if (canPickUp)
        {
            player.pickupController.PickUpObject(this, ItemData);
        }
        else
        {
            if (!isOpen)
            {
                OpenBox();
            }
        }
    }

    void OpenBox()
    {
        isOpen = true;
        contents.SetActive(true);
        boxAnimation.Play();
    }
}
