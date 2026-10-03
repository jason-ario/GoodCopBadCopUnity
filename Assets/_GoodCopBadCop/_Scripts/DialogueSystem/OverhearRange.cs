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

    /// <summary>
    /// Resolves who the local player is currently in a conversation with: the active scripted
    /// speaker (<see cref="ScriptedDialogueRunner.ActiveDialogueSpeakerNetId"/>) in scripted mode,
    /// otherwise the current booth suspect while in booth dialogue-choice mode. Returns false
    /// when the local player isn't a participant or the partner can't be resolved.
    /// </summary>
    public static bool TryGetLocalConversationPartner(out SpeakingInteraction partner)
    {
        partner = null;

        if (ScriptedDialogueRunner.IsScriptedModeActive)
        {
            ulong netId = ScriptedDialogueRunner.ActiveDialogueSpeakerNetId;
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (netId != 0 && nm != null && nm.SpawnManager != null &&
                nm.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj) && netObj != null)
                partner = netObj.GetComponent<SpeakingInteraction>();
        }
        else if (DialogueChoiceSystem.IsInDialogueMode &&
                 SuspectController.Instance != null && SuspectController.Instance.CurrentSuspect != null)
        {
            partner = SuspectController.Instance.CurrentSuspect.Speaking;
        }

        return partner != null;
    }

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
