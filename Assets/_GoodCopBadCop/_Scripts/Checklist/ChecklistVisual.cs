using UnityEngine;

/// <summary>
/// Drives the camera-visible representation of a single checklist item rendered onto the
/// page's RenderTexture overlay. Attach to each Checklist Item inside Exam Notebook Contents.
/// State is pushed by ExamPage.ApplyBitmask.
/// </summary>
public class ChecklistVisual : MonoBehaviour
{
    [SerializeField] private GameObject _checkmark;

    /// <summary>Shows or hides the checkmark captured by the checklist camera.</summary>
    public void SetChecked(bool value)
    {
        if (_checkmark != null)
            _checkmark.SetActive(value);
    }

    /// <summary>Shows or hides the entire visual item when its anomaly category is locked.</summary>
    public void SetVisible(bool value) => gameObject.SetActive(value);
}
