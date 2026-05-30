using TMPro;
using UnityEngine;

/// <summary>
/// World-space floating label that displays remaining ink uses for a stamp slot.
/// Shown when the player's reticle hovers over the stamp, hidden otherwise.
/// Billboards toward the main camera every LateUpdate.
///
/// Attach this component to the root of the World Space Canvas that lives as a child of
/// each InkStamp slot GameObject. Assign <see cref="countText"/> in the Inspector.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class StampInkLabel : MonoBehaviour
{
    private const string InfiniteText = "\u221e";

    [SerializeField] private TextMeshProUGUI countText;

    private StampContainer.StampType _stampType;
    private bool _isInfinite;
    private Camera _mainCamera;

    private void Awake()
    {
        _mainCamera = Camera.main;
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        StampInkManager.OnInkChanged += HandleInkChanged;
    }

    private void OnDisable()
    {
        StampInkManager.OnInkChanged -= HandleInkChanged;
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            return;
        }

        // Billboard: always face the camera
        Vector3 toCamera = transform.position - _mainCamera.transform.position;
        if (toCamera != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(toCamera);
    }

    /// <summary>
    /// Configures the label for the given stamp type and makes it visible.
    /// Call this from <see cref="InkStamp.OnHighlight"/>.
    /// </summary>
    public void Show(StampContainer.StampType stampType)
    {
        _stampType = stampType;
        _isInfinite = stampType == StampContainer.StampType.Pass;
        RefreshText();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the label. Call this from <see cref="InkStamp.OnStopHighlight"/>.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void RefreshText()
    {
        if (countText == null) return;

        if (_isInfinite)
        {
            countText.text = InfiniteText;
            return;
        }

        int uses = StampInkManager.Instance != null
            ? StampInkManager.Instance.GetUses(_stampType)
            : GetDefaultUses();

        int max = StampInkManager.Instance != null
            ? StampInkManager.Instance.GetMaxUses(_stampType)
            : GetDefaultUses();

        countText.text = $"{uses}/{max}";
    }

    private void HandleInkChanged(StampContainer.StampType type, int newCount)
    {
        if (type != _stampType || !gameObject.activeSelf || countText == null) return;

        int max = StampInkManager.Instance != null
            ? StampInkManager.Instance.GetMaxUses(_stampType)
            : GetDefaultUses();

        countText.text = $"{newCount}/{max}";
    }

    /// <summary>
    /// Fallback defaults before <see cref="StampInkManager"/> is available on this client.
    /// </summary>
    private int GetDefaultUses()
    {
        return _stampType switch
        {
            StampContainer.StampType.Quarantine => 3,
            StampContainer.StampType.Kill       => 2,
            _                                   => 0,
        };
    }
}
