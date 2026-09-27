using UnityEngine;

/// <summary>
/// Picks a random texture from a pool and applies it to the sibling Renderer's main texture
/// slot: <c>_BaseMap</c> (GraffitiScrubDecal / blood splatter decals) or, for legacy materials,
/// <c>_RevealMap</c> (UVReveal).
///
/// The texture is written through the renderer's MaterialPropertyBlock using Get → Set, so
/// other per-renderer values in the same block (e.g. GraffitiInteractable's _ScrubProgress)
/// are preserved and no per-instance material copy is created.
///
/// Call Randomize() manually at any time to re-roll the texture.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class BloodTextureRandomizer : MonoBehaviour
{
    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly int RevealMapId = Shader.PropertyToID("_RevealMap");

    [Header("Textures")]
    [Tooltip("Pool of textures to randomly select from. Must contain at least one entry.")]
    [SerializeField] private Texture2D[] textures;

    [Header("Timing")]
    [Tooltip("Pick a new random texture when the scene starts.")]
    [SerializeField] private bool randomizeOnStart = true;

    [Tooltip("Re-randomize every N seconds. Set to 0 to disable interval re-rolling.")]
    [SerializeField, Min(0f)] private float reRandomizeInterval = 0f;

    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private int _textureId;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _block = new MaterialPropertyBlock();

        Material mat = _renderer.sharedMaterial;
        _textureId = mat != null && !mat.HasTexture(BaseMapId) && mat.HasTexture(RevealMapId)
            ? RevealMapId
            : BaseMapId;
    }

    private void Start()
    {
        if (randomizeOnStart)
            Randomize();

        if (reRandomizeInterval > 0f)
            InvokeRepeating(nameof(Randomize), reRandomizeInterval, reRandomizeInterval);
    }

    /// <summary>Sets the main texture to a uniformly random texture from the pool.</summary>
    public void Randomize()
    {
        if (textures == null || textures.Length == 0)
        {
            Debug.LogWarning($"[BloodTextureRandomizer] No textures assigned on '{gameObject.name}'.", this);
            return;
        }

        Texture2D tex = textures[Random.Range(0, textures.Length)];
        if (tex == null) return;

        _renderer.GetPropertyBlock(_block);
        _block.SetTexture(_textureId, tex);
        _renderer.SetPropertyBlock(_block);
    }
}
