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
    public ReticleController reticle;
    public PlayerAnimationController playerAnimationController; 
    Interactable lastInteractable;
    private PlayerPickupController _playerPickupController;
    [SerializeField] float objectPlacerLerpSpeed = 10f;
    private bool _placerBlocked;

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

        if (Input.GetMouseButtonUp(1))
        {
            _placerBlocked = false;
        }
        
        HandleReticle();

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

            if (Input.GetMouseButtonDown(0) && Input.GetMouseButton(1))
            {
                _placerBlocked = true;
            }

            if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked)
            {
                CheckActivatePlacer(placementBoard, hit);
            }
            else
            {
                if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
            }
            
            lastInteractable = null;
        }

        reticle.SetInteractState(false);
    }

    void CheckActivatePlacer(PlacementBoard placementBoard, RaycastHit hit)
    {
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
            
            if (!ObjectPlacer.Instance.IsActive)
            {
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
            
            if (interactable == null)
            {
                return false;
            }

            if (pickupController.HeldObject != null)
            {
                //Check if held object is compatible with this object
                if (interactable.itemsThatCanInteractWith.Contains(pickupController.HeldObject))
                {
                    interactable.InteractWithItem(this, pickupController.HeldObject);
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