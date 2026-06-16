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

    [Tooltip("Material applied to every renderer of an unavailable item's preview, rendering it as a solid black silhouette.")]
    [SerializeField] private Material _unavailableItemMaterial;

    private ShopItem currentItem;

    /// <summary>
    /// Spawns the shop item at the preview position and frames the camera to fit it.
    /// When <paramref name="obscure"/> is true, all renderers on the spawned instance have their
    /// materials replaced with <see cref="_unavailableItemMaterial"/> to produce a black silhouette.
    /// </summary>
    public void SpawnAndFrame(ShopItem shopItem, bool obscure = false)
    {
        // Clean up old item
        if (currentItem != null)
            Destroy(currentItem.gameObject);

        // Spawn item
        Vector3 rotationOffset = shopItem.RotationOffset;
        currentItem = Instantiate(shopItem, spawnPoint.position, Quaternion.Euler(itemRotation) * Quaternion.Euler(0, 180, 0) * Quaternion.Euler(rotationOffset)); 
        currentItem.transform.parent = spawnPoint;

        if (obscure && _unavailableItemMaterial != null)
        {
            foreach (Renderer r in currentItem.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.materials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = _unavailableItemMaterial;
                r.materials = mats;
            }
        }

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
