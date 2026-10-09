using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the campaign slot-selection screen.
/// Displays the three save slots and routes the player into the pre-game lobby
/// once a slot is chosen.
///
/// Gamepad: each slot's delete button sits inside the slot, where Unity's automatic navigation can't
/// reliably reach it, so <see cref="ConfigureSlotNavigation"/> wires explicit navigation: along the slot
/// row it moves slot to slot (or delete to delete), and across it moves between a slot and its own
/// delete button. Rewired whenever slots refresh (a delete hides that slot's delete button).
/// </summary>
public class CampaignScreenController : MonoBehaviour
{
    private const string DeleteSaveTitle = "DELETE ASSIGNMENT?";
    private const string DeleteSaveBodyFormat = "This will permanently delete Save {0}. This cannot be undone.";
    private const string DeleteSaveConfirmText = "Delete";
    private const string DeleteSaveCancelText = "Cancel";
    [SerializeField] private CampaignSlot[] slots;
    [SerializeField] private ConfirmationDialogController confirmationOverlay;

    private Coroutine _navigationRoutine;

    private void Awake()
    {
        GamepadMenuNavigator.EnsureOn(gameObject);
    }

    private void OnEnable()
    {
        confirmationOverlay?.Hide();
        RefreshAllSlots();
    }

    private void OnDisable()
    {
        _navigationRoutine = null;
    }

    // ---------------------------------------------------------------------------
    // Slot Management
    // ---------------------------------------------------------------------------

