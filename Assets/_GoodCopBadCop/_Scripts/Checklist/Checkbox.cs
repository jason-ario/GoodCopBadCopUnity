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
    public bool IsChecked { get; private set; }

    private void OnEnable()
    {
        spriteRenderer.color = Color.clear;
    }

    private void Check()
    {
        IsChecked = true;
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
        IsChecked = false;
        checkmark.SetActive(false);
    }

    public void OnClick()
    {
        if (_isInteractable == false)
        {
            return;
        }

        if (IsChecked)
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
