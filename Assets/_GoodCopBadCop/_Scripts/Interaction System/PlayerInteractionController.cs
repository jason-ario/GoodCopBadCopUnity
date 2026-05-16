using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteractionController : NetworkBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;
    public LayerMask interactLayer;

    [Tooltip("Layers that count as valid free-placement surfaces (floors, desks, world geometry, etc.)")]
    public LayerMask placementLayer;

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
    bool reticleActive = false;
    public bool ReticleActive => reticleActive;
    
    private void Awake()
    {
        playerAnimationController = GetComponent<PlayerAnimationController>();
        playerMovementController = GetComponent<PlayerMovementController>();
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

        if (reticle == null)
        {
            reticle = GameObject.FindFirstObjectByType<ReticleController>();
            if (reticle == null)
            {
                return;
            }
        }

        if (UIController.Instance.IsPaused) return;

        HandleReticle();
        
        if (CanInteract == false) return;
        
        if (Input.GetMouseButtonUp(1))
        {
            _placerBlocked = false;
        }


        // Left click: pick up items and use item-on-item interactions
        if (Input.GetMouseButtonDown(0))
        {
            if (!TryPickupOrItemInteract())
            {
                Debug.Log("Can Not Interact");
                _playerPickupController.TryUseObject();
            }
        }

        // E key: interact with non-pickup interactables (doors, levers, etc.)
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryWorldInteract();
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

            if (IsControlledByOtherPlayer(interactable))
                return;

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
                    bool isWorldInteract = interactable is not PickableObject;
                    reticle.SetInteractState(true, interactable.interactText, isWorldInteract);
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
            
            // Handle non-interactable placement surfaces (like PlacementBoard) and free placement
            if (inRange)
            {
                if (Input.GetMouseButtonDown(0) && Input.GetMouseButton(1))
                {
                    _placerBlocked = true;
                }

                if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace)
                {
                    CheckActivatePlacer(placementBoard, hit, true);
                }
                else
                {
                    if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
                }
                
                if (_playerPickupController.IsHoldingObject) return;
            }
            else
            {
                // Out of range — keep the placer visible but tint it red so the player knows they can't drop here
                if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace && _playerPickupController.IsHoldingObject)
                {
                    CheckActivatePlacer(placementBoard, hit, false);
                }
                else if (ObjectPlacer.Instance.IsActive)
                {
                    ObjectPlacer.Instance.DeactivatePlacer();
                }

                if (_playerPickupController.IsHoldingObject) return;
            }
            
            lastInteractable = null;
        }
        else if (Physics.Raycast(ray, out RaycastHit surfaceHit, interactDistance, placementLayer))
        {
            // No interactable hit but we did hit a placement surface — handle free placement
            if (Input.GetMouseButtonDown(0) && Input.GetMouseButton(1))
            {
                _placerBlocked = true;
            }

            if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace)
            {
                CheckActivatePlacer(null, surfaceHit, true);
            }
            else
            {
                if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
            }

            if (_playerPickupController.IsHoldingObject) return;
        }
        else
        {
            // Nothing hit — keep the placer visible but red if it is already active
            if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace && _playerPickupController.IsHoldingObject && ObjectPlacer.Instance.IsActive)
            {
                ObjectPlacer.Instance.SetInRange(false);
            }
            else if (ObjectPlacer.Instance.IsActive)
            {
                ObjectPlacer.Instance.DeactivatePlacer();
            }
        }

        // Reset both states if no valid target was found or returned early
        reticle.SetInteractState(false);
        reticle.SetTooFarState(false);
    }

    /// <summary>
    /// Activates the ObjectPlacer toward the current hit point.
    /// When placementBoard is null, the object is placed freely on the surface at hit.point,
    /// oriented by the surface normal. When a PlacementBoard is provided, its transform
    /// orientation is used instead.
    /// </summary>
    void CheckActivatePlacer(PlacementBoard placementBoard, RaycastHit hit, bool inRange)
    {
        if (!_playerPickupController.IsHoldingObject) return;

        // Per-item opt-out
        if (_playerPickupController.HeldObject.ItemData.canUsePlacementBoard == false)
        {
            reticle.SetInteractState(false);
            if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
            lastInteractable = null;
            return;
        }

        // Hanging items need a hanging board
        if (placementBoard != null && placementBoard.IsHanging && _playerPickupController.HeldObject.ItemData.canBeHung == false)
        {
            reticle.SetInteractState(false);
            if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
            lastInteractable = null;
            return;
        }

        // Determine placement rotation: board rotation when available, otherwise align to surface normal
        Quaternion targetRotation = placementBoard != null
            ? placementBoard.transform.rotation
            : Quaternion.FromToRotation(Vector3.up, hit.normal);

        reticle.SetInteractState(false);

        if (!ObjectPlacer.Instance.IsActive)
        {
            ObjectPlacer.Instance.SetItem(_playerPickupController.HeldObject.ItemData);
            ObjectPlacer.Instance.ActivatePlacer(placementBoard);
            ObjectPlacer.Instance.transform.rotation = targetRotation;
            ObjectPlacer.Instance.transform.position = hit.point;
        }

        ObjectPlacer.Instance.transform.rotation = Quaternion.Lerp(ObjectPlacer.Instance.transform.rotation, targetRotation, Time.deltaTime * objectPlacerLerpSpeed);
        ObjectPlacer.Instance.transform.position = Vector3.Lerp(ObjectPlacer.Instance.transform.position, hit.point, Time.deltaTime * objectPlacerLerpSpeed);
        ObjectPlacer.Instance.SetInRange(inRange);
    }
    

    public void SetReticleActive(bool value)
    {
        if (reticle == null) return;
        reticle.gameObject.SetActive(value);
        reticleActive = value;
    }

    /// <summary>
    /// Handles left-click: picks up PickableObjects and performs item-on-item interactions.
    /// Returns true if an interaction was consumed.
    /// </summary>
    bool TryPickupOrItemInteract()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
            return false;

        Interactable interactable = hit.collider.GetComponent<Interactable>();
        InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();

        if (interactableCollider != null)
            interactable = interactableCollider.Interactable;

        if (IsControlledByOtherPlayer(interactable))
            return false;

        if (onlyAllowedInteractable != null && interactable != onlyAllowedInteractable)
            return false;

        if (interactable == null || !interactable.enabled)
            return false;

        // Item-on-item interaction takes priority regardless of interactable type
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

        // Only allow picking up PickableObjects on left click
        if (interactable is PickableObject)
        {
            interactable.Interact(this);
            reticle.SetInteractState(false);
            reticle.SetTooFarState(false);
            return true;
        }

        // Pickup slots (e.g. InkStamp) act like PickableObjects on left click with empty hands
        if (interactable is IPickupSlot && pickupController.HeldObject == null)
        {
            interactable.Interact(this);
            reticle.SetInteractState(false);
            reticle.SetTooFarState(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles E key: interacts with non-pickup interactables such as doors and levers.
    /// </summary>
    void TryWorldInteract()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
            return;

        Interactable interactable = hit.collider.GetComponent<Interactable>();
        InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();

        if (interactableCollider != null)
            interactable = interactableCollider.Interactable;

        if (IsControlledByOtherPlayer(interactable))
            return;

        if (onlyAllowedInteractable != null && interactable != onlyAllowedInteractable)
            return;

        if (interactable == null || !interactable.enabled)
            return;

        interactable.Interact(this);
    }

    /// <summary>
    /// Returns true when the resolved interactable is already under another player's control —
    /// either because that player is holding the object itself, or because the object is a
    /// document sitting inside a folder that another player is holding. In both cases the local
    /// player's interaction controller should treat the object as invisible.
    /// </summary>
    private bool IsControlledByOtherPlayer(Interactable interactable)
    {
        if (interactable == null)
            return false;

        if (interactable is FolderItem folderItem)
        {
            // Document inside a held folder — block regardless of who holds the folder.
            if (folderItem.insideThisFolder != null && folderItem.insideThisFolder.IsHeldByOtherPlayer)
                return true;
        }

        if (interactable is PickableObject pickable)
            return pickable.IsHeldByOtherPlayer;

        return false;
    }
}