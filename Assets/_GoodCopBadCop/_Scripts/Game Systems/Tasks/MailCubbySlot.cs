using System;
using HighlightPlus;
using TMPro;
using UnityEngine;

/// <summary>
/// Placed on each individual cubby of the "Mail Cubbies" prefab. Unlike the generic
/// <see cref="MailSortBin"/> (Quarantine/Confiscate), each <see cref="MailCubbySlot"/> is bound to
/// one specific resident via <see cref="_assignedResident"/>. A <see cref="MailPackageItem"/> is
/// only counted as correctly delivered if it is dropped into the cubby belonging to its own
/// addressee — dropping a deliverable package into any other resident's cubby bounces it back out,
/// exactly like dropping it into the wrong bin entirely.
///
/// Delivery is detected via <see cref="_placementSlot"/>'s <see cref="PlacementBoard.OnItemPlaced"/>
/// event — fired by <see cref="PlayerPickupController.DropObject"/> the moment the player commits
/// the ghost placement (RMB release or the LMB/E ghost-commit path, see
/// <see cref="PlayerInteractionController.TryPlaceHeldObjectAtGhost"/>) — NOT via a physics trigger
/// overlap. This is intentional: a package placed exactly via the ghost/snap-pose flow does not
/// reliably re-trigger a fresh <c>OnTriggerEnter</c> (its colliders were disabled while held and
/// the transform is simply set to the snap pose directly), which silently swallowed otherwise
/// correct deliveries — the "dropped it in the right slot and it just didn't register" bug.
/// Physically throwing/tossing a package so it collides into the cubby's opening no longer counts
/// as a delivery at all; the player must use the interact/ghost placement flow.
///
/// Setup (per cubby instance on the "Mail Cubbies" prefab):
///   - Assign <see cref="_assignedResident"/> to the <see cref="SuspectData"/> this physical cubby
///     is labelled for, or let <see cref="MailCubbyManager"/> auto-assign it at random.
///   - Assign <see cref="_placementSlot"/> to the <see cref="PlacementSlot"/> covering the cubby's
///     opening (its <see cref="PlacementBoard.OnItemPlaced"/> event drives delivery detection). If
///     left unassigned, this component falls back to <see cref="GetComponent{T}"/>.
///   - Assign <see cref="_label"/> to the TMP text on the cubby's tape (e.g. "Tape/Label"). If left
///     unassigned, this component falls back to the first <see cref="TMP_Text"/> found in children.
/// </summary>
public class MailCubbySlot : MonoBehaviour
{
    [Tooltip("The resident this physical cubby is labelled for. Packages are only counted as delivered here if addressed to this resident.")]
    [SerializeField] private SuspectData _assignedResident;

    [Tooltip("Collider covering the cubby's opening, used only as the raycast target so PlayerInteractionController can find this cubby's PlacementSlot while aiming. No longer drives delivery detection. Falls back to GetComponent<Collider>() if unassigned.")]
    [SerializeField] private Collider _triggerZone;

    [Tooltip("Fixed placement pose a correctly delivered package should snap to. Falls back to GetComponent<PlacementSlot>() if unassigned.")]
    [SerializeField] private PlacementSlot _placementSlot;

    [Tooltip("TMP text displaying the assigned resident's name on the cubby's tape label. Falls back to the first TMP_Text found in children if unassigned.")]
    [SerializeField] private TMP_Text _label;

    [Tooltip("Optional per-cubby outline highlight. Not used by MailCubbyManager (which highlights the whole \"Mail Cubbies\" stand root instead — see MailCubbyManager.HighlightAllActiveCubbies) — kept here only for callers that want to call out one specific cubby. Falls back to GetComponent<HighlightEffect>() if unassigned. SetHighlight() is a no-op if this is left unassigned and no HighlightEffect is found.")]
    [SerializeField] private HighlightEffect _highlightEffect;

    [Tooltip("Optional locker door guarding this cubby's opening (see \"Door Hinge/Locker Door\"). While assigned and closed, packages are rejected even if something manages to clip into the trigger zone — the door's own collider is what normally blocks physical entry, this is just a defensive backstop. Falls back to GetComponentInChildren<LockerDoorInteractable>() if unassigned.")]
    [SerializeField] private LockerDoorInteractable _lockerDoor;

