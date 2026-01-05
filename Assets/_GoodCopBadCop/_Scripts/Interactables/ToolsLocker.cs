using Unity.Netcode;
using UnityEngine;

public class ToolsLocker : Interactable
{
    [SerializeField] private Animator anim;
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    [SerializeField] private PurchaseLocker[] miniLockers;
    
    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += (oldValue, newValue) =>
        {
            anim.SetBool("Open", newValue);
        };
    }

    public override void Interact(PlayerInteractionController player)
    {
        ToggleToolLockerServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleToolLockerServerRpc()
    {
        isOpen.Value = !isOpen.Value;

        if (isOpen.Value == false)
        {
            foreach (var miniLocker in miniLockers)
            {
                miniLocker.CloseServerRpc();
            }
        }
    }
}
