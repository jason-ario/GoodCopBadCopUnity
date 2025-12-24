using UnityEngine;

public class SwitchButton : MonoBehaviour, IInteractable
{
    public void Interact(PlayerInteractionController player)
    {
        GameManager.Instance.OnStartLevel();
    }
}
