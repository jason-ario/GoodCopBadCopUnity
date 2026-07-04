using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SelectableTab : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    private static readonly Color SelectedBackgroundColor = Color.white;
    private static readonly Color DeselectedBackgroundColor = Color.black;
    private static readonly Color SelectedTextColor = Color.black;
    private static readonly Color DeselectedTextColor = Color.white;

    private bool isSelected = false;
    private Image backgroundImage;
    private TextMeshProUGUI labelText;

    public bool IsSelected => isSelected;
    [SerializeField] UnityEvent onSelected;

    private void Awake()
    {
        CacheStyleTargets();
        ApplySelectedStyle(isSelected);
    }

    public void Select()
    {
        SettingsMenu settingsMenu = GetComponentInParent<SettingsMenu>();
        if (settingsMenu != null && settingsMenu.OpenSettingsForTab(this))
        {
            return;
        }

        onSelected?.Invoke();
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
