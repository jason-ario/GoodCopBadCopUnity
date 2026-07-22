using UnityEngine;

/// <summary>
/// Placed on each of the three mail bins ("Mail Bin - Delivery", "Mail Bin - Quarantine",
/// "Mail Bin - Confiscate"). Detects a <see cref="MailPackageItem"/> being physically dropped
/// inside via a trigger collider and forwards the sort attempt to the server.
///
/// Setup:
///   - Assign <see cref="_binType"/> to match this bin's label.
///   - Assign <see cref="_triggerZone"/> to a Collider (isTrigger = true) covering the bin's
///     opening. If left unassigned, this component falls back to the first Collider found on
///     this GameObject — make sure that collider is marked as a trigger, or add a dedicated
///     child trigger volume so the bin's solid mesh collider is left untouched.
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
    }

    private void OnTriggerEnter(Collider other)
    {
        MailPackageItem package = other.GetComponentInParent<MailPackageItem>();
        if (package == null) return;
        if (package.IsHeld) return; // ignore momentary overlaps while a player carries a package past the bin
        if (package.IsResolved) return;

        package.RequestSortServerRpc((int)_binType);
    }
}
