/// <summary>
/// Implemented by PC terminal elements that support click-and-drag interaction driven by
/// <see cref="SimpleCanvasCursorFromMouseDelta"/> (e.g. scrollbars, scroll view drag areas).
/// </summary>
public interface IPCDraggable
{
    void BeginDrag();
    void DragFromCursorDelta();
    void EndDrag();
}
