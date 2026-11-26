using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Look of the lobby row's kick button, in the style of <see cref="TextButton"/>: a quiet label at
/// rest, the brush-stroke bar revealed on hover / gamepad focus, and a red bar while armed
/// (SURE?) so the confirming click is unmistakable. Armed state is driven by <see cref="PlayerInfoPanel"/>.
/// </summary>
[RequireComponent(typeof(Button))]
public class KickButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Image bar;
    [SerializeField] private TMP_Text label;

    [Header("Colours")]
    [SerializeField] private Color restLabelColor = new Color(0.45f, 0.12f, 0.1f, 0.6f);
    [SerializeField] private Color hoverBarColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private Color hoverLabelColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color armedBarColor = new Color(0.72f, 0.12f, 0.08f, 1f);
    [SerializeField] private Color armedLabelColor = new Color(1f, 1f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioClip sfxOnHover;

    private bool _hovered;
    private bool _selected;
    private bool _armed;

    private void OnEnable() => Apply();

    private void OnDisable()
    {
        _hovered = false;
        _selected = false;
        _armed = false;
    }

    public void SetArmed(bool armed)
    {
        _armed = armed;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHover(ref _hovered, true);
    public void OnPointerExit(PointerEventData eventData) => SetHover(ref _hovered, false);
    public void OnSelect(BaseEventData eventData) => SetHover(ref _selected, true);
    public void OnDeselect(BaseEventData eventData) => SetHover(ref _selected, false);

    private void SetHover(ref bool flag, bool value)
    {
        bool wasHot = _hovered || _selected;
        flag = value;
        if (!wasHot && (_hovered || _selected) && sfxOnHover != null)
            SFXController.Instance?.Play(sfxOnHover);
        Apply();
    }

    private void Apply()
    {
        bool hot = _hovered || _selected;
        if (bar != null)
        {
            bar.enabled = _armed || hot;
            bar.color = _armed ? armedBarColor : hoverBarColor;
        }
        if (label != null)
            label.color = _armed ? armedLabelColor : hot ? hoverLabelColor : restLabelColor;
    }
}
