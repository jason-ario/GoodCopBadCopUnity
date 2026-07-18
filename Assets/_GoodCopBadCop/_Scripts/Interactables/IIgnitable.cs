/// <summary>
/// Implemented by world objects that can be set alight by ignition sources
/// such as the <see cref="Flamethrower"/> or a <see cref="Match"/>.
/// <see cref="Ignite"/> must only be called on the server.
/// </summary>
public interface IIgnitable
{
    /// <summary>
    /// Server-only. Lights (or resets) this object's fire.
    /// </summary>
    void Ignite();
}
