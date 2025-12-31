using Unity.Netcode;
using UnityEngine;

public class PickableObject : Interactable
{
    // Virtual methods allow overriding
    [SerializeField] MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;
    protected PlayerPickupController playerPickupController;

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
    
    public virtual void OnDropped() { }

    public virtual void OnEquipped(PlayerPickupController player)
    {
        playerPickupController = player;
    }

    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    [SerializeField] AudioClip pickupSound;


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
}