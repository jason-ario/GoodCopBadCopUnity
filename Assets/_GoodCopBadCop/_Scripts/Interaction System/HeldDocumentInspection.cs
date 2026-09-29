/// <summary>
/// Shared "inspect" behaviour for held documents (ID card, application form, newspaper,
/// daily fax). Holding LMB raises the document via the <see cref="AnimBool"/> animator bool;
/// releasing it lowers the document again.
///
/// Zoom mode (<see cref="HeldItemZoomView"/>) reuses this exact path: it "holds LMB" through
/// <see cref="PlayerPickupController.BeginHeldUseFromZoom"/> and releases it on close.
/// </summary>
public static class HeldDocumentInspection
{
    /// <summary>Animator bool that drives every document's inspect pose.</summary>
    public const string AnimBool = "UsingTool";

    /// <summary>Call from a document's <see cref="PickableObject.OnStartUse"/>.</summary>
    public static void BeginFromUse(PlayerPickupController holder)
    {
        SetInspecting(holder, true);
    }

    /// <summary>Call from a document's <see cref="PickableObject.OnStopUse"/>.</summary>
    public static void EndFromUse(PlayerPickupController holder)
    {
        SetInspecting(holder, false);
    }

    public static void SetInspecting(PlayerPickupController holder, bool inspecting)
    {
        if (holder == null || holder.PlayerAnimationController == null) return;
        holder.PlayerAnimationController.SetAnimBool(AnimBool, inspecting);
    }
}
