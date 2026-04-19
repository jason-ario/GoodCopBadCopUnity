using System.Collections.Generic;
using UnityEngine;

public class TerminalRecordListUI : MonoBehaviour
{
    [SerializeField] private Transform listContainer;
    [SerializeField] private TerminalListItem listItemPrefab; 
    [SerializeField] private PC pc;

    private readonly List<TerminalListItem> _spawnedItems = new();

    public void ShowRecords(List<SuspectData> suspectDatas)
    {
        Clear(); 
        Debug.Log("Showing records");

        for (int i = 0; i < suspectDatas.Count; i++)
        {
            TerminalListItem item = Instantiate(listItemPrefab, listContainer);
            item.Setup(suspectDatas[i], pc);
            _spawnedItems.Add(item);
        }
    }

    private void Clear()
    {
        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            if (_spawnedItems[i] != null)
                Destroy(_spawnedItems[i].gameObject);
        }

        _spawnedItems.Clear();
    }
}