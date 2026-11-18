using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Reports pointer hover / press on a pooled settings row (or its gamepad binding cell) back to
    /// SettingsRedesignPreviewController, which owns the hover highlight and click sound.
    /// Does not implement IScrollHandler, so wheel input still bubbles to the settings scroll view.
    /// </summary>
    public sealed class SettingsPreviewRowPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        public Action<bool> HoverChanged;
        public Action Pressed;

        private bool isHovered;

        public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);

        public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Pressed?.Invoke();
        }

        // Pooled rows are deactivated on tab switch without a pointer exit, so drop the hover here.
        private void OnDisable() => SetHovered(false);

        private void SetHovered(bool hovered)
        {
            if (isHovered == hovered) return;
            isHovered = hovered;
            HoverChanged?.Invoke(hovered);
        }
    }
}
