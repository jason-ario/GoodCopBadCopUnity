using System;
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

    [Header("Search")]
    [SerializeField] private PCSearchField searchField;
    [Tooltip("Scroll view that is pushed down to make room for the search bar while it is visible.")]
    [SerializeField] private RectTransform scrollViewRect;
    [Tooltip("How far (canvas units) the scroll view's top edge moves down while the search bar is visible.")]
    [SerializeField] private float searchBarInset = 110f;

    private readonly List<PCListItemView> _spawnedItems = new();

    private bool _searchHooked;
    private bool _scrollDefaultsCached;
    private Vector2 _scrollDefaultPosition;
    private Vector2 _scrollDefaultSize;

    /// <summary>Raised whenever the local player edits the search query.</summary>
    public event Action<string> SearchQueryChanged;

    public bool IsSearchFocused => searchField != null && searchField.IsFocused;

    private void Awake()
    {
        HideTemplate();
        ResolveSearchReferences();
        HookSearchField();
        CacheScrollDefaults();
    }

    private void ResolveSearchReferences()
    {
        if (searchField == null)
            searchField = GetComponentInChildren<PCSearchField>(true);

        if (scrollViewRect == null)
        {
            ScrollRect scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (scrollRect != null)
                scrollViewRect = scrollRect.transform as RectTransform;
        }
    }

    private void OnDestroy()
    {
        if (_searchHooked && searchField != null)
            searchField.QueryChanged -= HandleSearchQueryChanged;
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

    /// <summary>
    /// Shows or hides the search bar, applies the replicated query, and sets whether the local
    /// player (only the one operating the terminal) can type into it.
    /// </summary>
    public void ConfigureSearch(bool visible, string query, bool editable)
    {
        ResolveSearchReferences();
        HookSearchField();
        CacheScrollDefaults();

        if (searchField != null)
        {
            searchField.gameObject.SetActive(visible);
            searchField.SetState(query, visible && editable);
        }

        if (scrollViewRect != null && _scrollDefaultsCached)
        {
            float inset = visible && searchField != null ? searchBarInset : 0f;
            scrollViewRect.anchoredPosition = _scrollDefaultPosition - new Vector2(0f, inset);
            scrollViewRect.sizeDelta = _scrollDefaultSize - new Vector2(0f, inset);
        }
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

    private void HookSearchField()
    {
        if (_searchHooked || searchField == null)
            return;

        searchField.QueryChanged += HandleSearchQueryChanged;
        _searchHooked = true;
    }

    private void HandleSearchQueryChanged(string query) => SearchQueryChanged?.Invoke(query);

    private void CacheScrollDefaults()
    {
        if (_scrollDefaultsCached || scrollViewRect == null)
            return;

        _scrollDefaultPosition = scrollViewRect.anchoredPosition;
        _scrollDefaultSize = scrollViewRect.sizeDelta;
        _scrollDefaultsCached = true;
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
