using System;
using System.Collections;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] Checkbox checkbox;
    [SerializeField] private SpriteRenderer sr;
    public bool IsChecking => examPage.IsChecking;


    private void Awake()
    {
        sr.enabled = false;
        checkbox.Uncheck();
    }

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        examPage.AnimateCheckMark(ikAnimationTarget);
    }

    public void SetInteractable(bool value)
    {
        checkbox.SetInteractable(value);
    }
}
