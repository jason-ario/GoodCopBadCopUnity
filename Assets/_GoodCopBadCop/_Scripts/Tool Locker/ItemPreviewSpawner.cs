using UnityEngine;

public class ItemPreviewSpawner : MonoBehaviour
{
    [Header("References")]
    public Camera previewCamera;
    public Transform spawnPoint;

    [Header("Framing Settings")]
    [Range(1.05f, 1.5f)]
    public float paddingMultiplier = 1.15f;

    [Tooltip("Optional manual rotation for the spawned item")]
    public Vector3 itemRotation;

    private ShopItem currentItem;

    public void SpawnAndFrame(ShopItem shopItem)
    {
        // Clean up old item
        if (currentItem != null)
            Destroy(currentItem.gameObject);

        // Spawn item
        Vector3 rotationOffset = shopItem.RotationOffset;
        // Combine the base rotations with the rotationOffset from the shopItem
        currentItem = Instantiate(shopItem, spawnPoint.position, Quaternion.Euler(itemRotation) * Quaternion.Euler(0, 180, 0) * Quaternion.Euler(rotationOffset)); 
        currentItem.transform.parent = spawnPoint;

        FrameObject(previewCamera, currentItem);
    }

    void FrameObject(Camera cam, ShopItem target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        // Calculate combined bounds
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        float radius = bounds.extents.magnitude * paddingMultiplier;

        float verticalFOV = cam.fieldOfView * Mathf.Deg2Rad;
        float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(verticalFOV / 2f) * cam.aspect);

        float distanceV = radius / Mathf.Sin(verticalFOV / 2f);
        float distanceH = radius / Mathf.Sin(horizontalFOV / 2f);
        float distance = Mathf.Max(distanceV, distanceH);

        cam.transform.position =
            bounds.center - cam.transform.forward * distance;

        cam.transform.LookAt(bounds.center);
    }
}