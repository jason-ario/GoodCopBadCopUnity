using UnityEngine;

/// <summary>
/// ScriptableObject describing a single hat cosmetic that can be worn by the player.
/// Create via Assets > Hats > Hat Data.
/// </summary>
[CreateAssetMenu(fileName = "New Hat Data", menuName = "Hats/Hat Data")]
public class HatData : ScriptableObject
{
    [Tooltip("Human-readable name shown in UI (e.g. shop or inventory).")]
    [SerializeField] private string displayName;
    public string DisplayName => displayName;

    [Tooltip("Optional sprite used in UI previews.")]
    [SerializeField] private Sprite previewSprite;
    public Sprite PreviewSprite => previewSprite;
}
