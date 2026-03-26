using UnityEngine;

public class GateController : Interactable
{
    [SerializeField] Animator gateAnimator;
    private bool isClosed;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        if (isClosed)
        {
            player.playerAnimationController.SetAnimTrigger("OpenDoor");
            OpenGate();
            isClosed = false;
        }
        else
        {
            CloseGate();
            isClosed = true;
        }
    }

    public void OpenGate()
    {
        gateAnimator.SetBool("Open", true);
    }
    
    public void CloseGate()
    {
        gateAnimator.SetBool("Open", false);
    }
}
