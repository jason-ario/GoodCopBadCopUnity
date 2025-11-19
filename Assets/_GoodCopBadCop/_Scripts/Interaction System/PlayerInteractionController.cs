using UnityEngine;

public class PlayerInteractionController : MonoBehaviour
{
    public Camera cam;
    public float interactDistance = 3f;
    public LayerMask interactLayer;

    public PlayerPickupController pickupController;  

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
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
}