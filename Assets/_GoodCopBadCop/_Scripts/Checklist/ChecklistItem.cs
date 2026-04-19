using System;
using System.Collections;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] Checkbox checkbox;
    [SerializeField] private SpriteRenderer sr;
    public bool IsChecking => examPage.IsChecking;
    [SerializeField] private UnityEngine.Object anomalyTypeReference; 
    public UnityEngine.Object AnomalyTypeReference => anomalyTypeReference;
    [SerializeField] private string anomalyTypeName;
    public string AnomalyTypeName => anomalyTypeName;
    public bool IsChecked => checkbox.IsChecked;

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
