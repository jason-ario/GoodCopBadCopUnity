using System;
using UnityEngine;

public class PlayerInteractionController : MonoBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;
    public LayerMask interactLayer;

    public PlayerPickupController pickupController;
    public ReticleController reticle;
    public PlayerAnimationController playerAnimationController;

    private void Awake()
    {
        playerAnimationController = GetComponent<PlayerAnimationController>();
    }

    void Update()
    {
        HandleReticle();

        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
    }

    void HandleReticle()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();

            if (interactable != null)
            {
                reticle.SetInteractState(true);
                return;
            }
        }

        reticle.SetInteractState(false);
    }

    void TryInteract()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();

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