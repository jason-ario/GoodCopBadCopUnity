using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "OPTIONAL" section of the consolidated HUD sidebar (see <see cref="HUDSidebarController"/>).
/// A minimal, non-animated sibling of <see cref="TutorialObjectiveList"/>: rows are plain
/// <see cref="TutorialObjectiveItem"/> instances (reusing the same checkbox-row prefab), but this
/// section has no slide in/out animation — it simply shows itself while it holds at least one row
/// and hides otherwise.
///
/// Not yet wired to any gameplay system — call <see cref="AddObjective"/> /
/// <see cref="CompleteAndRemoveObjective"/> from wherever optional side-tasks should surface
/// (e.g. a Day script's "Find lost document" objective) the same way callers already use
/// <see cref="TutorialObjectiveList"/> for mandatory current orders.
/// </summary>
public class OptionalTaskSection : MonoBehaviour
{
    private static OptionalTaskSection _instance;

    public static OptionalTaskSection Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<OptionalTaskSection>(FindObjectsInactive.Include);
            return _instance;
        }
        private set => _instance = value;
    }

    [SerializeField] private GameObject sectionRoot;
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskItemPrefab;

    private readonly List<TutorialObjectiveItem> _items = new();

    public int IncompleteCount
    {
        get
        {
            int count = 0;
            foreach (TutorialObjectiveItem item in _items)
            {
                if (item != null && !item.IsComplete)
                    count++;
            }
            return count;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Start()
    {
        for (int i = taskListContainer.childCount - 1; i >= 0; i--)
            Destroy(taskListContainer.GetChild(i).gameObject);
        _items.Clear();
        UpdateVisibility();
    }

    public TutorialObjectiveItem AddObjective(string text)
    {
        if (taskItemPrefab == null || taskListContainer == null)
            return null;

        GameObject go = Instantiate(taskItemPrefab, taskListContainer);
        var item = go.GetComponent<TutorialObjectiveItem>();
        if (item == null)
        {
            Destroy(go);
            return null;
        }

        item.SetText(text);
        _items.Add(item);
        UpdateVisibility();
        HUDSidebarController.Instance?.RefreshBadge();
        return item;
    }

    public void CompleteAndRemoveObjective(TutorialObjectiveItem item)
    {
        if (item == null) return;
        item.MarkComplete();
        _items.Remove(item);
        Destroy(item.gameObject);
        UpdateVisibility();
        HUDSidebarController.Instance?.RefreshBadge();
    }

    private void UpdateVisibility()
    {
        if (sectionRoot != null)
            sectionRoot.SetActive(_items.Count > 0);
    }
}
