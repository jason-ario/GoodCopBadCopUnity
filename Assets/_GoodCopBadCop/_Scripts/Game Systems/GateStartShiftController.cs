using UnityEngine;

public class GateStartShiftController : Interactable
{
    [SerializeField] Animator gateAnimator;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        UIController.Instance.OpenStartShiftScreen();
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