    /// <summary>The resident this physical cubby is currently labelled for.</summary>
    public SuspectData AssignedResident => _assignedResident;

    /// <summary>The full name of the resident assigned to this cubby, matching the format used by <see cref="MailPackageItem.ResidentName"/>. Display/log purposes only — sort-matching uses <see cref="ResidentId"/> instead, since display names are fragile to compare (whitespace, casing, typos between a resident's display name and this cubby's label).</summary>
    public string ResidentName => _assignedResident != null
        ? $"{_assignedResident.FirstName} {_assignedResident.LastName}".Trim()
        : string.Empty;

    /// <summary>
    /// <see cref="AssignedResident"/>'s index within <see cref="MailCubbyManager"/>'s shared
    /// resident pool, or -1 if unassigned/not found. Sort-matching
    /// (<see cref="SortMailTask.EvaluateSort"/>) sends this index across the RPC boundary instead
    /// of a display-name string, then resolves it back to the actual <see cref="SuspectData"/>
    /// reference server-side via <see cref="MailCubbyManager.ResolveResident"/> and compares that
    /// reference directly against <see cref="MailPackageItem.AssignedResident"/> — this avoids
    /// ever comparing two independently-built name strings, which was fragile (whitespace/casing/
    /// typo mismatches between a resident's display name and this cubby's label could silently
    /// break sorting even when the actual resident assignment was correct).
    /// </summary>
    public int ResidentPoolIndex => MailCubbyManager.Instance != null
        ? MailCubbyManager.Instance.GetResidentIndex(_assignedResident)
        : -1;

