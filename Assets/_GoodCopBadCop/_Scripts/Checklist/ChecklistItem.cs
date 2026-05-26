using System;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] private Checkbox checkbox;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private GameObject container;

    // Assigned automatically by ExamPage.InitializeChecklistIndices() at OnNetworkSpawn
    // so it always matches the item's position in the _checklistItems array.
    private int index;

    public bool IsChecking => examPage.IsChecking;

    [SerializeField] private UnityEngine.Object anomalyTypeReference;
    public UnityEngine.Object AnomalyTypeReference => anomalyTypeReference;

    /// <summary>
    /// Populated automatically from anomalyTypeReference in the Editor via OnValidate.
    /// Stores the C# class name so it is available at runtime without UnityEditor APIs.
    /// </summary>
    [SerializeField] [HideInInspector] private string anomalyTypeName;

    /// <summary>The anomaly C# class name used to match against anomaly.GetType().Name at scoring time.</summary>
    public string AnomalyTypeName => anomalyTypeName;

    public bool IsChecked => checkbox.IsChecked;

    /// <summary>
    /// Fired locally on the client that clicked the checkbox, the moment any checklist item
    /// is ticked. Use this in tutorial coroutines when you only need to know that the player
    /// interacted with the notebook — regardless of which box or whether all are complete.
    /// </summary>
    public static event Action OnAnyBoxChecked;

    /// <summary>
    /// Set to true on the local client the moment any checkbox is ticked.
    /// Also set by ExamNotebook's NetworkVariable callback on all clients.
    /// Reset this to false at the earliest point a tutorial beat could be entered,
    /// so that early interaction during preceding dialogue is still captured.
    /// </summary>
    public static bool AnyBoxChecked;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (anomalyTypeReference == null)
        {
            anomalyTypeName = string.Empty;
            return;
        }

        // anomalyTypeReference is a MonoScript asset. GetClass() returns the actual C# type,
        // whose Name matches GetType().Name on live anomaly instances — regardless of filename.
        if (anomalyTypeReference is UnityEditor.MonoScript monoScript)
        {
            System.Type scriptClass = monoScript.GetClass();
            if (scriptClass != null)
                anomalyTypeName = scriptClass.Name;
            else
                Debug.LogWarning($"[ChecklistItem] {name}: MonoScript '{monoScript.name}' has no class — anomalyTypeName not updated.", this);
        }
        else
        {
            // Fallback for non-MonoScript references (ScriptableObjects, etc.).
            anomalyTypeName = anomalyTypeReference.GetType().Name;
        }
    }
#endif

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
        AnyBoxChecked = true;
        OnAnyBoxChecked?.Invoke();
    }

    /// <summary>Applies the authoritative checked state to the checkbox visual. Called by ExamPage.ApplyBitmask.</summary>
    public void ApplyCheckedState(bool value)
    {
        if (value)
            checkbox.CheckVisual();
        else
            checkbox.Uncheck();
    }

    /// <summary>
    /// Shows or hides the container GameObject for this checklist item.
    /// Called by ExamPage when the anomaly's lock state is evaluated.
    /// </summary>
    public void ApplyLockState(bool locked)
    {
        if (container != null)
            container.SetActive(!locked);
    }
}
