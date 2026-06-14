using UnityEngine;

/// <summary>
/// A rectangular horizontal zone in which objects (e.g. trash bags) can be spawned.
/// Extracted from TakeOutTrashTask so it can be reused by TrashThreat and other systems.
/// </summary>
[System.Serializable]
public struct SpawnZone
{
    [Tooltip("Centre pivot of this zone.")]
    public Transform Center;

    [Tooltip("Half-extents on X and Z. Y is ignored for horizontal placement.")]
    public Vector3 HalfExtents;
}
