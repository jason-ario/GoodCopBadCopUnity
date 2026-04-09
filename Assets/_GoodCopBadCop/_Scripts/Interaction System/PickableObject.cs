using System;
using HighlightPlus;
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
    MeshRenderer[] meshRenderers;
    bool setSeeThrough = false;
    protected PlayerPickupController playerPickupController;
    [SerializeField] PickableItemData itemData;
    public PickableItemData ItemData => itemData;
    [SerializeField] AudioClip pickupSound;
    [SerializeField] AudioClip putDownSound;
    private ParentConstraint _parentConstraint;
    private InteractableCollider[] interactableColliders = Array.Empty<InteractableCollider>();
    private Rigidbody _rigidbody;

    public bool CanPickUpManually { get; set; } = true;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _rigidbody.isKinematic = true;
    }

    protected override void Awake()
    {
        base.Awake();
        meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        interactableColliders = GetComponentsInChildren<InteractableCollider>(true);
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
        _rigidbody.isKinematic = true;
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
        RemoveParent();
        source.sourceTransform = parent;
        source.weight = 1;
        _parentConstraint.AddSource(source);
        _parentConstraint.constraintActive = true;
    }

    public void RemoveParent()
    {
        transform.parent = null;
        if (_parentConstraint.sourceCount > 0)
        {
            _parentConstraint.RemoveSource(0);
        }
    }

    public virtual void OnEquipped(PlayerPickupController player)
    {
        SetInteractable(false);

        playerPickupController = player;

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, true);
        }
    }
    
    public virtual void OnUnequip(PlayerPickupController player)
    {
        SetInteractable(true);

        if (itemData.pickupAnimBool != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool(itemData.pickupAnimBool, false);
        }

        if (itemData.usesTwoArms)
        {
            player.PlayerAnimationController.DisableLeftArmMask();
            player.PlayerAnimationController.DisableRightArmMask();
        }
    }
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        if (!CanPickUpManually) return;
        player.pickupController.PickUpObject(this);
    }

    public void SetInteractable(bool value)
    {
        foreach (Collider col in GetComponents<Collider>())
        {
            col.enabled = value;
        }

        if (interactableColliders.Length <= 0) return;
        foreach (var interactableCollider in interactableColliders)
        {
            interactableCollider.GetComponent<Collider>().enabled = value;
        }
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


    public void SetPlacementClone()
    {
        _parentConstraint.enabled = false;
    }
}