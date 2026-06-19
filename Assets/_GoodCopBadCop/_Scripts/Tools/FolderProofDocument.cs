using UnityEngine;

/// <summary>
/// Attach this component to any PickableObject prefab to make it count as evidence.
/// When placed in a folder and the matching category checkbox was correctly ticked,
/// the player receives a coupon bonus per evidence item at verdict time.
///
/// Concrete subclasses (e.g. PolaroidDocument, VitalReadoutDocument) can override
/// OnPlacedInFolder to run setup logic when the item is filed, and override
/// DefaultCategory to pre-set the Inspector field when the component is first added.
/// </summary>
public class FolderProofDocument : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Which of the five anomaly categories this evidence supports.")]
    protected AnomalyCategory _category;

    /// <summary>The anomaly category this evidence item belongs to.</summary>
    public AnomalyCategory Category => _category;

    /// <summary>
    /// The C# type-name string for the category, matching the names used by
    /// AnomalyController.HasActiveAnomalyOfCategory and ChecklistItem.AnomalyTypeName.
    /// </summary>
    public string CategoryTypeName => _category.ToTypeName();

    /// <summary>
    /// Called on the local client the moment this document is successfully placed in a folder.
    /// Override in subclasses to trigger any setup or visual confirmation.
    /// </summary>
    public virtual void OnPlacedInFolder(FolderController folder) { }
}
