using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractionController : NetworkBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;

    [Tooltip("Max distance at which the player can drop a held object using right-click. Independent of interact distance.")]
    public float placementDistance = 5f;

    public LayerMask interactLayer;

    [Tooltip("Layers that count as valid free-placement surfaces (floors, desks, world geometry, etc.)")]
    public LayerMask placementLayer;

    [Tooltip("When the placement raycast doesn't land exactly on a PlacementBoard's own collider (e.g. it hits the desk/mat right next to a thin board trigger), search within this radius of the hit point for a nearby PlacementBoard and snap to it instead. This makes tutorial hand-off points (like HandOffPoint) register even when the free-placement surface swallows the raycast.")]
    [SerializeField] private float placementBoardSnapRadius = 0.15f;

    public PlayerPickupController pickupController;
    public PlayerMovementController playerMovementController;
    public ReticleController reticle;
    public PlayerAnimationController playerAnimationController; 
    Interactable lastInteractable;
    private PlayerPickupController _playerPickupController;
    private ThrowController _throwController;
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

    // ── Controller trigger helpers ─────────────────────────────────────────────
    // RT  (rightTrigger)  = LMB — primary interact / item use
    // LT  (leftTrigger)   = RMB — placement mode hold
    // RB  (rightShoulder) = MMB — throw charge / release
    // InputSystem's default press-point (0.5) converts the analog axes to the
    // wasPressedThisFrame / isPressed / wasReleasedThisFrame digital events we need.
    private bool LmbDown => Input.GetMouseButtonDown(0) || (Gamepad.current?.rightTrigger.wasPressedThisFrame  ?? false);
    private bool LmbHeld => Input.GetMouseButton(0)     || (Gamepad.current?.rightTrigger.isPressed             ?? false);
    private bool RmbHeld => Input.GetMouseButton(1)     || (Gamepad.current?.leftTrigger.isPressed              ?? false);
    private bool RmbUp   => Input.GetMouseButtonUp(1)   || (Gamepad.current?.leftTrigger.wasReleasedThisFrame   ?? false);
    private bool MmbDown => Input.GetMouseButtonDown(2) || (Gamepad.current?.rightShoulder.wasPressedThisFrame  ?? false);
    private bool MmbUp   => Input.GetMouseButtonUp(2)   || (Gamepad.current?.rightShoulder.wasReleasedThisFrame ?? false);
    // E key — world interact / alternate interact. buttonWest maps to Xbox X / PlayStation Square.
    private bool EKeyDown => Input.GetKeyDown(KeyCode.E) || (Gamepad.current?.buttonWest.wasPressedThisFrame ?? false);
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
        _throwController = GetComponent<ThrowController>();
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
        
        if (RmbUp)
        {
            _placerBlocked = false;
        }


        // LMB / RT triggers primary interaction (Interact — pickup, use).
        // E triggers alternate interaction (InteractAlternate — extract, secondary action).
        // When holding an item, both keys route to TryItemUse regardless.
        // Exception: when the cursor is visible (e.g. notebook draw mode), LMB belongs to the
        // ClickDetector — skip TryItemUse so it doesn't double-fire and call OnStartUse again.
        if (LmbDown || EKeyDown)
        {
            if (_playerPickupController.HeldObject == null)
                TryWorldInteract(alternate: EKeyDown);
            else if (!Cursor.visible || EKeyDown)
                TryItemUse();
        }

        HandleThrowInput();
    }
    
    /// <summary>
    /// Routes F key input to <see cref="ThrowController"/>. Automatically cancels
    /// any in-progress charge when the player is no longer holding an item
    /// (e.g. item was snatched by another player or dropped via other means).
    /// </summary>
    private void HandleThrowInput()
    {
        if (_throwController == null) return;

        if (!_playerPickupController.IsHoldingObject)
        {
            if (_throwController.IsCharging) _throwController.CancelCharge();
            return;
        }

        if (MmbDown)
            _throwController.StartCharge();

        if (_throwController.IsCharging)
            _throwController.UpdateCharge(Time.deltaTime);

        if (MmbUp)
            _throwController.ReleaseThrow();
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
        if (TryGetBestInteractHit(ray, out RaycastHit hit))
        {
            Interactable interactable = ResolveInteractable(hit.collider);

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
            // Search children too — a PlacementSlot used purely for ghosting/snap-pose is often
            // authored on a dedicated child Transform (e.g. a mail cubby's snap point) separate
            // from the GameObject that carries the trigger collider the raycast actually hits.
            PlacementBoard placementBoard = hit.collider.GetComponentInChildren<PlacementBoard>();
            if (placementBoard == null)
                placementBoard = FindNearbyPlacementBoard(hit.point);

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

            // Boards opted into ShowGhostWhileAiming (e.g. a mail cubby's PlacementSlot) show the
            // ghost — and put the reticle into its interact/hover state — as soon as the player
            // aims at them while holding an item, without needing to hold RMB first.
            bool aimGhostRequested = placementBoard != null && placementBoard.ShowGhostWhileAiming && _playerPickupController.IsHoldingObject;

            if (placerInRange)
            {
                if (LmbDown && RmbHeld)
                {
                    _placerBlocked = true;
                }

                if ((aimGhostRequested || (RmbHeld && !LmbHeld && !_placerBlocked)) && pickupController.CanPickUpAndPlace)
                {
                    CheckActivatePlacer(placementBoard, hit, true);
                }
                else
                {
                    if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
                }
                
                if (_playerPickupController.IsHoldingObject)
                {
                    if (aimGhostRequested)
                        reticle.SetInteractState(true, placementBoard.AimHoverText);
                    else
                        reticle.SetInteractState(false);
                    reticle.SetTooFarState(false);
                    return;
                }
            }
            else
            {
                // Out of placement range — keep the placer visible but tint it red so the player knows they can't drop here
                if ((aimGhostRequested || (RmbHeld && !LmbHeld && !_placerBlocked)) && pickupController.CanPickUpAndPlace && _playerPickupController.IsHoldingObject)
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
        else if (Physics.Raycast(ray, out RaycastHit surfaceHit, placementDistance, placementLayer, QueryTriggerInteraction.Ignore))
        {
            // No interactable hit but we did hit a placement surface — handle free placement.
            // The surface itself usually isn't a PlacementBoard, but a tutorial hand-off
            // board may sit right on top of it, so snap to one nearby if present.
            PlacementBoard nearbyBoard = FindNearbyPlacementBoard(surfaceHit.point);
            bool nearbyAimGhostRequested = nearbyBoard != null && nearbyBoard.ShowGhostWhileAiming && _playerPickupController.IsHoldingObject;

            if (LmbDown && RmbHeld)
            {
                _placerBlocked = true;
            }

            if ((nearbyAimGhostRequested || (RmbHeld && !LmbHeld && !_placerBlocked)) && pickupController.CanPickUpAndPlace)
            {
                CheckActivatePlacer(nearbyBoard, surfaceHit, true);
            }
            else
            {
                if (ObjectPlacer.Instance.IsActive) ObjectPlacer.Instance.DeactivatePlacer();
            }

            if (_playerPickupController.IsHoldingObject)
            {
                if (nearbyAimGhostRequested)
                    reticle.SetInteractState(true, nearbyBoard.AimHoverText);
                else
                    reticle.SetInteractState(false);
                reticle.SetTooFarState(false);
                return;
            }
        }
        else
        {
            // Nothing hit — keep the placer visible but red if it is already active
            if (RmbHeld && !LmbHeld && !_placerBlocked && pickupController.CanPickUpAndPlace && _playerPickupController.IsHoldingObject && ObjectPlacer.Instance.IsActive)
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
    /// Resolves the <see cref="Interactable"/> associated with a hit collider, following
    /// <see cref="InteractableCollider"/> indirection when present and enabled (belt-and-suspenders
    /// guard for late network re-enables of the underlying physics Collider).
    /// </summary>
    private Interactable ResolveInteractable(Collider collider)
    {
        Interactable interactable = collider.GetComponent<Interactable>();
        InteractableCollider interactableCollider = collider.GetComponent<InteractableCollider>();

        if (interactableCollider != null && interactableCollider.enabled)
        {
            interactable = interactableCollider.Interactable;
        }

        return interactable;
    }

    /// <summary>
    /// Performs the interact-layer raycast against ALL overlapping colliders instead of just the
    /// single closest one. When two interactables' colliders occupy nearly the same distance along
    /// the ray (e.g. a pickup sitting directly in front of another interactable), Unity's raycast
    /// ordering for near-equal distances is unstable and can flip between them every frame, making
    /// the front item impossible to reliably highlight/grab. This resolves that by:
    /// 1) Preferring whichever interactable was highlighted last frame if it's still among the
    ///    near-closest candidates (sticky selection — kills the frame-to-frame flicker).
    /// 2) Otherwise preferring the closest candidate that actually resolves to an enabled
    ///    Interactable over a closer but non-interactable collider hit.
    /// </summary>
    private bool TryGetBestInteractHit(Ray ray, out RaycastHit bestHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, 10f, interactLayer);

        if (hits.Length == 0)
        {
            bestHit = default;
            return false;
        }

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        const float stickyEpsilon = 0.05f;
        float closestDistance = hits[0].distance;

        if (lastInteractable != null)
        {
            for (int i = 0; i < hits.Length && hits[i].distance <= closestDistance + stickyEpsilon; i++)
            {
                if (ResolveInteractable(hits[i].collider) == lastInteractable)
                {
                    bestHit = hits[i];
                    return true;
                }
            }
        }

        for (int i = 0; i < hits.Length && hits[i].distance <= closestDistance + stickyEpsilon; i++)
        {
            Interactable candidate = ResolveInteractable(hits[i].collider);
            if (candidate != null && candidate.enabled)
            {
                bestHit = hits[i];
                return true;
            }
        }

        bestHit = hits[0];
        return true;
    }

    /// <summary>
    /// Searches within <see cref="placementBoardSnapRadius"/> of a placement point for the closest
    /// PlacementBoard (measured to each candidate collider's closest surface point, not its center).
    /// Used to make thin/small PlacementBoard triggers (e.g. tutorial hand-off points) register even
    /// when the free-placement raycast lands on the surface right next to them instead of on the
    /// board's own collider directly.
    /// </summary>
    PlacementBoard FindNearbyPlacementBoard(Vector3 point)
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(point, placementBoardSnapRadius, ~0, QueryTriggerInteraction.Collide);

        PlacementBoard closestBoard = null;
        float closestDistance = float.MaxValue;

        foreach (Collider col in nearbyColliders)
        {
            // Search children too — see the matching comment above where placementBoard is
            // first resolved from the raycast hit.
            PlacementBoard board = col.GetComponentInChildren<PlacementBoard>();
            if (board == null) continue;

            float distance = Vector3.Distance(point, col.ClosestPoint(point));
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestBoard = board;
            }
        }

        return closestBoard;
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

        // A PlacementSlot forces the object into an exact fixed pose (e.g. a mail bin's opening
        // or a mail cubby slot) instead of following the raycast hit point / surface normal —
        // this stops the ghost from appearing to stick to the side of the receptacle when the
        // player's aim lands slightly off-center.
        PlacementSlot placementSlot = placementBoard as PlacementSlot;

        // Slope check — hanging boards and exact placement slots are exempt (a slot's own pose
        // is authoritative regardless of what surface normal the raycast happened to hit).
        bool isHangingBoard = placementBoard != null && placementBoard.IsHanging;
        if (!isHangingBoard && placementSlot == null)
        {
            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (slopeAngle > MaxPlacementSlopeAngle)
                inRange = false;
        }

        // Determine placement pose: an exact PlacementSlot pose takes priority, then a generic
        // board's rotation (still following the hit point for position), otherwise align to the
        // surface normal at the raycast hit point for freeform placement.
        Quaternion targetRotation = placementSlot != null
            ? placementSlot.SnapPoint.rotation
            : placementBoard != null
                ? placementBoard.transform.rotation
                : Quaternion.FromToRotation(Vector3.up, hit.normal);

        Vector3 targetPosition = placementSlot != null ? placementSlot.SnapPoint.position : hit.point;

        reticle.SetInteractState(false);

        if (!ObjectPlacer.Instance.IsActive)
        {
            ObjectPlacer.Instance.SetItem(_playerPickupController.HeldObject.ItemData);
            ObjectPlacer.Instance.ActivatePlacer(placementBoard);
            ObjectPlacer.Instance.transform.rotation = targetRotation;
            ObjectPlacer.Instance.transform.position = targetPosition;
        }

        ObjectPlacer.Instance.transform.rotation = Quaternion.Lerp(ObjectPlacer.Instance.transform.rotation, targetRotation, Time.deltaTime * objectPlacerLerpSpeed);
        ObjectPlacer.Instance.transform.position = Vector3.Lerp(ObjectPlacer.Instance.transform.position, targetPosition, Time.deltaTime * objectPlacerLerpSpeed);
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