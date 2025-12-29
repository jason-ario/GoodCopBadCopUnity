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

    private void Awake()
    {
        playerAnimationController = GetComponent<PlayerAnimationController>();
        reticle = GameObject.FindFirstObjectByType<ReticleController>();
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
        
        if(lastInteractable != null) lastInteractable.Highlight(false);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            Interactable interactable = hit.collider.GetComponent<Interactable>();

            if (interactable != null && interactable.enabled)
            {
                reticle.SetInteractState(true);
                interactable.Highlight(true);
                lastInteractable = interactable;
                return;
            }
            else
            {
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