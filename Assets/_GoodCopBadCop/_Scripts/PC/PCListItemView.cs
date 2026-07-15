using System;
using System.Collections.Generic;
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
    private const string TextObjectName = "Text";
    private const string IconWrapperObjectName = "IconWrapper";
    private const string FolderIconName = "Folder";
    private const string ProfilePhotoIconName = "Pofile Photo";
    private const string ProfilePhotoIconNameCorrected = "Profile Photo";
    private const string UnknownIconName = "Unknown";

    [SerializeField] private TextMeshProUGUI textLabel;
    [SerializeField] private Image folderIcon;
    [SerializeField] private Image profileIcon;
    [SerializeField] private Image unknownIcon;
    [SerializeField] private ClickablePCElement clickableElement;

    private Sprite _profileSprite;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Configure(PCListItemModel model)
    {
        ResolveReferences();
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

    private void ResolveReferences()
    {
        if (textLabel == null)
            textLabel = FindDescendantByName(transform, TextObjectName)?.GetComponent<TextMeshProUGUI>();

        Transform iconWrapper = FindDescendantByName(transform, IconWrapperObjectName);
        Transform searchRoot = iconWrapper != null ? iconWrapper : transform;

        if (folderIcon == null)
            folderIcon = FindDescendantByName(searchRoot, FolderIconName)?.GetComponent<Image>();

        if (profileIcon == null)
        {
            profileIcon = FindDescendantByName(searchRoot, ProfilePhotoIconName)?.GetComponent<Image>();
            if (profileIcon == null)
                profileIcon = FindDescendantByName(searchRoot, ProfilePhotoIconNameCorrected)?.GetComponent<Image>();
        }

        if (unknownIcon == null)
            unknownIcon = FindDescendantByName(searchRoot, UnknownIconName)?.GetComponent<Image>();

        if (clickableElement == null)
            clickableElement = GetComponent<ClickablePCElement>();
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform result = FindDescendantByName(child, targetName);
            if (result != null)
                return result;
        }

        return null;
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