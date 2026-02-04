using Unity.Netcode;
using UnityEngine;

public class PickableObject : Interactable
{
    // Virtual methods allow overriding
    [SerializeField] MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;
    protected PlayerPickupController playerPickupController;

    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    [SerializeField] AudioClip pickupSound;
    [SerializeField] AudioClip putDownSound;

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    public virtual void OnPickedUp()
    {
        if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
        
        RequestDespawnServerRpc();
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void RequestDespawnServerRpc()
    {
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn();
        }
    }

    public virtual void OnDropped()
    {
        if (putDownSound != null)
        {
            SFXController.Instance.Play(putDownSound);
        } else if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
    }

    public virtual void OnEquipped(PlayerPickupController player)
    {
        playerPickupController = player;

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, true);
        }
    }
    
    public virtual void OnUnequip(PlayerPickupController player)
    {
        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, false);
        }
    }



    public override void Interact(PlayerInteractionController player)
    {
        player.pickupController.PickUpObject(this, ItemData);
    }

    public virtual void OnStartUse()
    {
        
    }
    
    public virtual void OnStopUse()
    {
        
    }

    public void OnDroppedFromBody()
    {
      
    }
}