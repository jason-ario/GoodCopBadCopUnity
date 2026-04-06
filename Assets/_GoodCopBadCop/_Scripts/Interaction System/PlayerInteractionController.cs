using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteractionController : NetworkBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;
    public LayerMask interactLayer;
    public PlayerPickupController pickupController;
    public PlayerMovementController playerMovementController;
    public ReticleController reticle;
    public PlayerAnimationController playerAnimationController; 
    Interactable lastInteractable;
    private PlayerPickupController _playerPickupController;
    [SerializeField] float objectPlacerLerpSpeed = 10f;
    private bool _placerBlocked;
    private bool _canInteract = true;
    public bool CanInteract => _canInteract;
    public Interactable onlyAllowedInteractable;
    
    private void Awake()
    {
        playerAnimationController = GetComponent<PlayerAnimationController>();
        playerMovementController = GetComponent<PlayerMovementController>();
        reticle = GameObject.FindFirstObjectByType<ReticleController>();
        _playerPickupController = GetComponent<PlayerPickupController>();
    }
    
    public void SetCanInteract(bool value, string interactText)
    {
        _canInteract = value;

        if (value == false)
        {
            if (lastInteractable != null)
            {
                lastInteractable.Highlight(false);
                reticle.SetInteractState(false, interactText);
            }
        }
        else
        {
            reticle.SetInteractState(true, interactText);
        }
    }

    void Update()
    {
        if (IsLocalPlayer == false)
        {
            return;
        }

        HandleReticle();
        
        if (CanInteract == false) return;
        
        if (Input.GetMouseButtonUp(1))
        {
            _placerBlocked = false;
        }


        if (Input.GetMouseButtonDown(0))
        {
            if (!TryInteract())
            {
                Debug.Log("Can Not Interact");
                // This is where you would call UseObject if interaction failed
                _playerPickupController.TryUseObject(); 
            }
        }
    }
    
    void HandleReticle()
    {
        if (_playerPickupController.CanPickUpAndPlace == false && CanInteract == false)
        {
            return;
        }
        
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

        // Increase distance significantly to detect "Too Far" objects
        if (Physics.Raycast(ray, out RaycastHit hit, 10f, interactLayer))
        {
            Interactable interactable = hit.collider.GetComponent<Interactable>(); 
            InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();
            
            if (interactableCollider != null)
            {
                interactable = interactableCollider.Interactable;
            }
            
            if (onlyAllowedInteractable != null && interactable != onlyAllowedInteractable)
            {
                return;
            }
            
            bool inRange = hit.distance <= interactDistance;
            PlacementBoard placementBoard = hit.collider.GetComponent<PlacementBoard>();

            if (interactable != null && interactable.enabled)
            {
                if (inRange)
                {
                    reticle.SetInteractState(true, interactable.interactText);
                    interactable.Highlight(true);
                    lastInteractable = interactable;
                }
                else
                {
                    Debug.Log("Out of range");
                    reticle.SetTooFarState(true);
                    lastInteractable = null;
                }

                PlaceObjectSlot placeObjectSlot = hit.collider.GetComponent<PlaceObjectSlot>();

                //Placement Slot?
                if (inRange && _playerPickupController.IsHoldingObject && placeObjectSlot != null && !placeObjectSlot.IsPlaced && placeObjectSlot.itemThatCanBePlaced.ItemData == _playerPickupController.HeldObject)
                {
                    reticle.SetInteractState(true, interactable.interactText);
                    placeObjectSlot.ShowPlaceObjectVisual();
                    ObjectPlacer.Instance.DeactivatePlacer();
                }
                else
                {
                    if (placeObjectSlot != null) placeObjectSlot.HidePlacedVisual();
                }
                
                // Return so the reset logic at the bottom doesn't run
                return;
            }
            
            // Handle non-interactable placement surfaces (like PlacementBoard)
            if (inRange)
            {
                if (Input.GetMouseButtonDown(0) && Input.GetMouseButton(1))
                {
                    _placerBlocked = true;
                }

                if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace)
                {
                    CheckActivatePlacer(placementBoard, hit);
                }
                else
                {
                    if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
                }
                
                // If we are over a placement board but it's not an "interactable" per se,
                // we might still want to return if CheckActivatePlacer sets a reticle state.
                if (placementBoard != null && _playerPickupController.IsHoldingObject) return;
            }
            
            lastInteractable = null;
        }

        // Reset both states if no valid target was found or returned early
        reticle.SetInteractState(false);
        reticle.SetTooFarState(false);
    }

    void CheckActivatePlacer(PlacementBoard placementBoard, RaycastHit hit)
    {
        //Placement Board?
        if (_playerPickupController.IsHoldingObject && placementBoard != null)
        {
            if (placementBoard.IsHanging && _playerPickupController.HeldObject.ItemData.canBeHung == false || _playerPickupController.HeldObject.ItemData.canUsePlacementBoard == false)
            {
                reticle.SetInteractState(false);
                if (ObjectPlacer.Instance.IsActive)
                {
                    ObjectPlacer.Instance.DeactivatePlacer();
                }
                
                lastInteractable = null;
                return;
            }
                
            reticle.SetInteractState(false);
            
            if (!ObjectPlacer.Instance.IsActive)
            {
                ObjectPlacer.Instance.SetItem(_playerPickupController.HeldObject.ItemData); // Set the item BEFORE activating
                ObjectPlacer.Instance.ActivatePlacer(placementBoard);
                ObjectPlacer.Instance.transform.rotation = placementBoard.transform.rotation;
                ObjectPlacer.Instance.transform.position = hit.point;
            }
                
            ObjectPlacer.Instance.transform.rotation = Quaternion.Lerp(ObjectPlacer.Instance.transform.rotation, placementBoard.transform.rotation, Time.deltaTime * objectPlacerLerpSpeed);
            ObjectPlacer.Instance.transform.position = Vector3.Lerp(ObjectPlacer.Instance.transform.position, hit.point, Time.deltaTime * objectPlacerLerpSpeed);
            return;
        }

        if (ObjectPlacer.Instance.IsActive)
        {
            ObjectPlacer.Instance.DeactivatePlacer();
        }
    }
    

    public void SetReticleActive(bool value)
    {
        reticle.gameObject.SetActive(value);
    }

    bool TryInteract()
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

            if (onlyAllowedInteractable != null)
            {
                if (interactable != onlyAllowedInteractable)
                {
                    return false;
                }
            }

            if (interactable == null)
            {
                return false;
            }

            if (pickupController.HeldObject != null)
            {
                if (interactable.itemsThatCanInteractWith.Contains(pickupController.HeldObject.ItemData))
                {
                    interactable.InteractWithItem(this, pickupController.HeldObject);
                    _playerPickupController.TryUseObject();
                    return true;
                }

                Debug.Log("Held Object is not compatible with this object");
                return false;
            }

            if (interactable != null && interactable.enabled)
            {
                interactable.Interact(this);
                return true;
            }
        }

        return false;
    }
}