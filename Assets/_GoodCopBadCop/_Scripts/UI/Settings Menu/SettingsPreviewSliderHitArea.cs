using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Enlarges the slider interaction target without changing the visible track or handle.
    /// It intentionally does not implement IScrollHandler, so wheel input still bubbles to the settings scroll view.
    /// </summary>
    public sealed class SettingsPreviewSliderHitArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<PointerEventData> PointerChanged;

        public void OnPointerDown(PointerEventData eventData)
        {
            PointerChanged?.Invoke(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            PointerChanged?.Invoke(eventData);
        }
    }
}