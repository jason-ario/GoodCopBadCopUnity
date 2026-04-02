using UnityEngine;

public class ClickablePCScrollbarTrack : ClickablePCElement
{
    [SerializeField] private ClickablePCScrollbar targetScrollbar;

    public override void OnClick()
    {
        if (targetScrollbar == null)
            return;

        targetScrollbar.JumpToCursorPosition();
    }

    public override void OnHoverEnter()
    {
        base.OnHoverEnter();
    }

    public override void OnHoverExit()
    {
        base.OnHoverExit();
    }
}