using HighlightPlus;
using UnityEngine;

public class PayphoneController : Interactable
{
    void Awake()
    {
        base.Awake();
        GetComponent<HighlightEffect>().effectNameFilter = "Friendphone";
    }
    protected override string DefaultInteractVerb => "Call";

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        UIController.Instance.OpenInvitePanel();
    }
}
