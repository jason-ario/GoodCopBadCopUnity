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
    bool _isInteractable = false;
    private bool _isChecked;
    
    private void OnEnable()
    {
        spriteRenderer.color = Color.clear;
    }

    private void Check()
    {
        _isChecked = true;
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
        _isChecked = false;
        checkmark.SetActive(false);
    }

    public void OnClick()
    {
        if (_isInteractable == false)
        {
            return;
        }

        if (_isChecked)
        {
            Uncheck();
        }
        else
        {
            Check();
        }
    }

    public void SetInteractable(bool value)
    {
        _isInteractable = value;
    }
}
