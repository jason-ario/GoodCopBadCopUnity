using UnityEngine;

public class PayphoneController : Interactable
{
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        UIController.Instance.OpenInvitePanel();
    }
}
