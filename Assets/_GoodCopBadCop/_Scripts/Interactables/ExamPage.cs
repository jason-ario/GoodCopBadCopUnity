using System;
using UnityEngine;

public class ExamPage : FolderItem
{
    [SerializeField] private ChecklistItem[] _checklistItems; 
    [SerializeField] ExamNotebook notebook;
    public Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool isRippedOut;

    public ChecklistItem[] ChecklistItems => _checklistItems;
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
