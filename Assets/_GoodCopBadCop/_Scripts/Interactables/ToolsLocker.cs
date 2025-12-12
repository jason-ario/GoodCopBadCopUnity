using UnityEngine;

public class ToolsLocker : MonoBehaviour, IInteractable
{
    public void Interact(PlayerInteractionController player)
    {
        UIController.Instance.OpenToolShopUI();
    }
}
