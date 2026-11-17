using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Folder-style hover feedback: while the hitbox is hovered or selected, the folder visual slides
/// out of its resting spot, tilts and grows slightly, as if being pulled from a stack.
/// Put this on the clickable hitbox and point <see cref="folder"/> at the visual. The hitbox itself
/// does not move, so the hover area stays stable while the folder animates.
/// </summary>
public class UIFolderHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private RectTransform folder;
    [Tooltip("Slide applied to the folder's anchored position (parent units) when fully hovered.")]
    [SerializeField] private Vector2 hoverOffset = new Vector2(-10f, 28f);
    [Tooltip("Extra Z rotation in degrees when fully hovered.")]
    [SerializeField] private float hoverRotation = 1.5f;
    [SerializeField, Min(0f)] private float hoverScale = 1.04f;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.08f;
    [SerializeField] private AudioClip sfxOnHover;
    [SerializeField] private AudioClip sfxOnClick;

    private Button button;
    private UIWobble wobble;
    private Vector2 basePosition;
    private Quaternion baseRotation;
    private Vector3 baseScale;
    private bool cached;
    private bool hovered;
    private bool selected;
    private float amount;
    private float velocity;

    private bool Interactable => button == null || button.interactable;

    private void Awake()
    {
        button = GetComponent<Button>();
        Cache();
    }

    private void Cache()
    {
        if (cached || folder == null) return;
        wobble = folder.GetComponent<UIWobble>();
        basePosition = folder.anchoredPosition;
        baseRotation = folder.localRotation;
        baseScale = folder.localScale;
        cached = true;
    }

    private void OnDisable()
    {
        hovered = selected = false;
        amount = velocity = 0f;
        Apply(0f);
    }

    private void Update()
    {
        if (!cached) return;
        float target = (hovered || selected) && Interactable ? 1f : 0f;
        if (Mathf.Approximately(amount, target)) return;

        // Unscaled so the menu still animates if Time.timeScale is 0.
        amount = Mathf.SmoothDamp(amount, target, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (Mathf.Abs(amount - target) < 0.001f) amount = target;
        Apply(amount);
    }

    private void Apply(float t)
    {
        if (!cached) return;

        Vector2 offset = hoverOffset * t;
        if (wobble != null) wobble.SetExtraOffset(offset);
        else folder.anchoredPosition = basePosition + offset;

        folder.localRotation = baseRotation * Quaternion.Euler(0f, 0f, hoverRotation * t);
        folder.localScale = baseScale * Mathf.LerpUnclamped(1f, hoverScale, t);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!Interactable) return;
        hovered = true;
        SFXController.Instance?.Play(sfxOnHover);
    }

    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    /// <summary>Plays the click sound on pointer down, before an OnClick listener can open a browser or hide the menu.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Interactable) return;
        SFXController.Instance?.Play(sfxOnClick);
    }

    /// <summary>Mirrors hover for gamepad/keyboard navigation.</summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (!Interactable) return;
        selected = true;
        SFXController.Instance?.Play(sfxOnHover);
    }

    public void OnDeselect(BaseEventData eventData) => selected = false;
}
