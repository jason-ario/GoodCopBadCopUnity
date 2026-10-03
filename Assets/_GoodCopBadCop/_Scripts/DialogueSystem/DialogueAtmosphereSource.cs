using UnityEngine;

/// <summary>
/// Put on a speaker (same object as, or a parent of, its <see cref="SpeakingInteraction"/>) to
/// apply a screen-space <see cref="DialogueAtmosphereProfile"/> while the local player is in a
/// conversation with them. Resolved by <see cref="DialogueAtmosphereController"/> through
/// <see cref="OverhearRange.TryGetLocalConversationPartner"/>, so it covers booth and scripted dialogue.
/// </summary>
public class DialogueAtmosphereSource : MonoBehaviour
{
    [SerializeField] private DialogueAtmosphereProfile profile;

    public DialogueAtmosphereProfile Profile => profile;
}
