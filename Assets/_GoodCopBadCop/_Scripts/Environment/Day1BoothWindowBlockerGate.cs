using UnityEngine;

/// <summary>
/// Turns the booth window blocker's colliders on only during the Day 1 locked-booth period:
/// <c>ShiftManager.CurrentDay == 1</c> AND the booth door is locked. The door is locked by
/// <see cref="ShiftManager"/> at day start / shift start / shift resume and unlocked at shift end
/// (<c>ShiftManager.OnDoorUnlock</c>), so the blocker drops out the moment the Day 1 shift ends
/// and is never active on Day 2+.
///
/// Both inputs are replicated (day via ShiftManager's network day, lock via DoorController's
/// NetworkVariable), so this is evaluated locally on every client and stays correct for late
/// joiners and restored saves without listening to any events.
/// </summary>
[DisallowMultipleComponent]
public class Day1BoothWindowBlockerGate : MonoBehaviour
{
    [Tooltip("Booth door whose locked state keeps the blocker active.")]
    [SerializeField] private DoorController _boothDoor;

    [Tooltip("Day on which the blocker is used.")]
    [SerializeField] private int _activeDay = 1;

    private Collider[] _colliders;
    private bool _applied;
    private bool _active;

    private void Awake()
    {
        _colliders = GetComponentsInChildren<Collider>(true);
        if (_boothDoor == null)
            Debug.LogWarning("[Day1BoothWindowBlockerGate] No booth door assigned; blocker will stay disabled.", this);
    }

    private void OnEnable()
    {
        _applied = false;
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        bool active = ShouldBeActive();
        if (_applied && active == _active) return;

        _applied = true;
        _active = active;
        foreach (Collider col in _colliders)
            if (col != null) col.enabled = active;
    }

    private bool ShouldBeActive()
    {
        if (_boothDoor == null || !_boothDoor.IsSpawned) return false;
        ShiftManager shift = ShiftManager.Instance;
        return shift != null && shift.CurrentDay == _activeDay && _boothDoor.IsLocked;
    }
}
