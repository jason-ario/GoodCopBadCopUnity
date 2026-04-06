using System;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] Checkbox[] checkboxes;
    [SerializeField] private SpriteRenderer[] _spriteRenderers;

    private void Awake()
    {
        foreach (var sr in _spriteRenderers)
        {
            sr.enabled = false;
        }
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
}
