using Unity.Netcode;
using UnityEngine;

public class GateStartShiftController : Interactable
{
    [SerializeField] Animator gateAnimator;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        UIController.Instance.OpenStartShiftScreen();
    }

    /// <summary>Opens the gate on all clients. Must be called on the server.</summary>
    public void OpenGate()
    {
        SetGateOpenClientRpc(true);
    }

    /// <summary>Closes the gate on all clients. Must be called on the server.</summary>
    public void CloseGate()
    {
        SetGateOpenClientRpc(false);
    }

    [ClientRpc]
    private void SetGateOpenClientRpc(bool open)
    {
        gateAnimator.SetBool("Open", open);
    }
}