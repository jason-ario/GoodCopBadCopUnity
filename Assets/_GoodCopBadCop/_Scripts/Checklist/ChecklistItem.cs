using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] private Checkbox checkbox;
    [SerializeField] private SpriteRenderer sr;

    // Assigned automatically by ExamPage.InitializeChecklistIndices() at OnNetworkSpawn
    // so it always matches the item's position in the _checklistItems array.
    private int index;

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

    /// <summary>Called by ExamPage.OnNetworkSpawn to set the array position of this item.</summary>
    public void SetIndex(int i) => index = i;

    /// <summary>Routes a checkbox click through the network via ExamPage.</summary>
    public void OnCheckboxClicked(bool currentValue)
    {
        examPage.SetCheckboxChecked(index, !currentValue);
    }

    /// <summary>Applies the authoritative checked state to the checkbox visual. Called by ExamPage.ApplyBitmask.</summary>
    public void ApplyCheckedState(bool value)
    {
        if (value)
            checkbox.CheckVisual();
        else
            checkbox.Uncheck();
    }
}
