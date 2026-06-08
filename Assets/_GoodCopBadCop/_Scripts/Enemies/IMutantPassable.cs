/// <summary>
/// Implemented by doors and gates that a <see cref="MutantEnemy"/> can force open
/// when the closed NavMeshObstacle is blocking its path to a target.
/// All members must only be called on the server.
/// </summary>
public interface IMutantPassable
{
    /// <summary>
    /// True when this obstacle is actively blocking the NavMesh and can be forced open by a mutant.
    /// Returns false if the door/gate is already open or is locked against mutant access.
    /// </summary>
    bool IsBlockingMutant { get; }

    /// <summary>
    /// Forces this obstacle open so the mutant can pass through.
    /// Must only be called on the server.
    /// </summary>
    void OpenForMutant();
}
