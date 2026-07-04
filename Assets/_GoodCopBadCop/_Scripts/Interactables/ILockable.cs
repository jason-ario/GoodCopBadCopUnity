/// <summary>
/// Implemented by objects that can be locked and unlocked (e.g. <see cref="ToolsLocker"/>).
/// Unlock and Lock should only be called from server-side code.
/// </summary>
public interface ILockable
{
    /// <summary>Whether the object is currently locked.</summary>
    bool IsLocked { get; }

    /// <summary>Locks the object. Must be called on the server.</summary>
    void Lock();

    /// <summary>Unlocks the object. Must be called on the server.</summary>
    void Unlock();
}
