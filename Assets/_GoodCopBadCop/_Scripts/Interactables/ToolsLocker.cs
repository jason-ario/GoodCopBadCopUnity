using UnityEngine;

public class ToolsLocker : Interactable
{
    public override void Interact(PlayerInteractionController player)
    {
        UIController.Instance.OpenToolShopUI();
    }
}
