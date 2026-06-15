using UnityEngine;

/// <summary>
/// Configuration for a single doppelganger type. One asset per impersonatable suspect.
/// Place assets under _Data/Doppelgangers/. Assign the pool to DailySuspectManager
/// via a DoppelgangerLineupSet.
/// </summary>
[CreateAssetMenu(menuName = "Scriptable Objects/Doppelganger Data")]
public class DoppelgangerData : ScriptableObject
{
    [Tooltip("The suspect whose CharacterPrefab and identity are impersonated.")]
    public SuspectData targetSuspect;

    [Tooltip("Number of overlapping anomalies drawn from the biological/behavior pools, " +
             "applied in addition to all uncanny anomalies.")]
    [Min(0)] public int overlappingAnomalyCount = 1;

    [Tooltip("How much to desaturate the skin material via MaterialPropertyBlock " +
             "(0 = no change, 1 = fully desaturated).")]
    [Range(0f, 1f)] public float skinDesaturationAmount = 0.3f;

    [Tooltip("When true, the doppelganger's subtle idle micro-movement animation layer is suppressed.")]
    public bool removeIdleMicroMovements = true;
}
