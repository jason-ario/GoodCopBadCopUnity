using HighlightPlus;
using UnityEngine;

/// <summary>
/// Diegetic view for the mini fridge. Extends <see cref="DiegeticViewController"/> with
/// item-pickup logic: raycasting each frame to detect <see cref="PickableObject"/> items
/// inside the fridge, highlighting them on hover, and picking them up for free on click.
/// </summary>
public class MiniFridgeDiegeticController : DiegeticViewController
{
    [Header("Fridge Setup")]
    [Tooltip("The fridge body's collider — disabled while the view is open so it doesn't block item raycasts.")]
    [SerializeField] private Collider _fridgeCollider;

    [Header("UI")]
    [Tooltip("Cursor-following prompt shown when hovering a pickable item. Optional.")]
    [SerializeField] private CursorPromptController _cursorPrompt;

    // ─── Runtime state ────────────────────────────────────────────────────────

    private MiniFridge _fridge;
    private PickableObject _lastHovered;

    // ─── Constants ────────────────────────────────────────────────────────────

    private const string HoldingObjectMessage = "Put down what you're holding first!";

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the mini fridge diegetic view for <paramref name="player"/>.
    /// Stores the <paramref name="fridge"/> reference so the door can be closed on exit.
    /// </summary>
    public void Open(PlayerInteractionController player, MiniFridge fridge)
    {
        _fridge = fridge;
        base.Open(player);
    }

    // ─── DiegeticViewController hooks ────────────────────────────────────────

    protected override void OnOpened()
    {
        if (_fridgeCollider != null)
            _fridgeCollider.enabled = false;

        _cursorPrompt?.Hide();
    }

    protected override void OnClosed()
    {
        ClearHover();
        _cursorPrompt?.Hide();

        if (_fridgeCollider != null)
            _fridgeCollider.enabled = true;

        if (_fridge != null)
        {
            _fridge.RequestClose();
            _fridge = null;
        }
    }

    /// <summary>
    /// Each frame while the fridge view is open: raycasts from the cursor through the
    /// scene camera to fire hover/unhover highlight changes on fridge items and pick up
    /// the clicked item for free.
    /// </summary>
    protected override void OnUpdate()
    {
        Camera cam = RaycastCamera;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide);

        PickableObject hovered = didHit ? hit.collider.GetComponentInParent<PickableObject>() : null;

        if (hovered != _lastHovered)
        {
            SetHighlight(_lastHovered, false);
            SetHighlight(hovered, true);

            if (hovered != null)
                _cursorPrompt?.Show(hovered.interactText);
            else
                _cursorPrompt?.Hide();

            _lastHovered = hovered;
        }

        if (Input.GetMouseButtonDown(0) && hovered != null)
            TryPickUp(hovered);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void ClearHover()
    {
        SetHighlight(_lastHovered, false);
        _lastHovered = null;
    }

    private static void SetHighlight(PickableObject item, bool on)
    {
        if (item == null) return;
        HighlightEffect effect = item.GetComponent<HighlightEffect>();
        if (effect != null)
            effect.enabled = on;
    }

    private void TryPickUp(PickableObject item)
    {
        if (item == null || !item.CanPickUpManually) return;

        PlayerPickupController pickup = GetLocalPlayerPickup();
        if (pickup == null)
        {
            Debug.LogError("MiniFridgeDiegeticController: Could not find local PlayerPickupController.");
            return;
        }

        if (pickup.IsHoldingObject)
        {
            UIController.Instance.ShowShopNotification(HoldingObjectMessage);
            return;
        }

        ClearHover();
        _cursorPrompt?.Hide();
        pickup.PickUpObject(item);
        Close();
    }

    private static PlayerPickupController GetLocalPlayerPickup()
    {
        if (PlayerInstance.Instance == null) return null;
        return PlayerInstance.Instance.GetComponent<PlayerPickupController>();
    }
}
