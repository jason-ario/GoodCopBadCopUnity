using TMPro;
using UnityEngine;

/// <summary>
/// Controls a single polaroid slot on the Quarantine Board.
/// Auto-discovers its child renderers and text components in Awake.
/// Call Setup() to populate the slot; call Hide() to empty it.
/// </summary>
public class QuarantinePolaroid : MonoBehaviour
{
    [SerializeField] private MeshRenderer _photoRenderer;
    [SerializeField] private TextMeshPro  _nameText;
    [SerializeField] private TextMeshPro  _daysLeftText;

    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");

    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();

        if (_photoRenderer == null)
        {
            Transform t = transform.Find("Photo (1)");
            if (t != null) _photoRenderer = t.GetComponent<MeshRenderer>();
        }

        if (_nameText == null)
        {
            Transform t = transform.Find("Character Name");
            if (t != null) _nameText = t.GetComponent<TextMeshPro>();
        }

        if (_daysLeftText == null)
        {
            Transform t = transform.Find("Days Left");
            if (t != null) _daysLeftText = t.GetComponent<TextMeshPro>();
        }
    }

    /// <summary>Populates this polaroid slot with a quarantined suspect's data.</summary>
    public void Setup(SuspectData suspectData, int remainingDays)
    {
        gameObject.SetActive(true);
        _suspectData = suspectData;
        ApplyName();

        if (_daysLeftText != null)
            _daysLeftText.text = remainingDays == 1 ? "1 Day Left" : $"{remainingDays} Days Left";

        if (_photoRenderer != null && suspectData.IDPhoto != null)
        {
            _photoRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(BaseMapID, suspectData.IDPhoto);
            _photoRenderer.SetPropertyBlock(_mpb);
        }
    }

    private SuspectData _suspectData;

    private void OnEnable() { Loc.LanguageChanged += ApplyName; }

    private void OnDisable() { Loc.LanguageChanged -= ApplyName; }

    // Name in the local language, matching the ID card / registry spelling (Loc.Name).
    private void ApplyName()
    {
        if (_nameText != null && _suspectData != null)
            _nameText.text = $"{Loc.Name(_suspectData.FirstName)} {Loc.Name(_suspectData.LastName)}".Trim();
    }

    /// <summary>Hides this slot when no suspect is currently assigned to it.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
