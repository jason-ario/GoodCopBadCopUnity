/// <summary>
/// Optional component on a <see cref="SuspectCharacter"/> that takes over the plain "Talk"
/// interaction (the step that would otherwise open its <see cref="SuspectWorldDialogue"/>).
/// Locks, verdict/mutant close-outs, the post-dialogue block window and rejoining an active
/// scripted dialogue are still handled by <see cref="SuspectCharacter.Interact"/> first.
/// Called locally on the interacting client.
/// </summary>
public interface ISuspectTalkOverride
{
    /// <summary>Return true when the interaction was handled.</summary>
    bool TryHandleTalk(SuspectCharacter character);
}
