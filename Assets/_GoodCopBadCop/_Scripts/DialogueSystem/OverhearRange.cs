using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single source of truth for whether the local player may see a conversation's subtitles.
/// Every subtitle path (booth/suspect dialogue, scripted dialogue, world dialogue, in-world
/// bubbles, NPC barks) routes through here so the range is driven by one value:
/// <see cref="GameSettings.OverhearSubtitleProximity"/>.
/// <para>
/// Several conversations can run in parallel (one per NPC, see <see cref="ScriptedDialogueRunner"/>
/// session slots and <see cref="SuspectWorldDialogue"/>). A player who is IN a conversation sees
/// only that conversation's lines — never a parallel one's, even within overhear range, so another
/// NPC's line can't evict their own waiting subtitle or prompt.
/// </para>
/// </summary>
public static class OverhearRange
{
    /// <summary>Max overhear distance in meters, from <see cref="GameSettings"/>.</summary>
    public static float Distance => GameSettings.Instance.OverhearSubtitleProximity;

    /// <summary>
    /// True when the local player is locked into a conversation (scripted dialogue participant,
    /// world conversation, or booth dialogue-choice mode).
    /// </summary>
    public static bool IsLocalPlayerConversationParticipant =>
        ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode ||
        SuspectWorldDialogue.IsLocalPlayerInConversation;

    /// <summary>
    /// Resolves who the local player is currently in a conversation with: their scripted session's
    /// speaker, else their world-conversation NPC, else the current booth suspect while in booth
    /// dialogue-choice mode. Returns false when the local player isn't a participant or the partner
    /// can't be resolved.
    /// </summary>
    public static bool TryGetLocalConversationPartner(out SpeakingInteraction partner)
    {
        partner = null;

        if (ScriptedDialogueRunner.IsScriptedModeActive)
        {
            ulong netId = ScriptedDialogueRunner.LocalSpeakerNetId;
            var nm = NetworkManager.Singleton;
            if (netId != 0 && nm != null && nm.SpawnManager != null &&
                nm.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj) && netObj != null)
                partner = netObj.GetComponent<SpeakingInteraction>();
        }
        else if (SuspectWorldDialogue.TryGetLocalConversationSpeaker(out SpeakingInteraction worldSpeaker))
        {
            partner = worldSpeaker;
        }
        else if (DialogueChoiceSystem.IsInDialogueMode &&
                 SuspectController.Instance != null && SuspectController.Instance.CurrentSuspect != null)
        {
            partner = SuspectController.Instance.CurrentSuspect.Speaking;
        }

        return partner != null;
    }

    /// <summary>
    /// True when the local player is in a conversation whose partner is resolvable and is NOT the
    /// speaker at <paramref name="source"/> — i.e. <paramref name="source"/> belongs to a parallel
    /// conversation the local player must not see.
    /// </summary>
    public static bool IsLocalPlayerInOtherConversation(Transform source)
    {
        if (source == null || !IsLocalPlayerConversationParticipant) return false;
        if (!TryGetLocalConversationPartner(out SpeakingInteraction partner)) return false;
        return !IsSameSpeaker(partner, source);
    }

    private static bool IsSameSpeaker(SpeakingInteraction partner, Transform source)
    {
        if (partner == null || source == null) return false;
        Transform p = partner.transform;
        if (p == source || source.IsChildOf(p) || p.IsChildOf(source)) return true;

        NetworkObject a = partner.GetComponentInParent<NetworkObject>();
        NetworkObject b = source.GetComponentInParent<NetworkObject>();
        return a != null && a == b;
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
    /// Bystander check for a conversation the local player is not part of: within overhear range,
    /// and not busy in a different conversation of their own.
    /// </summary>
    public static bool CanLocalPlayerOverhear(Transform source)
    {
        return !IsLocalPlayerInOtherConversation(source) && IsLocalPlayerWithin(source);
    }

    /// <summary>
    /// True when the local player should see a subtitle spoken at <paramref name="source"/>:
    /// either it is their own conversation's partner, or they are a free bystander within
    /// overhear range. While in a conversation with an unresolvable partner, every line is shown
    /// (legacy behaviour).
    /// </summary>
    public static bool CanLocalPlayerSee(Transform source)
    {
        if (IsLocalPlayerConversationParticipant)
        {
            if (source == null) return true;
            if (TryGetLocalConversationPartner(out SpeakingInteraction partner))
                return IsSameSpeaker(partner, source);
            return true;
        }

        return IsLocalPlayerWithin(source);
    }
}
