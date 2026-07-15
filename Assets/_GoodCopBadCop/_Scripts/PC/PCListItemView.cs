using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum PCListItemIcon
{
    Folder,
    Profile,
    Unknown
}

public sealed class PCListItemModel
{
    public PCListItemModel(string text, PCListItemIcon icon, Action onSelected = null, Texture2D profilePhoto = null, bool interactable = true)
    {
        Text = text;
        Icon = icon;
        OnSelected = onSelected;
        ProfilePhoto = profilePhoto;
        Interactable = interactable;
    }

    public string Text { get; }
    public PCListItemIcon Icon { get; }
    public Action OnSelected { get; }
    public Texture2D ProfilePhoto { get; }
    public bool Interactable { get; }
}

public sealed class PCListItemView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI textLabel;
    [SerializeField] private Image folderIcon;
    [SerializeField] private Image profileIcon;
    [SerializeField] private Image unknownIcon;
    [SerializeField] private ClickablePCElement clickableElement;

    private Sprite _profileSprite;

    public void Configure(PCListItemModel model)
    {
        DestroyProfileSprite();

        if (textLabel != null)
            textLabel.text = model?.Text ?? string.Empty;

        SetIcon(model);
        ConfigureClick(model);
    }

    private void SetIcon(PCListItemModel model)
    {
        SetImageActive(folderIcon, model?.Icon == PCListItemIcon.Folder);
        SetImageActive(profileIcon, model?.Icon == PCListItemIcon.Profile);
        SetImageActive(unknownIcon, model == null || model.Icon == PCListItemIcon.Unknown);

        if (profileIcon == null || model?.Icon != PCListItemIcon.Profile)
            return;

        if (model.ProfilePhoto == null)
        {
            SetImageActive(profileIcon, false);
            SetImageActive(unknownIcon, true);
            return;
        }

        _profileSprite = Sprite.Create(
            model.ProfilePhoto,
            new Rect(0, 0, model.ProfilePhoto.width, model.ProfilePhoto.height),
            new Vector2(0.5f, 0.5f));

        profileIcon.sprite = _profileSprite;
        profileIcon.preserveAspect = true;
        SetImageActive(profileIcon, true);
    }

    private void ConfigureClick(PCListItemModel model)
    {
        if (clickableElement == null)
            return;

        bool interactable = model != null && model.Interactable && model.OnSelected != null;
        clickableElement.enabled = interactable;
        clickableElement.SetClickHandler(interactable ? model.OnSelected : null);
    }

    private static void SetImageActive(Image image, bool active)
    {
        if (image == null)
            return;

        image.gameObject.SetActive(active);
        image.enabled = active;
    }

    private void DestroyProfileSprite()
    {
        if (_profileSprite == null)
            return;

        Destroy(_profileSprite);
        _profileSprite = null;
    }

    private void OnDestroy()
    {
        DestroyProfileSprite();
    }
}
