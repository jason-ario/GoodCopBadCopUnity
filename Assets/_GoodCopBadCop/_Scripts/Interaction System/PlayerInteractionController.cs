using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteractionController : NetworkBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;
    public LayerMask interactLayer;

    public PlayerPickupController pickupController;
    public ReticleController reticle;
    public PlayerAnimationController playerAnimationController; 
    Interactable lastInteractable;
    private PlayerPickupController _playerPickupController;

    private void Awake()
    {
        playerAnimationController = GetComponent<PlayerAnimationController>();
        reticle = GameObject.FindFirstObjectByType<ReticleController>();
        _playerPickupController = GetComponent<PlayerPickupController>();
    }

    void Update()
    {
        if (IsLocalPlayer == false)
        {
            return;
        }
        
        HandleReticle();

        if (Input.GetMouseButtonDown(0))
        {
            TryInteract();
        }
    }

    void HandleReticle()
    {
        if (reticle == null)
        {
            reticle = GameObject.FindFirstObjectByType<ReticleController>();
        }
        
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (lastInteractable != null)
        {
            lastInteractable.Highlight(false);
            if(lastInteractable.GetComponent<PlaceObjectSlot>() != null) lastInteractable.GetComponent<PlaceObjectSlot>().HidePlacedVisual();
        }

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            Interactable interactable = hit.collider.GetComponent<Interactable>(); 
            InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();

            if (interactableCollider != null)
            {
                interactable = interactableCollider.Interactable;
            }
            
            PlacementBoard placementBoard = hit.collider.GetComponent<PlacementBoard>();

            if (interactable != null && interactable.enabled)
            {
                reticle.SetInteractState(true);
                interactable.Highlight(true);
                lastInteractable = interactable;
                PlaceObjectSlot placeObjectSlot = hit.collider.GetComponent<PlaceObjectSlot>();

                //Placement Slot?
                if (_playerPickupController.IsHoldingObject && placeObjectSlot != null && !placeObjectSlot.IsPlaced && placeObjectSlot.itemThatCanBePlaced == _playerPickupController.HeldObject)
                {
                    reticle.SetInteractState(true);
                    placeObjectSlot.ShowPlaceObjectVisual();
                    ObjectPlacer.Instance.DeactivatePlacer();
                }
                else
                {
                    if (placeObjectSlot != null) placeObjectSlot.HidePlacedVisual();
                }
                
                return;
            }
            else
            {
                lastInteractable = null;
            }


            //Placement Board?
            if (_playerPickupController.IsHoldingObject && placementBoard != null)
            {
                if (placementBoard.IsHanging && _playerPickupController.HeldObject.canBeHung == false || _playerPickupController.HeldObject.canUsePlacementBoard == false)
                {
                    reticle.SetInteractState(false);
                    if (ObjectPlacer.Instance.IsActive)
                    {
                        ObjectPlacer.Instance.DeactivatePlacer();
                    }
                
                    lastInteractable = null;
                    return;
                }
                
                reticle.SetInteractState(true);
                if (ObjectPlacer.Instance.IsActive == false)
                {
                    ObjectPlacer.Instance.ActivatePlacer(placementBoard);
                }
                
                ObjectPlacer.Instance.transform.rotation = placementBoard.transform.rotation;
                ObjectPlacer.Instance.transform.position = hit.point;
                return;
            }
            else
            {
                if (ObjectPlacer.Instance.IsActive)
                {
                    ObjectPlacer.Instance.DeactivatePlacer();
                }
                
                lastInteractable = null;
            }
        }

        reticle.SetInteractState(false);
    }

    void TryInteract()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            Interactable interactable = hit.collider.GetComponent<Interactable>();
            InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();

            if (interactableCollider != null)
            {
                interactable = interactableCollider.Interactable;
            }
            
            if (interactable != null)
            {
                interactable.Interact(this);
            }
        }
    }

    public void SetReticleActive(bool value)
    {
        reticle.gameObject.SetActive(value);
    }
}