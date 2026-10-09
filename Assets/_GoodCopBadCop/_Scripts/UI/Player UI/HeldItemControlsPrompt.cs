using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Bottom-right HUD "Held Item Controls" stack: one row per extra input that works with the held item,
/// alongside the "[LMB] to {verb}" row (<see cref="HeldItemUsePrompt"/>) and the "[R] to reload" row
/// (<see cref="HeldItemReloadPrompt"/>). Each row here is shown only while its input would do something:
///  • Inspect (F / ZoomHeldItem): the item has a <see cref="HeldItemZoomable"/> that can open zoom mode.
///  • Place (RMB / PlaceObject): the item may use placement boards (<see cref="PickableItemData.cantUsePlacementBoard"/>).
///  • Throw (MMB / ThrowObject): the item can be thrown (<see cref="PickableItemData.canBeThrown"/>).
/// Row icons are <c>HelperIconKeyDisplay</c> components pinned to the matching GameAction, so they follow
/// rebinding and the active input device.
///
/// Attach to the always-active "Player HUD" root (not the rows, which are toggled).
/// </summary>
public class HeldItemControlsPrompt : MonoBehaviour
{
    public enum Control
    {
        Inspect,
        Place,
        Throw,
    }

    [Serializable]
    private struct Row
    {
        public Control control;

        [Tooltip("Prompt row toggled with this control's availability (child of Held Item Controls).")]
        public GameObject root;

        [Tooltip("Label next to the icon.")]
        public TMP_Text label;

        [Tooltip("Label text, e.g. \"to inspect\".")]
        public string text;
    }

    [SerializeField] private Row[] _rows = Array.Empty<Row>();

    private void OnEnable()
    {
        foreach (Row row in _rows)
            if (row.label != null && !string.IsNullOrEmpty(row.text)) row.label.text = row.text;
        HideAll();
    }

    private void OnDisable() => HideAll();

    private void Update()
    {
        PlayerPickupController pickup = ResolveLocalPickup(out PlayerInteractionController interaction);
        PickableObject held = pickup != null ? pickup.HeldObject : null;

        // Held-item controls only apply in free first-person play: not paused, not inside a
        // diegetic view (zoom, PC, lockers…), and while pickup/place input isn't locked.
        bool active = held != null
                      && !(UIController.Instance != null && UIController.Instance.IsPaused)
                      && !DiegeticViewController.IsAnyViewActive
                      && pickup.CanPickUpAndPlace;

        foreach (Row row in _rows)
            SetVisible(row.root, active && IsAvailable(row.control, held, interaction));
    }

    private static bool IsAvailable(Control control, PickableObject held, PlayerInteractionController interaction)
    {
        PickableItemData data = held.ItemData;
        switch (control)
        {
            case Control.Inspect:
            {
                // Mirrors HeldItemZoomView.TryOpen's item-level gates.
                if (interaction != null && !interaction.CanInteract) return false;
                HeldItemZoomable zoomable = held.GetComponent<HeldItemZoomable>();
                if (zoomable == null || !zoomable.CanBeginZoom) return false;
                return !held.IsBeingUsed || zoomable.InspectWhileZoomed;
            }
            case Control.Place:
                return data == null || !data.cantUsePlacementBoard;
            case Control.Throw:
                return data == null || data.canBeThrown;
            default:
                return false;
        }
    }

    private static PlayerPickupController ResolveLocalPickup(out PlayerInteractionController interaction)
    {
        interaction = null;

        // The held item is only known locally, so hide while mirroring a teammate's HUD.
        if (SpectateManager.IsHudMirroringTarget) return null;

        PlayerInstance player = PlayerInstance.Instance;
        if (player == null) return null;
        interaction = player.PlayerInteractionController;
        return player.PlayerPickupController;
    }

    private void HideAll()
    {
        foreach (Row row in _rows) SetVisible(row.root, false);
    }

    private static void SetVisible(GameObject root, bool visible)
    {
        if (root != null && root.activeSelf != visible) root.SetActive(visible);
    }
}