    private void RefreshAllSlots()
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i].Initialise(this, i);

        ScheduleNavigationRebuild();
    }

    /// <summary>
    /// Called by <see cref="CampaignSlot"/> when the player confirms a slot.
    /// Creates a lobby (so another player can join) and proceeds to the pre-game lobby screen.
    /// </summary>
    public void OnSlotChosen(int slotIndex)
    {
        Debug.Log($"[CampaignScreenController] Slot {slotIndex} chosen.");
        MainMenuController.Instance.StartNewGame();
    }

    /// <summary>
    /// Opens the campaign-specific confirmation overlay before destructive save operations.
    /// The overlay stays generic: its text and callbacks are supplied by this caller.
    /// </summary>
    public void RequestDeleteSlot(int slotIndex)
    {
        if (confirmationOverlay == null)
        {
            Debug.LogError("[CampaignScreenController] Confirmation Overlay is not assigned.", this);
            return;
        }

        confirmationOverlay.Show(
            DeleteSaveTitle,
            string.Format(DeleteSaveBodyFormat, slotIndex + 1),
            DeleteSaveConfirmText,
            DeleteSaveCancelText,
            () => DeleteSlot(slotIndex));
    }

    private void DeleteSlot(int slotIndex)
    {
        SaveDataManager.Instance.DeleteSlot(slotIndex);
        RefreshAllSlots();

        // The focused delete button just disappeared: put focus back on the (now empty) slot.
        CampaignSlot slot = slotIndex >= 0 && slotIndex < slots.Length ? slots[slotIndex] : null;
        if (slot != null && slot.MainButton != null && GoodCopBadCop.Input.ActiveInputDeviceTracker.IsGamepad
            && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(slot.MainButton.gameObject);
    }

    // ---------------------------------------------------------------------------
    // Gamepad navigation
    // ---------------------------------------------------------------------------

    private void ScheduleNavigationRebuild()
    {
        if (!isActiveAndEnabled) return;
        if (_navigationRoutine != null) StopCoroutine(_navigationRoutine);
        _navigationRoutine = StartCoroutine(RebuildNavigationNextFrame());
    }

    private IEnumerator RebuildNavigationNextFrame()
    {
        // Wait for layout so slot positions are final.
        yield return null;
        _navigationRoutine = null;
        ConfigureSlotNavigation();
    }

    private static bool IsUsable(Selectable s) => s != null && s.isActiveAndEnabled && s.IsInteractable();

    private void ConfigureSlotNavigation()
    {
        if (slots == null || slots.Length == 0) return;
        Canvas.ForceUpdateCanvases();

        int count = slots.Length;
        Button[] mains = new Button[count];
        Button[] deletes = new Button[count];
        for (int i = 0; i < count; i++)
        {
            if (slots[i] == null) continue;
            mains[i] = slots[i].MainButton;
            deletes[i] = IsUsable(slots[i].DeleteButton) ? slots[i].DeleteButton : null;
        }

        // Reset to automatic so Unity's geometric search gives each slot's neighbours outside the row.
        Navigation automatic = new Navigation { mode = Navigation.Mode.Automatic };
        for (int i = 0; i < count; i++)
        {
            if (mains[i] != null) mains[i].navigation = automatic;
            if (deletes[i] != null) deletes[i].navigation = automatic;
        }

        // Layout axis: horizontal row unless the first two slots are stacked vertically.
        bool horizontal = true;
        int first = -1, second = -1;
        for (int i = 0; i < count && second < 0; i++)
        {
            if (mains[i] == null) continue;
            if (first < 0) first = i; else second = i;
        }
        if (first >= 0 && second >= 0)
        {
            Vector2 d = GamepadMenuNavigator.GetScreenCenter(mains[second]) - GamepadMenuNavigator.GetScreenCenter(mains[first]);
            horizontal = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
        }

        // Slot order along the layout axis.
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        System.Array.Sort(order, (a, b) =>
        {
            if (mains[a] == null || mains[b] == null) return (mains[a] == null).CompareTo(mains[b] == null);
            Vector2 pa = GamepadMenuNavigator.GetScreenCenter(mains[a]);
            Vector2 pb = GamepadMenuNavigator.GetScreenCenter(mains[b]);
            return horizontal ? pa.x.CompareTo(pb.x) : pb.y.CompareTo(pa.y);
        });

        // Unity's automatic neighbours (up, down, left, right), with delete buttons mapped to their slot.
        Selectable[,] auto = new Selectable[count, 4];
        for (int i = 0; i < count; i++)
        {
            if (mains[i] == null) continue;
            auto[i, 0] = Resolve(mains[i].FindSelectableOnUp(), i, mains, deletes);
            auto[i, 1] = Resolve(mains[i].FindSelectableOnDown(), i, mains, deletes);
            auto[i, 2] = Resolve(mains[i].FindSelectableOnLeft(), i, mains, deletes);
            auto[i, 3] = Resolve(mains[i].FindSelectableOnRight(), i, mains, deletes);
        }

        for (int k = 0; k < count; k++)
        {
            int i = order[k];
            if (mains[i] == null) continue;

            int prev = FindNeighbour(order, k, -1, mains);
            int next = FindNeighbour(order, k, +1, mains);

            Selectable up = auto[i, 0], down = auto[i, 1], left = auto[i, 2], right = auto[i, 3];
            if (horizontal)
            {
                left = prev >= 0 ? mains[prev] : left;
                right = next >= 0 ? mains[next] : right;
            }
            else
            {
                up = prev >= 0 ? mains[prev] : up;
                down = next >= 0 ? mains[next] : down;
            }

            Button del = deletes[i];
            if (del != null)
            {
                Vector2 offset = GamepadMenuNavigator.GetScreenCenter(del) - GamepadMenuNavigator.GetScreenCenter(mains[i]);

                // Delete navigation: back toward its slot, away = the slot's own neighbour that way,
                // along the row = the neighbouring slot's delete (or that slot when it has none).
                Navigation dn = new Navigation { mode = Navigation.Mode.Explicit };
                if (horizontal)
                {
                    bool deleteAbove = offset.y > 0f;
                    dn.selectOnUp = deleteAbove ? up : mains[i];
                    dn.selectOnDown = deleteAbove ? mains[i] : down;
                    dn.selectOnLeft = prev >= 0 ? (Selectable)deletes[prev] ?? mains[prev] : left;
                    dn.selectOnRight = next >= 0 ? (Selectable)deletes[next] ?? mains[next] : right;
                    if (deleteAbove) up = del; else down = del;
                }
                else
                {
                    bool deleteRight = offset.x > 0f;
                    dn.selectOnRight = deleteRight ? right : mains[i];
                    dn.selectOnLeft = deleteRight ? mains[i] : left;
                    dn.selectOnUp = prev >= 0 ? (Selectable)deletes[prev] ?? mains[prev] : up;
                    dn.selectOnDown = next >= 0 ? (Selectable)deletes[next] ?? mains[next] : down;
                    if (deleteRight) right = del; else left = del;
                }
                del.navigation = dn;
            }

            mains[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right
            };
        }
    }

    private static int FindNeighbour(int[] order, int k, int step, Button[] mains)
    {
        for (int j = k + step; j >= 0 && j < order.Length; j += step)
            if (mains[order[j]] != null) return order[j];
        return -1;
    }

    /// <summary>Maps an automatic target onto slot navigation: another slot's delete → that slot; own buttons → none.</summary>
    private static Selectable Resolve(Selectable target, int self, Button[] mains, Button[] deletes)
    {
        if (target == null) return null;
        for (int j = 0; j < mains.Length; j++)
        {
            if (target == mains[j] || target == deletes[j])
                return j == self ? null : mains[j];
        }
        return target;
    }
}
