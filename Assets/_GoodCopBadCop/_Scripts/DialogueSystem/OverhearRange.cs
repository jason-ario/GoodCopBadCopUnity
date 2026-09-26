using UnityEngine;

/// <summary>
/// Single source of truth for whether the local player may see a conversation's subtitles.
/// Every subtitle path (booth/suspect dialogue, scripted dialogue, world dialogue, in-world
/// bubbles, NPC barks) routes through here so the range is driven by one value:
/// <see cref="GameSettings.OverhearSubtitleProximity"/>.
/// </summary>
public static class OverhearRange
{
    /// <summary>Max overhear distance in meters, from <see cref="GameSettings"/>.</summary>
    public static float Distance => GameSettings.Instance.OverhearSubtitleProximity;

    /// <summary>
    /// True when the local player is locked into a conversation (scripted dialogue participant
    /// or booth dialogue-choice mode). Participants always see their own conversation's subtitles.
    /// </summary>
    public static bool IsLocalPlayerConversationParticipant =>
        ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode;

    /// <summary>True when the local player is within <see cref="Distance"/> of <paramref name="position"/>.</summary>
    public static bool IsLocalPlayerWithin(Vector3 position)
    {
        Transform localPlayer = PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;
        if (localPlayer == null) return false;

        float max = Distance;
        return (localPlayer.position - position).sqrMagnitude <= max * max;
    }

    /// <summary>
    /// Range check against <paramref name="source"/>. A null source cannot be located, so the
    /// line is treated as audible (keeps legacy behaviour for speaker-less lines).
    /// </summary>
    public static bool IsLocalPlayerWithin(Transform source)
    {
        return source == null || IsLocalPlayerWithin(source.position);
    }

    /// <summary>
    /// True when the local player should see a subtitle spoken at <paramref name="source"/>:
    /// either they are a conversation participant, or they are within overhear range.
    /// </summary>
    public static bool CanLocalPlayerSee(Transform source)
    {
        return IsLocalPlayerConversationParticipant || IsLocalPlayerWithin(source);
    }
}
