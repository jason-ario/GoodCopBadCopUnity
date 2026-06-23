/// <summary>
/// Implemented by any world-space object that should respond to cursor hover
/// when detected by <see cref="ClickDetector"/>.
/// </summary>
public interface IHoverable
{
    void OnHoverEnter();
    void OnHoverExit();
}
