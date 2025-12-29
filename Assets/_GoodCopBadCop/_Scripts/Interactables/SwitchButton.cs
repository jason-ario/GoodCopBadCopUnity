using UnityEngine;

public class SwitchButton : Interactable
{
    public override void Interact(PlayerInteractionController player)
    {
        GameManager.Instance.TryStartLevel();
    }
}
