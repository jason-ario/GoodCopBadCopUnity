using UnityEngine;

/// <summary>
/// Helpers for forcing world-space AudioSources into fully 3D, short-range playback so
/// network-replicated one-shots (gates, doors, etc.) are only heard by nearby players.
/// </summary>
public static class SpatialAudioUtility
{
    /// <summary>
    /// Configures <paramref name="source"/> as a fully 3D source with a linear rolloff that
    /// reaches silence at <paramref name="maxDistance"/>.
    /// </summary>
    public static void ConfigureShortRange3D(AudioSource source, float minDistance, float maxDistance)
    {
        if (source == null) return;

        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
        source.dopplerLevel = 0f;
    }
}
