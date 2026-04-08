using System;
using System.Collections;
using UnityEngine;

public class Checkbox : MonoBehaviour, IClickable
{
    [SerializeField] GameObject checkmark; 
    [SerializeField] ChecklistItem checklistItem; 
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] private Animator ikAnimationTarget;
    [SerializeField] private Transform ikTargetTransform;
    [SerializeField] private AudioClip drawSound;
    private void OnEnable()
    {
        spriteRenderer.color = Color.clear;
    }

    private void Check()
    {
        checklistItem.UncheckOther(this);
        checklistItem.AnimateCheckMark(ikTargetTransform);
        ikAnimationTarget.SetTrigger("Check");

        StartCoroutine(WaitAndCheck());
    }
    
    IEnumerator WaitAndCheck()
    {
        yield return new WaitForSeconds(.15f);
        checkmark.SetActive(true);
        SFXController.Instance.Play(drawSound);
    }

    public void Uncheck()
    {
        checkmark.SetActive(false);
    }

    public void OnClick()
    {
        if (checklistItem.IsChecking)
        {
            return;
        }
        
        Check();
    }
}
