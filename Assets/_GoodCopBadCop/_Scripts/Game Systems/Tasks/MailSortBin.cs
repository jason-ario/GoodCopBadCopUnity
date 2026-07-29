using UnityEngine;

/// <summary>
/// Placed on the generic mail bin ("Mail Bin - Confiscate"). Detects a
/// <see cref="MailPackageItem"/> being physically dropped inside via a trigger collider and
/// forwards the sort attempt to the server.
///
/// There is no generic "Mail Bin - Delivery": deliverable packages must instead be dropped into
/// the addressee's own cubby — see <see cref="MailCubbySlot"/> on the "Mail Cubbies" prefab.
///
/// Setup:
///   - Assign <see cref="_binType"/> to match this bin's label (Confiscate).
///   - Assign <see cref="_triggerZone"/> to a Collider (isTrigger = true) covering the bin's
///     opening. If left unassigned, this component falls back to the first Collider found on
///     this GameObject — make sure that collider is marked as a trigger, or add a dedicated
///     child trigger volume so the bin's solid mesh collider is left untouched.
///   - IMPORTANT: Unity only delivers OnTriggerEnter to components on the SAME GameObject as the
///     trigger Collider — it never bubbles up to a parent. If <see cref="_triggerZone"/> lives on
///     a child GameObject (e.g. a dedicated "Trigger Zone" object) rather than this GameObject
///     itself, add a <see cref="MailSortBinTriggerRelay"/> component to that child so trigger
///     events still reach this <see cref="MailSortBin"/>.
/// </summary>
public class MailSortBin : MonoBehaviour
{
    [Tooltip("Which sorting outcome this bin represents.")]
    [SerializeField] private MailSortBinType _binType;

    [Tooltip("Trigger collider covering the bin's opening. Falls back to GetComponent<Collider>() if unassigned.")]
    [SerializeField] private Collider _triggerZone;

    public MailSortBinType BinType => _binType;

    private void Awake()
    {
        if (_triggerZone == null)
            _triggerZone = GetComponent<Collider>();

        if (_triggerZone == null)
            Debug.LogWarning($"[MailSortBin] '{name}' has no trigger collider assigned or attached.", this);
        else if (!_triggerZone.isTrigger)
            Debug.LogWarning($"[MailSortBin] '{name}' trigger collider is not marked isTrigger — packages will not be detected.", this);
        else if (_triggerZone.gameObject != gameObject && _triggerZone.GetComponent<MailSortBinTriggerRelay>() == null)
            Debug.LogWarning($"[MailSortBin] '{name}' trigger collider lives on a different GameObject ('{_triggerZone.name}') " +
                              "without a MailSortBinTriggerRelay component — OnTriggerEnter will never fire on this MailSortBin. " +
                              "Add a MailSortBinTriggerRelay to the trigger collider's GameObject.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandlePackageTriggerEnter(other);
    }

    /// <summary>
    /// Evaluates whether the given Collider belongs to a droppable <see cref="MailPackageItem"/>
    /// and, if so, forwards the sort attempt to the server. Called directly by
    /// <see cref="OnTriggerEnter"/> when <see cref="_triggerZone"/> is on this GameObject, or by
    /// <see cref="MailSortBinTriggerRelay"/> when the trigger collider lives on a child instead.
    /// </summary>
    public void HandlePackageTriggerEnter(Collider other)
    {
        MailPackageItem package = other.GetComponentInParent<MailPackageItem>();
        if (package == null) return;
        if (package.IsHeld) return; // ignore momentary overlaps while a player carries a package past the bin
        if (package.IsResolved) return;

        package.RequestSortServerRpc((int)_binType, string.Empty);
    }
}

/// <summary>
/// Small forwarding component for a child trigger volume that is not on the same GameObject as
/// its owning <see cref="MailSortBin"/>. Unity never bubbles physics trigger callbacks up to
/// parent GameObjects, so without this relay, a <see cref="MailSortBin"/> whose
/// <c>_triggerZone</c> is assigned to a child Collider (e.g. a dedicated "Trigger Zone" object)
/// would never actually detect packages being dropped in.
///
/// Setup: add this component to the same GameObject as the trigger Collider (the child assigned
/// to <see cref="MailSortBin"/>'s <c>_triggerZone</c> field).
/// </summary>
public class MailSortBinTriggerRelay : MonoBehaviour
{
    private MailSortBin _bin;

    private void Awake()
    {
        _bin = GetComponentInParent<MailSortBin>();
        if (_bin == null)
            Debug.LogWarning($"[MailSortBinTriggerRelay] '{name}' has no MailSortBin in its parent hierarchy.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        _bin?.HandlePackageTriggerEnter(other);
    }
}

