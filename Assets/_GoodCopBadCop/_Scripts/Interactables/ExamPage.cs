using System;
using UnityEngine;

public class ExamPage : MonoBehaviour
{
    [SerializeField] private ChecklistItem[] _checklistItems; 
    [SerializeField] ExamNotebook notebook;
    [SerializeField] Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool IsRippedOut;

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        notebook.AnimateCheckMark(ikAnimationTarget);
    }

    public void RipOutAndAddToFolder()
    {
        
    }
}
