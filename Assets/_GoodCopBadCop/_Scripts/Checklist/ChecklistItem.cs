using System;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] Checkbox[] checkbox;
    [SerializeField] private SpriteRenderer[] _spriteRenderers;

    private void Awake()
    {
        foreach (var sr in _spriteRenderers)
        {
            sr.enabled = false;
        }
    }

    public void UncheckOther(Checkbox checkbox)
    {
        foreach (var item in this.checkbox)
        {
            if (item != checkbox)
            {
                item.Uncheck();
            }
        }
    }
}
