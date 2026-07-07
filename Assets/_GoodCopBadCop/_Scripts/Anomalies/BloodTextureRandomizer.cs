using UnityEngine;

/// <summary>
/// Picks a random texture from a pool and applies it to the UVReveal shader's _RevealMap
/// property on the sibling Renderer. Because UVRevealObject only writes light-related
/// properties (_UVLightPositions, _UVLightDirections, etc.) to its MaterialPropertyBlock,
/// changing _RevealMap on the per-instance material does not interfere with it.
///
/// Call Randomize() manually at any time to re-roll the texture.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class BloodTextureRandomizer : MonoBehaviour
{
    private static readonly int RevealMapId = Shader.PropertyToID("_RevealMap");

    [Header("Textures")]
    [Tooltip("Pool of textures to randomly select from. Must contain at least one entry.")]
    [SerializeField] private Texture2D[] textures;

    [Header("Timing")]
    [Tooltip("Pick a new random texture when the scene starts.")]
    [SerializeField] private bool randomizeOnStart = true;

    [Tooltip("Re-randomize every N seconds. Set to 0 to disable interval re-rolling.")]
    [SerializeField, Min(0f)] private float reRandomizeInterval = 0f;

    private Material _materialInstance;

    private void Awake()
    {
        // renderer.material creates a per-instance copy so we never mutate the shared asset.
        _materialInstance = GetComponent<Renderer>().material;
    }

    private void Start()
    {
        if (randomizeOnStart)
            Randomize();

        if (reRandomizeInterval > 0f)
            InvokeRepeating(nameof(Randomize), reRandomizeInterval, reRandomizeInterval);
    }

    /// <summary>Sets _RevealMap to a uniformly random texture from the pool.</summary>
    public void Randomize()
    {
        if (textures == null || textures.Length == 0)
        {
            Debug.LogWarning($"[BloodTextureRandomizer] No textures assigned on '{gameObject.name}'.", this);
            return;
        }

        _materialInstance.SetTexture(RevealMapId, textures[Random.Range(0, textures.Length)]);
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
            Destroy(_materialInstance);
    }
}
