using UnityEngine;

public class Checkbox : MonoBehaviour
{
    [SerializeField] GameObject checkmark; 
    [SerializeField] ChecklistItem checklistItem;

    public void Check()
    {
        checklistItem.UncheckOther(this);
        checkmark.SetActive(true);
    }

    public void Uncheck()
    {
        checkmark.SetActive(false);
    }
}
