using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class FileListView : MonoBehaviour
{
    private const string LabelObjectName = "Label";
    private const string ContentObjectName = "Content";
    private const string ListItemObjectName = "List Item";

    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private PCListItemView itemTemplate;
    [SerializeField] private Scrollbar verticalScrollbar;

    private readonly List<PCListItemView> _spawnedItems = new();

    private void Awake()
    {
        ResolveReferences();
        HideTemplate();
    }

    public void Show(string listLabel, IReadOnlyList<PCListItemModel> items)
    {
        ResolveReferences();
        Clear();

        if (label != null)
            label.text = listLabel ?? string.Empty;

        if (contentRoot == null || itemTemplate == null || items == null)
        {
            ResetScroll();
            return;
        }

        HideTemplate();

        for (int i = 0; i < items.Count; i++)
        {
            PCListItemView item = Instantiate(itemTemplate, contentRoot);
            item.gameObject.SetActive(true);
            item.Configure(items[i]);
            _spawnedItems.Add(item);
        }

        ResetScroll();
    }

    public void Clear()
    {
        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            if (_spawnedItems[i] != null)
                Destroy(_spawnedItems[i].gameObject);
        }

        _spawnedItems.Clear();

        if (contentRoot == null || itemTemplate == null)
            return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = contentRoot.GetChild(i);
            if (child == itemTemplate.transform)
                continue;

            Destroy(child.gameObject);
        }
    }

    private void ResetScroll()
    {
        if (verticalScrollbar != null)
            verticalScrollbar.value = 1f;
    }

    private void HideTemplate()
    {
        if (itemTemplate != null)
            itemTemplate.gameObject.SetActive(false);
    }

    private void ResolveReferences()
    {
        if (label == null)
            label = FindDescendantByName(transform, LabelObjectName)?.GetComponent<TextMeshProUGUI>();

        if (contentRoot == null)
            contentRoot = FindDescendantByName(transform, ContentObjectName);

        if (itemTemplate == null)
        {
            Transform templateTransform = contentRoot != null
                ? FindDirectChildByName(contentRoot, ListItemObjectName)
                : FindDescendantByName(transform, ListItemObjectName);

            if (templateTransform != null)
                itemTemplate = templateTransform.GetComponent<PCListItemView>() ?? templateTransform.gameObject.AddComponent<PCListItemView>();
        }

        if (verticalScrollbar == null)
            verticalScrollbar = GetComponentInChildren<Scrollbar>(true);
    }

    private static Transform FindDirectChildByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;
        }

        return null;
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
}