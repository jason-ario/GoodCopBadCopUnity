/// <summary>
/// A lever-style object driven from a <see cref="LeverDiegeticController"/> close-up view
/// (the booth shutter <see cref="Lever"/>, the power station <see cref="PowerSwitch"/>).
/// All drag inputs use "positive = up"; each implementation maps that onto its own
/// normalised travel and decides what the up / down ends mean.
/// </summary>
public interface IDiegeticLever
{
    /// <summary>Whether the lever currently accepts player input. The view closes when this goes false.</summary>
    bool IsUsable { get; }

    /// <summary>Starts a drag from the lever's current committed state.</summary>
    void BeginDrag();

    /// <summary>Moves the lever by a raw per-frame Mouse Y delta (positive = up).</summary>
    void DragByMouse(float mouseYDelta);

    /// <summary>Moves the lever by a continuous stick value in [-1, 1] (positive = up), scaled by deltaTime.</summary>
    void DragByStick(float stickY);

    /// <summary>Ends the drag and commits the lever to the nearest end.</summary>
    void EndDrag();

    /// <summary>Ends the drag and commits the lever to the given end regardless of travel (gamepad tap).</summary>
    void EndDragToward(bool up);

    /// <summary>Reaches the player's free hand onto the lever (grip pose + arm IK).</summary>
    void BeginHandGrab(PlayerInteractionController player);

    /// <summary>Releases the hand grab started by <see cref="BeginHandGrab"/>.</summary>
    void EndHandGrab();

    /// <summary>Hover / drag highlight (implemented by <see cref="Interactable"/>).</summary>
    void Highlight(bool highlight);
}
