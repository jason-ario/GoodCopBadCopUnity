using System;
using UnityEngine;

public class ExamPage : MonoBehaviour
{
    [SerializeField] private ChecklistItem[] _checklistItems; 
    [SerializeField] ExamNotebook notebook;
    public bool IsChecking => notebook.IsChecking;

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        notebook.AnimateCheckMark(ikAnimationTarget);
    }
}
