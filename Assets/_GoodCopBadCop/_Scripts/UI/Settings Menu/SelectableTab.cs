using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SelectableTab : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    private static readonly Color SelectedBackgroundColor = Color.white;
    private static readonly Color DeselectedBackgroundColor = Color.white;
    private static readonly Color SelectedTextColor = Color.white;
    private static readonly Color DeselectedTextColor = new(0.58f, 0.58f, 0.58f, 1f);

    private bool isSelected = false;
    [SerializeField] private Sprite selectedBackgroundSprite;
    [SerializeField] private Sprite deselectedBackgroundSprite;
    private Image backgroundImage;
    private TextMeshProUGUI labelText;
    private readonly Subject<SelectableTab> selected = new();

    public bool IsSelected => isSelected;
    public Observable<SelectableTab> Selected => selected;

    private void Awake()
    {
        CacheStyleTargets();
        ApplySelectedStyle(isSelected);
    }

    public void Select()
    {
        selected.OnNext(this);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        ApplySelectedStyle(selected);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Select();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Select();
    }

    private void ApplySelectedStyle(bool selected)
    {
        CacheStyleTargets();

        if (backgroundImage != null)
        {
            backgroundImage.sprite = selected ? selectedBackgroundSprite : deselectedBackgroundSprite;
            backgroundImage.color = selected ? SelectedBackgroundColor : DeselectedBackgroundColor;
        }

        if (labelText != null)
        {
            labelText.color = selected ? SelectedTextColor : DeselectedTextColor;
        }
    }

    private void CacheStyleTargets()
    {
        if (backgroundImage == null)
        {
            Transform background = transform.Find("BG");
            if (background != null)
            {
                backgroundImage = background.GetComponent<Image>();
            }
        }

        if (labelText != null)
        {
            return;
        }

        TextMeshProUGUI[] textComponents = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI textComponent in textComponents)
        {
            textComponent.raycastTarget = false;

            if (textComponent.text == ">")
            {
                continue;
            }

            labelText = textComponent;
            break;
        }
    }
}
