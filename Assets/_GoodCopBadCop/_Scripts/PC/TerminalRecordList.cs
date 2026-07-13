using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


public class TerminalRecordListUI : MonoBehaviour
{
    private const string ListContainerName = "Content";
    private const string ListItemPrefabPath = "Assets/_GoodCopBadCop/_Prefabs/UI/PC UI Elements/Record Archive List Element.prefab";

    [SerializeField] private Transform listContainer;
    [SerializeField] private TerminalListItem listItemPrefab;
    [SerializeField] private PC pc;

    private List<TerminalListItem> _spawnedItems;

    private void Awake()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ResolveReferences();
    }
#endif

    public void ShowRecords(List<SuspectData> suspectDatas, string summary = "")
    {
        EnsureSpawnedItems();
        ResolveReferences();
        Clear();

        if (!CanSpawnItems())
            return;

        if (!string.IsNullOrWhiteSpace(summary))
        {
            TerminalListItem summaryItem = CreateItem();
            if (summaryItem != null)
            {
                summaryItem.SetupSummary(summary);
                _spawnedItems.Add(summaryItem);
            }
        }

        if (suspectDatas == null)
            return;

        for (int i = 0; i < suspectDatas.Count; i++)
        {
            TerminalListItem item = CreateItem();
            if (item == null)
                continue;

            item.Setup(suspectDatas[i], pc, pc != null ? pc.GetTerminalStatus(suspectDatas[i]) : string.Empty);
            _spawnedItems.Add(item);
        }
    }

    public void ShowNews(List<TerminalNewsEntry> newsEntries, string summary = "")
    {
        EnsureSpawnedItems();
        ResolveReferences();
        Clear();

        if (!CanSpawnItems())
            return;

        if (!string.IsNullOrWhiteSpace(summary))
        {
            TerminalListItem summaryItem = CreateItem();
            if (summaryItem != null)
            {
                summaryItem.SetupSummary(summary);
                _spawnedItems.Add(summaryItem);
            }
        }

        if (newsEntries == null)
            return;

        for (int i = 0; i < newsEntries.Count; i++)
        {
            TerminalListItem item = CreateItem();
            if (item == null)
                continue;

            item.SetupNews(newsEntries[i], pc);
            _spawnedItems.Add(item);
        }
    }

    private TerminalListItem CreateItem()
    {
        if (listItemPrefab == null || listContainer == null)
            return null;

        EnsureSpawnedItems();
        return Instantiate(listItemPrefab, listContainer);
    }

    private void EnsureSpawnedItems()
    {
        _spawnedItems ??= new List<TerminalListItem>();
    }

    private void ResolveReferences()
    {
        if (pc == null)
            pc = GetComponentInParent<PC>(true);

        if (listContainer == null)
            listContainer = FindDescendantByName(transform, ListContainerName);

#if UNITY_EDITOR
        if (listItemPrefab == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ListItemPrefabPath);
            if (prefab != null)
                listItemPrefab = prefab.GetComponent<TerminalListItem>();
        }
#endif
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

    private bool CanSpawnItems()
    {
        if (listContainer != null && listItemPrefab != null)
            return true;

        Debug.LogError($"[TerminalRecordListUI] Missing list setup. Content: {listContainer != null}, Item prefab: {listItemPrefab != null}", this);
        return false;
    }

    private void Clear()
    {
        EnsureSpawnedItems();

        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            TerminalListItem item = _spawnedItems[i];
            if (item != null && item.gameObject != null)
                Destroy(item.gameObject);
        }

        _spawnedItems.Clear();

        if (listContainer == null)
            return;

        List<GameObject> childrenToDestroy = new List<GameObject>();
        for (int i = 0; i < listContainer.childCount; i++)
        {
            Transform child = listContainer.GetChild(i);
            if (child != null && child.gameObject != null)
                childrenToDestroy.Add(child.gameObject);
        }

        for (int i = 0; i < childrenToDestroy.Count; i++)
        {
            if (childrenToDestroy[i] != null)
                Destroy(childrenToDestroy[i]);
        }
    }
}

public sealed class TerminalNewsEntry
{
    public TerminalNewsEntry(int day, string date, NewspaperContentScriptable content)
    {
        Day = day;
        Date = date;
        Content = content;
    }

    public int Day { get; }
    public string Date { get; }
    public NewspaperContentScriptable Content { get; }
}
