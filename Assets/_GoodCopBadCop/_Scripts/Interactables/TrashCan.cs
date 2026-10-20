using System;
using System.Collections.Generic;
using UnityEngine;

public class TrashCan : Interactable
{
    private const string DocumentationCategory = "Documentation";
    private const string DocumentationTutorialIncompleteMessage =
        "Can't throw away yet";
    private static string SupplyBoxNotEmptyMessage => Loc.T("Empty the box first");
    private static string FolderNotEmptyMessage => Loc.T("Empty the folder first");
    private static string SuspectStillAtWindowMessage => Loc.T("Deliver a verdict first");
    private const string ConfirmDiscardMessage = "Are you sure you want to throw this away?";

    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip throwTrashSound;

    // ── Shared discard registry ──────────────────────────────────────────────
    // Other discard targets (e.g. DumpsterInteractable) reuse whatever the scene's trash
    // can(s) accept, so the accept list stays authored in one place.

    private static readonly List<TrashCan> s_instances = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_instances.Clear();

    protected override void Awake()
    {
        base.Awake();
        if (!s_instances.Contains(this))
            s_instances.Add(this);
    }

    public override void OnDestroy()
    {
        s_instances.Remove(this);
        base.OnDestroy();
    }

    /// <summary>
    /// True when any trash can in the loaded scenes would accept <paramref name="item"/>
    /// (its inspector list, or folders by type). Lets other targets mirror the trash can's
    /// accepted items without duplicating the list.
    /// </summary>
    public static bool IsAcceptedByAnyTrashCan(PickableObject item)
    {
        if (item == null) return false;
        for (int i = s_instances.Count - 1; i >= 0; i--)
        {
            TrashCan can = s_instances[i];
            if (can == null) { s_instances.RemoveAt(i); continue; }
            if (can.CanInteractWithItem(item)) return true;
        }
        return false;
    }

    // ── Interactable ─────────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        //throw trash
    }

    /// <summary>Interact (empty-handed) does nothing here — held items are thrown away with E via InteractWithItem.</summary>
    public override bool ShowsInteractPrompt(PlayerInteractionController player) => false;

    /// <summary>
    /// Folders are accepted by type (no inspector list entry needed); non-empty ones are
    /// rejected with an error notification (negative sound, same as "Trash is full") in
    /// <see cref="InteractWithItem"/>, like supply boxes.
    /// </summary>
    public override bool CanInteractWithItem(PickableObject item)
    {
        return base.CanInteractWithItem(item) || item is FolderController;
    }

    /// <summary>Always takes items (folders by type even with an empty inspector list).</summary>
    public override bool AcceptsHeldItems => true;

    public override string GetItemUseVerb(PlayerInteractionController player, PickableObject item) => "Throw away";

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);

        RequestThrowAway(playerInteractionController, item, this, () =>
        {
            if (audioSource != null && throwTrashSound != null)
                audioSource.PlayOneShot(throwTrashSound);
        });
    }

    // ── Shared discard flow ──────────────────────────────────────────────────

    /// <summary>
    /// Validates <paramref name="item"/> against the discard rules, asks for confirmation, and
    /// on "Yes" destroys the player's held item and invokes <paramref name="onDiscarded"/>
    /// (local client; use it for feedback such as sound). Reused by the dumpster.
    /// </summary>
    public static void RequestThrowAway(
        PlayerInteractionController player,
        PickableObject item,
        UnityEngine.Object context,
        Action onDiscarded)
    {
        if (!CanThrowAway(item, context)) return;

        // Ask first. If the dialog can't open (unassigned / already open / paused), don't discard.
        UIController ui = UIController.Instance;
        if (ui == null) return;

        ui.ShowGameplayConfirmation(
            title: "Throw Away",
            body: ConfirmDiscardMessage,
            confirmText: "Yes",
            cancelText: "No",
            onConfirm: () => ConfirmThrowAway(player, item, context, onDiscarded));
    }

    private static void ConfirmThrowAway(
        PlayerInteractionController player,
        PickableObject item,
        UnityEngine.Object context,
        Action onDiscarded)
    {
        // State may have changed while the popup was open (item dropped/despawned, verdict...).
        if (player == null || item == null || player.pickupController == null) return;
        if (player.pickupController.HeldObject != item) return;
        if (!CanThrowAway(item, context)) return;

        player.pickupController.DestroyEquippedItem();
        onDiscarded?.Invoke();
    }

    /// <summary>
    /// All discard rules. Shows the matching error notification and returns false when the
    /// item can't be thrown away right now.
    /// </summary>
    public static bool CanThrowAway(PickableObject item, UnityEngine.Object context = null)
    {
        if (item is FolderItem folderItem && folderItem.BelongsToUnresolvedSuspectAtWindow())
        {
            UIController.Instance?.ShowErrorNotification(SuspectStillAtWindowMessage);
            return false;
        }

        if (item is FolderController folder && !folder.IsEmpty())
        {
            UIController.Instance?.ShowErrorNotification(FolderNotEmptyMessage);
            return false;
        }

        if (item is SupplyBox supplyBox)
        {
            var remaining = new List<string>();
            if (supplyBox.GetItemsStillInBox(remaining) > 0)
            {
                Debug.Log($"[TrashCan] Supply box not empty — still contains: {string.Join(", ", remaining)}", context);
                UIController.Instance?.ShowErrorNotification(SupplyBoxNotEmptyMessage);
                return false;
            }
        }

        if (item is ExamNotebook examNotebook &&
            examNotebook.CategoryName == DocumentationCategory &&
            Day_01.Instance != null &&
            !Day_01.Instance.DocumentationExamTutorialComplete)
        {
            UIController.Instance?.ShowShopNotification(DocumentationTutorialIncompleteMessage);
            return false;
        }

        return true;
    }
}
