using UnityEngine;

/// <summary>
/// Holds a reference to the <see cref="InWorldSubtitle"/> instance parented above this
/// character's head. Attach to the root of the Suspect prefab and the Player prefab, with
/// <see cref="Subtitle"/> pointing at the child "In World Subtitle" instance.
/// <see cref="ScriptedDialogueRunner"/> resolves this component via the speaker's or player's
/// <c>NetworkObject</c> to show/hide the floating subtitle bubble.
/// </summary>
public class InWorldSubtitleAnchor : MonoBehaviour
{
    [SerializeField] private InWorldSubtitle subtitle;

    public InWorldSubtitle Subtitle => subtitle;
}
