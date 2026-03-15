using UnityEngine;

public class SupplyBox : PickableObject
{ 
    public bool canPickUp = false;
    [SerializeField] Animation boxAnimation;
    [SerializeField] private GameObject contents;
    bool isOpen = false;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (canPickUp)
        {
            player.pickupController.PickUpObject(this);
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
