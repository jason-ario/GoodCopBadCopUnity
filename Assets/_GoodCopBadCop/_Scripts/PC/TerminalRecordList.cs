using System.Collections.Generic;
using UnityEngine;

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

public class TerminalRecordListUI : MonoBehaviour
{
    [SerializeField] private Transform listContainer;
    [SerializeField] private TerminalListItem listItemPrefab; 
    [SerializeField] private PC pc;

    private readonly List<TerminalListItem> _spawnedItems = new();

    public void ShowRecords(List<SuspectData> suspectDatas, string summary = "")
    {
        Clear();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            TerminalListItem summaryItem = Instantiate(listItemPrefab, listContainer);
            summaryItem.SetupSummary(summary);
            _spawnedItems.Add(summaryItem);
        }

        if (suspectDatas == null)
            return;

        for (int i = 0; i < suspectDatas.Count; i++)
        {
            TerminalListItem item = Instantiate(listItemPrefab, listContainer);
            item.Setup(suspectDatas[i], pc, pc.GetTerminalStatus(suspectDatas[i]));
            _spawnedItems.Add(item);
        }
    }

    public void ShowNews(List<TerminalNewsEntry> newsEntries, string summary = "")
    {
        Clear();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            TerminalListItem summaryItem = Instantiate(listItemPrefab, listContainer);
            summaryItem.SetupSummary(summary);
            _spawnedItems.Add(summaryItem);
        }

        if (newsEntries == null)
            return;

        for (int i = 0; i < newsEntries.Count; i++)
        {
            TerminalListItem item = Instantiate(listItemPrefab, listContainer);
            item.SetupNews(newsEntries[i], pc);
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
