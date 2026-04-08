using System;
using System.Collections;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] Checkbox[] checkboxes;
    [SerializeField] private SpriteRenderer sr;
    public bool IsChecking => examPage.IsChecking;


    private void Awake()
    {
        sr.enabled = false;

        foreach (var checkbox in checkboxes)
        {
            checkbox.Uncheck();
        }
    }

    public void UncheckOther(Checkbox checkbox)
    {
        foreach (var item in this.checkboxes)
        {
            if (item != checkbox)
            {
                item.Uncheck();
            }
        }
    }

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        examPage.AnimateCheckMark(ikAnimationTarget);
    }
}
