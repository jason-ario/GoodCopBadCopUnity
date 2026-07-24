using UnityEngine;

/// <summary>
/// Placed on each individual cubby of the "Mail Cubbies" prefab. Unlike the generic
/// <see cref="MailSortBin"/> (Quarantine/Confiscate), each <see cref="MailCubbySlot"/> is bound to
/// one specific resident via <see cref="_assignedResident"/>. A <see cref="MailPackageItem"/> is
/// only counted as correctly delivered if it is dropped into the cubby belonging to its own
/// addressee — dropping a deliverable package into any other resident's cubby bounces it back out,
/// exactly like dropping it into the wrong bin entirely.
///
/// Setup (per cubby instance on the "Mail Cubbies" prefab):
///   - Assign <see cref="_assignedResident"/> to the <see cref="SuspectData"/> this physical cubby
///     is labelled for.
///   - Assign <see cref="_triggerZone"/> to a Collider (isTrigger = true) covering the cubby
///     opening. If left unassigned, this component falls back to the first Collider found on this
///     GameObject.
/// </summary>
public class MailCubbySlot : MonoBehaviour
{
    [Tooltip("The resident this physical cubby is labelled for. Packages are only counted as delivered here if addressed to this resident.")]
    [SerializeField] private SuspectData _assignedResident;

    [Tooltip("Trigger collider covering the cubby's opening. Falls back to GetComponent<Collider>() if unassigned.")]
    [SerializeField] private Collider _triggerZone;

    /// <summary>The full name of the resident assigned to this cubby, matching the format used by <see cref="MailPackageItem.ResidentName"/>.</summary>
    public string ResidentName => _assignedResident != null
        ? $"{_assignedResident.FirstName} {_assignedResident.LastName}".Trim()
        : string.Empty;

    private void Awake()
    {
        if (_triggerZone == null)
            _triggerZone = GetComponent<Collider>();

        if (_triggerZone == null)
            Debug.LogWarning($"[MailCubbySlot] '{name}' has no trigger collider assigned or attached.", this);
        else if (!_triggerZone.isTrigger)
            Debug.LogWarning($"[MailCubbySlot] '{name}' trigger collider is not marked isTrigger — packages will not be detected.", this);

        if (_assignedResident == null)
            Debug.LogWarning($"[MailCubbySlot] '{name}' has no assigned resident — this cubby will never accept a delivery.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        MailPackageItem package = other.GetComponentInParent<MailPackageItem>();
        if (package == null) return;
        if (package.IsHeld) return; // ignore momentary overlaps while a player carries a package past the cubby
        if (package.IsResolved) return;

        package.RequestSortServerRpc((int)MailSortBinType.Delivery, ResidentName);
    }
}
