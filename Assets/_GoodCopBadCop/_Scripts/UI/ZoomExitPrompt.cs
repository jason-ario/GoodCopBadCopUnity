using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;

namespace GoodCopBadCop.UI
{
    /// <summary>
    /// "{0} Exit" prompt ({0} = key icon) shown next to the Back button while a view that has its own toggle key
    /// is open:
    /// <list type="bullet">
    ///   <item>Zoom mode (<see cref="HeldItemZoomView"/>) - <see cref="GameAction.ZoomHeldItem"/>.</item>
    ///   <item>The guidebook (<see cref="GuidebookController"/>) - <see cref="GameAction.OpenGuidebook"/> (Tab / View).</item>
    /// </list>
    /// The key icon follows the current binding (and the gamepad icon) via
    /// <see cref="HelperIconKeyDisplay"/>.
    ///
    /// Lives next to the Back button (under <c>Back UI</c>) so it stays visible while the player HUD is
    /// hidden and moves with the Back button's HUD avoidance. This object must stay active so
    /// polling keeps running; it toggles <see cref="_root"/>.
    /// </summary>
    public class ZoomExitPrompt : MonoBehaviour, IIconPromptDriver
    {
        public System.Collections.Generic.IEnumerable<TMP_Text> DrivenLabels => new TMP_Text[] { _label };

        [Tooltip("Visual root toggled while a supported view is open. Must be a child of this object so polling keeps running.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Key icon display; switched to the open view's toggle action.")]
        [SerializeField] private HelperIconKeyDisplay _keyDisplay;

        [Tooltip("Label right of the icon. A label for text left of the icon is cloned from it when a translation needs one.")]
        [SerializeField] private TextMeshProUGUI _label;

        [Tooltip("Prompt sentence; {0} is the toggle key icon, e.g. \"{0} Exit\".")]
        [SerializeField, LocalizeField] private string _text = "{0} Exit";

        private GameAction? _shownAction;
        private IconPromptLabels _labels;

        private void OnEnable()
        {
            Loc.LanguageChanged += ApplyText;
            ApplyText();
            _shownAction = null;
            Refresh();
        }

        private void OnDisable()
        {
            Loc.LanguageChanged -= ApplyText;
        }

        private void ApplyText()
        {
            if (_label == null) return;
            _labels ??= new IconPromptLabels(_label, _keyDisplay);
            _labels.Apply(IconPrompt.Localize(_text));
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            GameAction? action = CurrentExitAction();
            if (action.HasValue && action != _shownAction && _keyDisplay != null)
                _keyDisplay.SetAction(action.Value);
            _shownAction = action;

            bool visible = action.HasValue;
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }

        private static GameAction? CurrentExitAction()
        {
            if (UIController.Instance != null && UIController.Instance.IsPaused) return null;

            if (DiegeticViewController.Current is HeldItemZoomView zoom && zoom.IsZoomed)
                return GameAction.ZoomHeldItem;

            GuidebookController guidebook = GuidebookController.Local;
            if (guidebook != null && guidebook.IsOpen)
                return GameAction.OpenGuidebook;

            return null;
        }
    }
}
