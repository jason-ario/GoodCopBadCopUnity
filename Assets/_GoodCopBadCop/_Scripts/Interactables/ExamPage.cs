using System;
using UnityEngine;

public class ExamPage : MonoBehaviour
{
    [SerializeField] private ChecklistItem[] _checklistItems; 
    [SerializeField] ExamNotebook notebook;
    public Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool IsRippedOut;

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        notebook.AnimateCheckMark(ikAnimationTarget);
    }

    public void SetInteractable(bool b)
    {
        foreach (ChecklistItem item in _checklistItems)
        {
            item.SetInteractable(b);
        }
    }
}
