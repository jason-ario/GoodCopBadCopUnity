using UnityEngine;

public class FilingCabinet : Interactable
{
    [SerializeField] private GameObject levelSelectUI;
    
    public override void Interact(PlayerInteractionController player)
    {
        player.GetComponent<PlayerMovementController>().SetCanControl(false);
        
        UIController.Instance.OpenLevelSelectUI();
    }
}
