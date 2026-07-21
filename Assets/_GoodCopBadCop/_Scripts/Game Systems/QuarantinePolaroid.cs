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

        if (_nameText != null)
            _nameText.text = $"{suspectData.FirstName} {suspectData.LastName}";

        if (_daysLeftText != null)
            _daysLeftText.text = remainingDays == 1 ? "1 Day Left" : $"{remainingDays} Days Left";

        if (_photoRenderer != null && suspectData.IDPhoto != null)
        {
            _photoRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(BaseMapID, suspectData.IDPhoto);
            _photoRenderer.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Hides this slot when no suspect is currently assigned to it.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
