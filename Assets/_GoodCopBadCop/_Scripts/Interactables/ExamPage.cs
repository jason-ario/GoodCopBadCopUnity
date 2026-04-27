using System;
using UnityEngine;

public class ExamPage : FolderItem
{
    [SerializeField] private ChecklistItem[] _checklistItems; 
    private ExamNotebook notebook;
    public Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool isRippedOut;
    
    public ChecklistItem[] ChecklistItems => _checklistItems;

    public void Initialize(ExamNotebook notebook)
    {
        this.notebook = notebook;
    }
    
    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        notebook.AnimateCheckMark(ikAnimationTarget);
    }

    public void SetChecklistInteractable(bool b)
    {
        foreach (ChecklistItem item in _checklistItems)
        {
            item.SetInteractable(b);
        }
    }
    
}
