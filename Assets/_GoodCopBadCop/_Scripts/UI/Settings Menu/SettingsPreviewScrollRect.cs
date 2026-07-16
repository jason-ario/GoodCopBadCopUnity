using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Preview scroll view that accepts mouse wheel input while reserving drag input for its scrollbar.
    /// </summary>
    public sealed class SettingsPreviewScrollRect : ScrollRect
    {
        public override void OnBeginDrag(PointerEventData eventData) { }
        public override void OnDrag(PointerEventData eventData) { }
        public override void OnEndDrag(PointerEventData eventData) { }
    }
}