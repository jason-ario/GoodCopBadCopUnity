using System;
using UnityEngine;

public class Checkbox : MonoBehaviour, IClickable
{
    [SerializeField] GameObject checkmark; 
    [SerializeField] ChecklistItem checklistItem; 
    [SerializeField] SpriteRenderer spriteRenderer;

    private void OnEnable()
    {
        spriteRenderer.color = Color.clear;
    }

    private void Check()
    {
        checklistItem.UncheckOther(this);
        checkmark.SetActive(true);
    }

    public void Uncheck()
    {
        checkmark.SetActive(false);
    }

    public void OnClick()
    {
        Check();
    }
}
