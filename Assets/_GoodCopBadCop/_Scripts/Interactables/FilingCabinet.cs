using UnityEngine;

public class FilingCabinet : MonoBehaviour, IInteractable
{
    [SerializeField] private GameObject levelSelectUI;
    
    public void Interact(PlayerInteractionController player)
    {
        player.GetComponent<PlayerMovementController>().SetCanControl(false);
        
        UIController.Instance.OpenLevelSelectUI();
    }
}
