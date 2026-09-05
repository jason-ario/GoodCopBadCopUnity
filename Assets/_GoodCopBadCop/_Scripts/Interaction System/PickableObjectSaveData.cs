using System;
using UnityEngine;

/// <summary>
/// Serializable snapshot of one <see cref="PickableObject"/>'s transform, keyed by its scene
/// hierarchy path (see <see cref="PickableObject.SaveId"/>). Persisted in the
/// active save slot's <c>PickableObjects</c> list (see <see cref="SaveDataManager"/>) and used
/// both for general save/load and to restore world clutter to its last checkpoint when a player
/// dies and retries (see <see cref="ShiftManager.HandleAllSuspectsProcessed"/> and
/// <see cref="GameManager.RestartDay"/>).
/// </summary>
[Serializable]
public class PickableObjectSaveData
{
    public bool HasExistenceState;
    public bool Exists;
    public string Id;
    public Vector3 Position;
    public Vector3 EulerRotation;

    // Optional, type-specific state. Values are only interpreted by the matching pickable type.
    public bool HasResourceAmount;
    public float ResourceAmount;
    public bool HasInternalBatteryCharge;
    public float InternalBatteryCharge;
    public bool HasDurability;
    public int Durability;
    public int SecondaryState;
    public int[] IntegerState = Array.Empty<int>();
    public bool[] BooleanState = Array.Empty<bool>();
}
