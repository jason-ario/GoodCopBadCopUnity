using System;
using UnityEngine;

public class ChecklistItem : MonoBehaviour
{
    [SerializeField] private ExamPage examPage;
    [SerializeField] private Checkbox checkbox;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private GameObject container;

    /// <summary>
    /// Optional overlay shown when this item's anomaly has not been unlocked yet.
    /// Assign a child GameObject (e.g. a dark-tinted SpriteRenderer or a "???" label)
    /// in the prefab Inspector. Hidden when the anomaly is unlocked.
    /// </summary>
    [SerializeField] private GameObject lockedVisual;

    /// <summary>
    /// The TextMeshPro label showing this item's human-readable name (e.g. "Expiration Date").
    /// Assigned in the "Checklist Item" base prefab so every instance inherits it automatically.
    /// Updated at runtime by <see cref="SetAnomalyTypeName"/> when a page auto-populates its
    /// checklist from a category in AnomalyUnlockProgressionSO.
    /// </summary>
    [SerializeField] private TMPro.TextMeshPro label;

    // Assigned automatically by ExamPage.InitializeChecklistIndices() at OnNetworkSpawn
    // so it always matches the item's position in the _checklistItems array.
    private int index;
    private bool _isLocked;

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

    /// <summary>True when this item's anomaly has not been unlocked yet and cannot be checked.</summary>
    public bool IsLocked => _isLocked;

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
        // Locked items can never become interactable, even when the exam is active.
        checkbox.SetInteractable(_isLocked ? false : value);
    }

    /// <summary>Called by ExamPage.OnNetworkSpawn to set the array position of this item.</summary>
    public void SetIndex(int i) => index = i;

    /// <summary>
    /// Wires this item back to the page that owns it. Required when the item is instantiated
    /// dynamically at runtime (see <see cref="ExamPage.BuildChecklistFromCategory"/>), since the
    /// base "Checklist Item" prefab has no page to reference until it's spawned into one.
    /// </summary>
    public void SetExamPage(ExamPage page) => examPage = page;

    /// <summary>
    /// Overrides this item's anomaly type name at runtime and refreshes its label to match,
    /// using a humanized version of the type name (e.g. "IDNumberWrongAnomaly" → "ID Number Wrong").
    /// Used by <see cref="ExamPage.BuildChecklistFromCategory"/> to auto-populate checklist
    /// items from AnomalyUnlockManager's progression asset instead of relying on hand-authored
    /// per-item values in the prefab.
    /// </summary>
    public void SetAnomalyTypeName(string typeName)
    {
        anomalyTypeName = typeName;

        if (label != null)
            label.text = HumanizeAnomalyTypeName(typeName);
    }

    /// <summary>
    /// Converts an anomaly C# type name into a readable checklist label, e.g.
    /// "ExpirationDateAnomaly" → "Expiration Date", "IDNumberWrongAnomaly" → "ID Number Wrong".
    /// </summary>
    private static string HumanizeAnomalyTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return string.Empty;

        const string suffix = "Anomaly";
        string trimmed = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;

        if (trimmed.Length == 0) return typeName;

        // Insert a space before each capital letter that starts a new word, while keeping
        // consecutive capitals (acronyms like "ID") together as a single word.
        var sb = new System.Text.StringBuilder(trimmed.Length + 8);
        sb.Append(trimmed[0]);
        for (int i = 1; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool prevIsLower = char.IsLower(trimmed[i - 1]);
            bool startsAcronymWord = char.IsUpper(c) && i + 1 < trimmed.Length && char.IsLower(trimmed[i + 1])
                                      && char.IsUpper(trimmed[i - 1]);
            if (char.IsUpper(c) && (prevIsLower || startsAcronymWord))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Routes a checkbox click through the network via ExamPage.</summary>
    public void OnCheckboxClicked(bool currentValue)
    {
        examPage.SetCheckboxChecked(index, !currentValue);
        AnyBoxChecked = true;
        OnAnyBoxChecked?.Invoke();
    }

    /// <summary>
    /// Applies the authoritative checked state to the checkbox visual. Called by ExamPage.ApplyBitmask,
    /// which re-applies EVERY item's state on the page whenever ANY single item's bitmask changes (since
    /// the whole page shares one NetworkVariable). Guarded to only touch the checkbox when its state is
    /// actually changing — otherwise every checkbox click would re-fire CheckVisual() on every other
    /// already-checked item on the page too, replaying its draw animation, draw sound, and arm-IK
    /// trigger, and restarting its WaitAndShowCheckmark coroutine, for no reason.
    /// </summary>
    public void ApplyCheckedState(bool value)
    {
        if (checkbox.IsChecked == value) return;

        if (value)
            checkbox.CheckVisual();
        else
            checkbox.Uncheck();
    }

    /// <summary>True while this item's checkbox can currently be toggled.</summary>
    public bool IsInteractable => checkbox.IsInteractable;

    /// <summary>Shows or hides this item's controller-navigation "Selected Box" highlight.</summary>
    public void SetControllerSelected(bool value) => checkbox.SetSelected(value);

    /// <summary>Toggles this item's checkbox exactly as a mouse click would, for controller (gamepad) input.</summary>
    public void ActivateViaController() => checkbox.OnClick();

    /// <summary>
    /// Applies the locked/unlocked state to this checklist item.
    /// When locked: hides the container so the row is invisible, and blocks checkbox interaction.
    /// When unlocked: shows the container and restores normal interaction rules.
    /// After calling this, invoke <see cref="SetInteractable"/> to apply the exam's
    /// current interactable state with the new lock guard in effect.
    /// </summary>
    public void ApplyLockState(bool locked)
    {
        _isLocked = locked;

        if (container != null)
            container.SetActive(!locked);
    }
}