    /// <summary>
    /// The resident's name abbreviated for the tape label, e.g. "Sandro P." instead of the full
    /// "Sandro Petrov" — keeps <see cref="ResidentName"/> (used for delivery matching) untouched.
    /// Falls back to the full first name if there is no last name to abbreviate.
    /// </summary>
    public string AbbreviatedResidentName
    {
        get
        {
            if (_assignedResident == null) return string.Empty;

            string firstName = _assignedResident.FirstName?.Trim() ?? string.Empty;
            string lastName = _assignedResident.LastName?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(lastName)) return firstName;
            if (string.IsNullOrEmpty(firstName)) return $"{lastName[0]}.";

            return $"{firstName} {lastName[0]}.";
        }
    }

    private void Awake()
    {
        // _triggerZone is retained only as the raycast target PlayerInteractionController uses to
        // find this cubby's PlacementBoard/PlacementSlot while aiming (see
        // PlayerInteractionController.CheckActivatePlacer) — it no longer drives delivery
        // detection itself (see the class doc comment).
        if (_triggerZone == null)
            _triggerZone = GetComponent<Collider>();

        if (_triggerZone == null)
            Debug.LogWarning($"[MailCubbySlot] '{name}' has no collider assigned or attached for aim detection.", this);

        if (_placementSlot == null)
            _placementSlot = GetComponent<PlacementSlot>();

        if (_placementSlot == null)
            Debug.LogWarning($"[MailCubbySlot] '{name}' has no PlacementSlot assigned or attached — this cubby will never accept a delivery.", this);
        else
            _placementSlot.OnItemPlaced += HandleItemPlaced;

        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);

        if (_highlightEffect == null)
            _highlightEffect = GetComponent<HighlightEffect>();

        if (_lockerDoor == null)
            _lockerDoor = GetComponentInChildren<LockerDoorInteractable>(true);

        if (_assignedResident == null)
            Debug.LogWarning($"[MailCubbySlot] '{name}' has no assigned resident — this cubby will never accept a delivery.", this);

        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (_placementSlot != null)
            _placementSlot.OnItemPlaced -= HandleItemPlaced;
    }

    /// <summary>
    /// Assigns the resident this cubby is labelled for and immediately refreshes the tape label
    /// text to match. Used by <see cref="MailCubbyManager"/> to randomize cubby assignments.
    /// </summary>
    public void SetAssignedResident(SuspectData resident)
    {
        _assignedResident = resident;
        RefreshLabel();
    }

    /// <summary>Updates the tape label text to show the current <see cref="AbbreviatedResidentName"/>.</summary>
    private void RefreshLabel()
    {
        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);

        if (_label != null)
            _label.text = AbbreviatedResidentName;
    }

    /// <summary>
    /// Turns this cubby's own outline highlight on or off, if it has one assigned. Not called by
    /// <see cref="MailCubbyManager"/> — see <see cref="MailCubbyManager.HighlightAllActiveCubbies"/>,
    /// which highlights the whole "Mail Cubbies" stand root instead. No-op if this cubby has no
    /// <see cref="HighlightEffect"/> assigned or found.
    /// </summary>
    public void SetHighlight(bool highlight)
    {
        if (_highlightEffect == null) return;

        _highlightEffect.enabled = true;
        _highlightEffect.highlighted = highlight;
    }

    /// <summary>
    /// Fired by <see cref="_placementSlot"/> (a <see cref="PlacementBoard"/>) the moment the
    /// player commits a package into this cubby via the interact/ghost placement flow — see
    /// <see cref="PlayerPickupController.DropObject"/>, which calls
    /// <see cref="PlacementBoard.OnPlaced"/> before the drop position/network RPCs go out. Runs
    /// locally on whichever client performed the placement; <see cref="MailPackageItem.RequestSortServerRpc"/>
    /// routes the actual validation to the server. Physically throwing/tossing a package into this
    /// cubby's opening no longer triggers a delivery — see the class doc comment.
    /// </summary>
    private void HandleItemPlaced(PickableObject placedObject)
    {
        if (placedObject is not MailPackageItem package) return;
        if (package.IsResolved) return;
        if (_lockerDoor != null && !_lockerDoor.IsOpen) return; // door's own collider normally blocks entry — this is just a backstop

        // Optimistically lock the package out of being picked back up the instant it lands in
        // its addressee's cubby, instead of waiting for the RequestSortServerRpc round trip to
        // confirm and call MailPackageItem.MarkDelivered -> LockInteractableNetworked. Even for
        // a host, ServerRpcs are queued through Netcode's messaging pipeline rather than executed
        // synchronously in this same call, which left a real (if brief) window where a
        // correctly-delivered package's colliders were still enabled. Interactable.Interact only
        // checks whether colliders are currently enabled (see PickableObject.IsInteractable), so
        // a player quickly delivering several packages in a row could click back into that
        // window and grab the just-delivered package straight back out. Since
        // MailPackageItem.IsResolved is already true server-side by the time that round trip
        // lands, EvaluateSort silently ignores every later placement attempt for that same
        // package — it can never be delivered again. That is the "exactly one box quietly stops
        // working" bug: it was delivered correctly, then picked back out by accident before the
        // lock could land.
        //
        // IMPORTANT: this must call PickableObject.LockInteractable() rather than the plain
        // SetInteractable(false) — DropObject() only just now called ReleaseHolderServerRpc to
        // clear _holdingClientId, and when that NetworkVariable's change is delivered (moments
        // after this handler returns, even on the host, since NetworkVariable writes are also
        // flushed through Netcode's tick rather than applied inline) PickableObject.OnHoldingClientChanged
        // runs and unconditionally calls SetInteractable(true) again (holder-based logic, since no
        // network override is active yet) — silently undoing a plain SetInteractable(false) and
        // reopening the exact same grab window a moment later. LockInteractable() sets the
        // _interactableLocked guard flag that OnHoldingClientChanged explicitly checks and bails
        // out on, so the held->released transition can no longer re-enable colliders out from
        // under us. The eventual server confirmation
        // (MarkDelivered -> LockInteractableNetworked -> _networkInteractableOverride) is still
        // authoritative and applies the same lock networked, for every other client.
        bool isCorrectResident = _assignedResident != null && _assignedResident == package.AssignedResident;
        if (isCorrectResident)
            package.LockInteractable();

        if (_placementSlot != null)
        {
            Transform snap = _placementSlot.SnapPoint;
            package.RequestSortServerRpc((int)MailSortBinType.Delivery, ResidentPoolIndex, true, snap.position, snap.rotation);
        }
        else
        {
            package.RequestSortServerRpc((int)MailSortBinType.Delivery, ResidentPoolIndex);
        }
    }
}
