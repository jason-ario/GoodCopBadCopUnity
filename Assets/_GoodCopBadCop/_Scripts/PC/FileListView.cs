using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class FileListView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Transform contentRoot;
    [SerializeField] private PCListItemView itemTemplate;
    [SerializeField] private Scrollbar verticalScrollbar;

    private readonly List<PCListItemView> _spawnedItems = new();

    private void Awake()
    {
        HideTemplate();
    }

    public void Show(string listLabel, IReadOnlyList<PCListItemModel> items)
    {
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
}
