using UnityEngine;

public class PlayerPickupController : MonoBehaviour
{
    public Transform holdPoint;
    public float holdSmoothness = 10f;

    private PickableObject heldObject;

    void Update()
    {
        if (heldObject != null)
        {
            MoveHeldObject();

            // Drop with E or right-click
            if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(1))
            {
                DropObject();
            }
        }
    }

    public void PickupObject(PickableObject obj)
    {
        // Drop existing object if holding something already
        if (heldObject != null)
        {
            DropObject();
        }

        heldObject = obj;

        // Disable physics & collisions
        if (obj.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = true;
        }

        if (obj.TryGetComponent<Collider>(out var col))
        {
            col.enabled = false;
        }

        // Snap immediately before smoothing
        obj.transform.position = holdPoint.position;
        obj.transform.rotation = holdPoint.rotation;

        // Notify item-specific logic
        obj.OnPickedUp();
    }

    public void DropObject()
    {
        if (heldObject == null) return;

        // Re-enable physics & collider
        if (heldObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = false;

            // Small forward toss for feel
            rb.AddForce(holdPoint.forward * 2f, ForceMode.Impulse);
        }

        if (heldObject.TryGetComponent<Collider>(out var col))
        {
            col.enabled = true;
        }

        // Notify item-specific logic
        heldObject.OnDropped();

        heldObject = null;
    }

    private void MoveHeldObject()
    {
        // Smooth movement to hold position
        heldObject.transform.position = Vector3.Lerp(
            heldObject.transform.position,
            holdPoint.position,
            holdSmoothness * Time.deltaTime
        );

        heldObject.transform.rotation = Quaternion.Lerp(
            heldObject.transform.rotation,
            holdPoint.rotation,
            holdSmoothness * Time.deltaTime
        );
    }
}
