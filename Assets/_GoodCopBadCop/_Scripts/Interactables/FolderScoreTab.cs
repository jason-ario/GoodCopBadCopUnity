using UnityEngine;

/// <summary>
/// Drives the folder's "Score Tab" readout: sums the mutation-score points (see
/// <see cref="AnomalyManager"/>/<see cref="AnomalyController.GetAnomalyPointCost"/>) of every
/// checked checklist item across every <see cref="ExamPage"/> currently filed into the parent
/// <see cref="FolderController"/>, and displays the total on <see cref="_scoreText"/>.
///
/// Recomputes whenever any checklist checkbox changes or any notebook page is filed anywhere —
/// both fire identically on every client — and on a short safety timer to catch any other state
/// change (e.g. a page being pulled back out of the folder) without needing new network events.
/// </summary>
public class FolderScoreTab : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshPro _scoreText;

    /// <summary>How often the safety-net recompute runs, in seconds.</summary>
    [SerializeField] private float _refreshInterval = 0.5f;

    private FolderController _folder;

    private void Awake()
    {
        _folder = GetComponentInParent<FolderController>();

        if (_scoreText == null)
            _scoreText = GetComponentInChildren<TMPro.TextMeshPro>();

        if (_folder == null)
            Debug.LogWarning($"[FolderScoreTab] '{name}' could not find a FolderController in its parents — score will not update.", this);
    }

    private void OnEnable()
    {
        ExamNotebook.OnAnyCheckboxChecked += OnAnyCheckboxChecked;
        ExamNotebook.OnAnyNotebookPageFiled += RefreshScore;

        RefreshScore();
        InvokeRepeating(nameof(RefreshScore), _refreshInterval, _refreshInterval);
    }

    private void OnDisable()
    {
        ExamNotebook.OnAnyCheckboxChecked -= OnAnyCheckboxChecked;
        ExamNotebook.OnAnyNotebookPageFiled -= RefreshScore;

        CancelInvoke(nameof(RefreshScore));
    }

    private void OnAnyCheckboxChecked(ExamNotebook _) => RefreshScore();

    /// <summary>
    /// Recomputes and displays the total mutation-score points of every checked checklist item
    /// across every exam page currently filed into this folder.
    /// </summary>
    public void RefreshScore()
    {
        if (_folder == null || _scoreText == null) return;

        int total = 0;

        foreach (ExamPage page in _folder.GetFiledExamPages())
        {
            ChecklistItem[] items = page.ChecklistItems;
            if (items == null) continue;

            foreach (ChecklistItem item in items)
            {
                if (item == null || !item.IsChecked) continue;

                AnomalyCategory? category = AnomalyCategoryExtensions.FromAnomalyTypeName(item.AnomalyTypeName);
                if (category == null) continue;

                total += AnomalyController.GetAnomalyPointCost(category.Value);
            }
        }

        _scoreText.text = total.ToString();
    }
}
