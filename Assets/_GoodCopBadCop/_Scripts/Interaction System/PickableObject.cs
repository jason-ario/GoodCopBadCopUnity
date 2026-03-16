using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Animations;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkRigidbody))]
[RequireComponent(typeof(ParentConstraint))]
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
    private ParentConstraint _parentConstraint;
    [SerializeField] Collider[] interactableColliders;
    private Rigidbody _rigidbody;

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        _parentConstraint = GetComponent<ParentConstraint>();
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.isKinematic = true;
    }

    public virtual void OnPickedUp()
    {
        if (pickupSound != null)
        {
            SFXController.Instance.Play(pickupSound);
        }
        // Do NOT despawn — reparenting is handled by PlayerPickupController
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

    public void SetParent(Transform parent)
    {
        ConstraintSource source = new ConstraintSource();
        source.sourceTransform = parent;
        source.weight = 1;
        _parentConstraint.AddSource(source);
        _parentConstraint.constraintActive = true;
    }

    public void RemoveParent()
    {
        _parentConstraint.RemoveSource(0);
    }

    public virtual void OnEquipped(PlayerPickupController player)
    {
        Debug.Log("Should disable colliders");

        playerPickupController = player;
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = false;
        }

        foreach (Collider interactableCollider in interactableColliders)
        {
            Debug.Log("Should disable colliders");
            interactableCollider.enabled = false;
        }

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, true);
        }
    }

    void DisableColliders()
    {
        
    }

    void EnableColliders()
    {
        
    }
    
    public virtual void OnUnequip(PlayerPickupController player)
    {
        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, false);
        }
        
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = true;
        }
        
        foreach (Collider interactableCollider in interactableColliders)
        {
            interactableCollider.enabled = true;
        }
    }



    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        player.pickupController.PickUpObject(this);
    }

    public virtual void OnStartUse()
    {
    }
    
    public virtual void OnBodyStartUse()
    {
        
    }

    public virtual void OnBodyStopUse()
    {
    }
    
    
    public virtual void OnStopUse()
    {
        
    }

    public void OnDroppedFromBody()
    {
      
    }


}