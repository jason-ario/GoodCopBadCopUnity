using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteractionController : NetworkBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;

    [Tooltip("Max distance at which the player can drop a held object using right-click. Independent of interact distance.")]
    public float placementDistance = 5f;

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

    /// <summary>
    /// Maximum surface slope angle (in degrees from vertical-up) that allows free placement.
    /// Surfaces steeper than this will show the ghost red and block the drop.
    /// Hanging placement boards are exempt from this check.
    /// </summary>
    private const float MaxPlacementSlopeAngle = 30f;
    private bool _placerBlocked;
    private bool _canInteract = true;
    public bool CanInteract => _canInteract;
    public Interactable onlyAllowedInteractable;
    bool reticleActive = false;
    public bool ReticleActive => reticleActive;

    private bool _suspectCamActive = false;

    /// <summary>
    /// Suppresses all interaction and hides the reticle while the suspect cam is active.
    /// Safe to call before the reticle reference is assigned — the guard in HandleReticle
    /// will enforce the hidden state as soon as the reference is populated.
    /// </summary>
    public void SetSuspectCamMode(bool active)
    {
        _suspectCamActive = active;
        _canInteract = !active;
        if (reticle != null)
        {
            if (active)
                reticle.DisableReticle();
            else
                reticle.EnableReticle();
        }
    }
    
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


        // LMB triggers primary interaction (Interact — pickup, use).
        // E triggers alternate interaction (InteractAlternate — extract, secondary action).
        // When holding an item, both keys route to TryItemUse regardless.
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E))
        {
            if (_playerPickupController.HeldObject == null)
                TryWorldInteract(alternate: Input.GetKeyDown(KeyCode.E));
            else
                TryItemUse();
        }
    }
    
    void HandleReticle()
    {
        if (_suspectCamActive)
        {
            // Reticle ref may be null on first entry (early game) — hide it as soon as it's available.
            reticle?.DisableReticle();

            // Clear any highlight that was active before the view was entered.
            if (lastInteractable != null)
            {
                lastInteractable.Highlight(false);
                lastInteractable = null;
            }

            return;
        }

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
            
            // Only resolve from InteractableCollider when the MonoBehaviour is enabled —
            // SetInteractable(false) disables it as a belt-and-suspenders guard in case
            // the physics Collider is re-enabled by a late network update.
            if (interactableCollider != null && interactableCollider.enabled)
            {
                interactable = interactableCollider.Interactable;
            }

            // The local player's held object keeps its colliders active during the server
            // round-trip after pickup. Treat it as non-interactable immediately so the
            // tooltip doesn't re-appear on the very next frame after picking something up.
            if (interactable != null && interactable == _playerPickupController.HeldObject)
            {
                interactable = null;
                lastInteractable = null;
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
                    // When holding an item, only E triggers world interact so show [E].
                    // When empty-handed, LMB picks up / interacts (primary) and E extracts (alternate).
                    bool isHolding = _playerPickupController.HeldObject != null;
                    bool isWorldInteract = isHolding ? interactable is not PickableObject : true;

                    // Hide the button tooltip if the interactable requires a specific item
                    // but the player isn't holding a matching one.
                    bool showButtonTooltip = interactable.itemsThatCanInteractWith.Length == 0
                        || (isHolding && interactable.itemsThatCanInteractWith.Contains(pickupController.HeldObject.ItemData));

                    reticle.SetInteractState(true, interactable.interactText, isWorldInteract, showButtonTooltip, interactable.ShowInteractHint);
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
            bool placerInRange = hit.distance <= placementDistance;
            if (placerInRange)
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
                
                if (_playerPickupController.IsHoldingObject)
                {
                    reticle.SetInteractState(false);
                    reticle.SetTooFarState(false);
                    return;
                }
            }
            else
            {
                // Out of placement range — keep the placer visible but tint it red so the player knows they can't drop here
                if (Input.GetMouseButton(1) && !Input.GetMouseButton(0) && !_placerBlocked && pickupController.CanPickUpAndPlace && _playerPickupController.IsHoldingObject)
                {
                    CheckActivatePlacer(placementBoard, hit, false);
                }
                else if (ObjectPlacer.Instance.IsActive)
                {
                    ObjectPlacer.Instance.DeactivatePlacer();
                }

                if (_playerPickupController.IsHoldingObject)
                {
                    reticle.SetInteractState(false);
                    reticle.SetTooFarState(false);
                    return;
                }
            }
            
            lastInteractable = null;
        }
        else if (Physics.Raycast(ray, out RaycastHit surfaceHit, placementDistance, placementLayer))
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

            if (_playerPickupController.IsHoldingObject)
            {
                reticle.SetInteractState(false);
                reticle.SetTooFarState(false);
                return;
            }
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
        if (_playerPickupController.HeldObject.ItemData.cantUsePlacementBoard == true)
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

        // Slope check — hanging boards are exempt (they're intentionally vertical/angled).
        // For all other surfaces, block placement when the surface is steeper than MaxPlacementSlopeAngle.
        bool isHangingBoard = placementBoard != null && placementBoard.IsHanging;
        if (!isHangingBoard)
        {
            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (slopeAngle > MaxPlacementSlopeAngle)
                inRange = false;
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
    /// Handles Left Click while holding an item: performs item-on-item interactions or
    /// falls back to using the held item in the world. Never triggers world interact.
    /// </summary>
    void TryItemUse()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            _playerPickupController.TryUseObject();
            return;
        }

        Interactable interactable = hit.collider.GetComponent<Interactable>();
        InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();
        if (interactableCollider != null)
            interactable = interactableCollider.Interactable;

        if (IsControlledByOtherPlayer(interactable)) return;
        if (onlyAllowedInteractable != null && interactable != onlyAllowedInteractable) return;

        // No valid interactable under the cursor (e.g. hovering a placement board) —
        // fall through to using the held item in place, same as when nothing is hit.
        if (interactable == null || !interactable.enabled)
        {
            _playerPickupController.TryUseObject();
            return;
        }

        if (interactable.itemsThatCanInteractWith.Contains(pickupController.HeldObject.ItemData))
        {
            interactable.InteractWithItem(this, pickupController.HeldObject);
            _playerPickupController.TryUseObject();
        }
        else if (interactable is IHeldItemPassthrough)
        {
            // This interactable explicitly handles LMB input itself regardless of held item.
            // Do not call TryUseObject — the interactable manages the held-button lifecycle.
            interactable.Interact(this);
        }
        else
        {
            Debug.Log("Held object is not compatible with this interactable");
            _playerPickupController.TryUseObject();
        }
    }

    /// <summary>
    /// Handles E key (and Left Click when empty-handed): interacts with any interactable
    /// in range — pickups, world objects, slots, etc.
    /// When <paramref name="alternate"/> is true (E key), calls
    /// <see cref="Interactable.InteractAlternate"/>; otherwise calls
    /// <see cref="Interactable.Interact"/> (LMB).
    /// </summary>
    void TryWorldInteract(bool alternate = false)
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
            return;

        Interactable interactable = hit.collider.GetComponent<Interactable>();
        InteractableCollider interactableCollider = hit.collider.GetComponent<InteractableCollider>();
        if (interactableCollider != null)
            interactable = interactableCollider.Interactable;

        if (IsControlledByOtherPlayer(interactable)) return;
        if (onlyAllowedInteractable != null && interactable != onlyAllowedInteractable) return;
        if (interactable == null || !interactable.enabled) return;

        if (alternate)
            interactable.InteractAlternate(this);
        else
            interactable.Interact(this);

        reticle.SetInteractState(false);
        reticle.SetTooFarState(false);
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