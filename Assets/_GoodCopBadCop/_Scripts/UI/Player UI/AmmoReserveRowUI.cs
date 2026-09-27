using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the <see cref="AmmoReserveHUD"/> panel: an ammo icon plus a "current / max" label
/// for a single <see cref="AmmoType"/>. Plays a pop + colour flash when ammo is gained and a red
/// flash when the reserve is full.
/// </summary>
public class AmmoReserveRowUI : MonoBehaviour
{
    [Tooltip("Which reserve this row displays.")]
    [SerializeField] private AmmoType _ammoType;

    [Tooltip("Icon shown to the left of the amount.")]
    [SerializeField] private Image _icon;

    [Tooltip("Label showing 'current / max'.")]
    [SerializeField] private TMP_Text _amountText;

    [Header("Animation")]
    [Tooltip("Resting text colour.")]
    [SerializeField] private Color _normalColor = Color.white;

    [Tooltip("Flash colour when ammo is added to the reserve.")]
    [SerializeField] private Color _gainColor = new(1f, 0.82f, 0.25f, 1f);

    [Tooltip("Flash colour when the reserve is already full.")]
    [SerializeField] private Color _fullColor = new(1f, 0.35f, 0.3f, 1f);

    [Tooltip("Extra scale applied at the peak of the pop (0.3 = 130%).")]
    [SerializeField, Range(0f, 1f)] private float _popStrength = 0.3f;

    [Tooltip("Duration of the scale pop, in seconds.")]
    [SerializeField, Min(0.05f)] private float _popDuration = 0.3f;

    [Tooltip("Seconds the flash colour takes to fade back to the resting colour.")]
    [SerializeField, Min(0.05f)] private float _colorFadeDuration = 0.6f;

    private Tween _scaleTween;
    private Tween _colorTween;

    public AmmoType AmmoType => _ammoType;

    private void OnDisable() => ResetVisuals();

    /// <summary>Updates the label and shows the row only while the player has some of this ammo.</summary>
    public void SetAmount(int current, int max)
    {
        if (_amountText != null)
            _amountText.text = $"{current}<color=#FFFFFF88> / {max}</color>";

        bool visible = current > 0;
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    /// <summary>Pop + highlight used when ammo is added.</summary>
    public void PlayGain() => Play(_gainColor, _popStrength);

    /// <summary>Smaller pop + red flash used when the reserve is full.</summary>
    public void PlayFull() => Play(_fullColor, _popStrength * 0.5f);

    private void Play(Color flashColor, float strength)
    {
        if (_amountText == null || !isActiveAndEnabled) return;

        ResetVisuals();

        Transform target = _amountText.transform;
        _scaleTween = target.DOPunchScale(Vector3.one * strength, _popDuration, 6, 0.6f).SetLink(gameObject);

        _amountText.color = flashColor;
        _colorTween = DOTween.To(() => _amountText.color, c => _amountText.color = c, _normalColor, _colorFadeDuration)
            .SetEase(Ease.InQuad)
            .SetLink(gameObject);
    }

    private void ResetVisuals()
    {
        _scaleTween?.Kill();
        _colorTween?.Kill();
        _scaleTween = null;
        _colorTween = null;

        if (_amountText == null) return;
        _amountText.transform.localScale = Vector3.one;
        _amountText.color = _normalColor;
    }
}
