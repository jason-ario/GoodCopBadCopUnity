using UnityEngine;

/// <summary>
/// Assigns a pre-baked <see cref="Texture2D"/> to the <c>_OverlayMap</c> slot on a paper
/// renderer, replacing the live RenderTexture + camera system used by <see cref="ExamPage"/>.
/// Use this on shop/display variants of exam pages where the checklist state never changes.
///
/// Workflow:
///   1. Use the bake tool on ExamPageEditor (Play Mode required) to capture a PNG.
///   2. Assign that PNG to <see cref="_bakedOverlay"/> in the Inspector.
///   3. Assign the page's <see cref="SkinnedMeshRenderer"/> to <see cref="_paperRenderer"/>.
/// </summary>
public class StaticExamPageDisplay : MonoBehaviour
{
    private static readonly int OverlayMapProperty = Shader.PropertyToID("_OverlayMap");

    [Tooltip("The RenderTexture or Texture2D captured by the bake tool.")]
    [SerializeField] private Texture _bakedOverlay;

    [Tooltip("The SkinnedMeshRenderer whose material slot exposes _OverlayMap.")]
    [SerializeField] private SkinnedMeshRenderer _paperRenderer;

    [Tooltip("Index of the material slot that contains _OverlayMap. Matches ExamPage (default: 1).")]
    [SerializeField] private int _materialSlotIndex = 1;

    private Material _materialInstance;

    private void Awake() => ApplyOverlay();

    /// <summary>Stamps the baked texture onto a per-instance material clone at startup.</summary>
    private void ApplyOverlay()
    {
        if (_bakedOverlay == null || _paperRenderer == null)
        {
            Debug.LogWarning($"[StaticExamPageDisplay] Missing references on '{name}'. Assign _bakedOverlay and _paperRenderer.", this);
            return;
        }

        if (_materialSlotIndex < 0 || _materialSlotIndex >= _paperRenderer.sharedMaterials.Length)
        {
            Debug.LogWarning($"[StaticExamPageDisplay] _materialSlotIndex {_materialSlotIndex} is out of range on '{name}'.", this);
            return;
        }

        _materialInstance = new Material(_paperRenderer.sharedMaterials[_materialSlotIndex]);
        _materialInstance.SetTexture(OverlayMapProperty, _bakedOverlay);

        // Replace all slots with the instance so every sub-mesh renders correctly,
        // mirroring the approach used in ExamPage.SetupRenderTexture.
        Material[] slots = new Material[_paperRenderer.sharedMaterials.Length];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = _materialInstance;
        _paperRenderer.materials = slots;
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
            Destroy(_materialInstance);
    }
}
