using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;

namespace GoodCopBadCop.UI
{
    /// <summary>
    /// "[F] Exit" prompt shown while zoom mode (<see cref="HeldItemZoomView"/>) is open, telling the
    /// player that the zoom key closes it again. The key icon follows the current
    /// <see cref="GameAction.ZoomHeldItem"/> binding (and the gamepad icon) via
    /// <see cref="HelperIconKeyDisplay"/>.
    ///
    /// Lives next to the Back button (under <c>Back UI</c>) so it stays visible while the player HUD is
    /// hidden by the diegetic view and moves with the Back button's HUD avoidance. This object must
    /// stay active so polling keeps running; it toggles <see cref="_root"/>.
    /// </summary>
    public class ZoomExitPrompt : MonoBehaviour
    {
        [Tooltip("Visual root toggled while zoomed. Must be a child of this object so polling keeps running.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Key icon display; forced to the Zoom Item action on Awake.")]
        [SerializeField] private HelperIconKeyDisplay _keyDisplay;

        [Tooltip("Optional label next to the icon.")]
        [SerializeField] private TextMeshProUGUI _label;

        [SerializeField] private string _text = "Exit";

        private void Awake()
        {
            if (_keyDisplay != null) _keyDisplay.SetAction(GameAction.ZoomHeldItem);
        }

        private void OnEnable()
        {
            if (_label != null) _label.text = _text;
            SetVisible(IsZoomed());
        }

        private void Update()
        {
            SetVisible(IsZoomed());
        }

        private static bool IsZoomed()
        {
            bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
            return !paused && DiegeticViewController.Current is HeldItemZoomView zoom && zoom.IsZoomed;
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }
    }
}
