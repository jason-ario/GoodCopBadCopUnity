/// <summary>
/// Implemented by any held item that can ignite a <see cref="FirePit"/>.
///
/// The fire pit checks <see cref="IsLit"/> server-side before accepting the ignition;
/// it then calls <see cref="OnUsedToIgnite"/> (also server-side) so the item can update
/// its own state (decrement match count, extinguish, or despawn) without the fire pit
/// needing to know which concrete type it is dealing with.
/// </summary>
public interface IIgnitionSource
{
    /// <summary>True once the item is lit and ready to ignite a fire pit.</summary>
    bool IsLit { get; }

    /// <summary>
    /// Called server-side by the <see cref="FirePit"/> immediately after a successful
    /// <see cref="FirePit.Ignite"/> call. Implementations should update their own
    /// authoritative state here (e.g. decrement match count, reset lit flag).
    /// This is a plain method call — no RPC needed because it is already executing on
    /// the server inside <c>IgniteWithItemServerRpc</c>.
    /// </summary>
    void OnUsedToIgnite();
}
