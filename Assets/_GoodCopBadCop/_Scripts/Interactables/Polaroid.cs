using UnityEngine;

/// <summary>
/// A pickable polaroid photo that can display a <see cref="Texture2D"/> captured from the
/// <see cref="CameraPickup"/> viewfinder on its photo mesh surface.
///
/// The photo mesh is a child GameObject assigned in the inspector so the polaroid card
/// border and the image surface can be separate objects with independent materials.
/// <see cref="SetPhoto"/> applies the captured texture to the <c>_BaseMap</c> slot of the
/// photo mesh's material — matching the Toony Colors Pro shader used by <c>foto.mat</c>.
///
/// Texture display is local-only: only the client that took the photo sees the image.
/// All other clients see the default placeholder texture on the material.
///
/// Prefab requirements
/// ─────────────────────────────────────────────────────────────────────────────
///   • NetworkObject + NetworkTransform
///   • HighlightEffect   (required by <see cref="Interactable"/>)
///   • ParentConstraint  (required by <see cref="PickableObject"/>)
///   • Collider on the Interactable layer
///   • Child GameObject with a MeshRenderer using foto.mat → assign to <see cref="_photoRenderer"/>
/// </summary>
public class Polaroid : PickableObject
{
    private const string UsingToolBool = "UsingTool";

    private static readonly int BaseMapId     = Shader.PropertyToID("_BaseMap");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");

    [Header("Photo Display")]
    [Tooltip("The child MeshRenderer whose material _BaseMap slot displays the captured photo.")]
    [SerializeField] private MeshRenderer _photoRenderer;

    private Material _instanceMaterial;
    private Texture2D _ownedTexture;

    /// <summary>Returns the photo texture currently displayed, or null if none has been set.</summary>
    public Texture2D Photo => _ownedTexture;

    /// <summary>
    /// Applies <paramref name="photo"/> to the photo mesh's <c>_BaseMap</c> material slot.
    /// Creates a per-instance material copy on first call so each polaroid has independent texture state.
    ///
    /// Should only be called on the client that captured the image — texture data is not synced.
    /// </summary>
    /// <param name="photo">Texture2D read from the camera viewfinder RenderTexture.</param>
    /// <param name="takeOwnership">
    /// When true (default) this component destroys the texture in <see cref="OnPolaroidDestroyed"/>.
    /// Pass false when the caller manages the texture's lifetime externally.
    /// </param>
    public void SetPhoto(Texture2D photo, bool takeOwnership = true)
    {
        if (photo == null)
        {
            Debug.LogWarning("[Polaroid] SetPhoto called with a null texture.", this);
            return;
        }

        if (_photoRenderer == null)
        {
            Debug.LogWarning("[Polaroid] _photoRenderer is not assigned in the Inspector.", this);
            return;
        }

        EnsureInstanceMaterial();
        _instanceMaterial.SetTexture(BaseMapId,     photo);
        _instanceMaterial.SetTexture(EmissionMapId, photo);

        if (takeOwnership)
            _ownedTexture = photo;
    }

    /// <summary>
    /// Creates a per-instance material copy so setting a texture here does not affect
    /// every other polaroid that shares the same material asset on the prefab.
    /// </summary>
    private void EnsureInstanceMaterial()
    {
        if (_instanceMaterial != null) return;

        // Accessing .material creates and caches a per-instance copy automatically.
        _instanceMaterial = _photoRenderer.material;
    }

    /// <summary>Sets UsingTool on the player animator while LMB is held.</summary>
    public override void OnStartUse()
    {
        base.OnStartUse();
        playerPickupController?.PlayerAnimationController.SetAnimBool(UsingToolBool, true);
    }

    /// <summary>Clears UsingTool on the player animator when LMB is released.</summary>
    public override void OnStopUse()
    {
        base.OnStopUse();
        playerPickupController?.PlayerAnimationController.SetAnimBool(UsingToolBool, false);
    }

    public override void OnDropped()
    {
        base.OnDropped();
    }

    /// <summary>Cleans up the per-instance material copy and owned texture to avoid leaks.</summary>
    private void OnDestroy()
    {
        if (_instanceMaterial != null)
        {
            Destroy(_instanceMaterial);
            _instanceMaterial = null;
        }

        if (_ownedTexture != null)
        {
            Destroy(_ownedTexture);
            _ownedTexture = null;
        }
    }
}
