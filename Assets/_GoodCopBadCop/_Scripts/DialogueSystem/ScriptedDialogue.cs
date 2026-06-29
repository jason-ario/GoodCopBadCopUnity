using System;
using UnityEngine;

/// <summary>
/// Defines whether a <see cref="ScriptedDialogueNode"/> delivers a single line
/// or presents the player with two response choices.
/// </summary>
public enum ScriptedDialogueNodeType
{
    /// <summary>NPC speaks; the player clicks or presses E to advance.</summary>
    Monologue,

    /// <summary>
    /// NPC speaks, then two player-choice buttons appear. The NPC delivers a
    /// unique reply to the chosen option before the sequence continues linearly.
    /// </summary>
    Choice,
}

/// <summary>
/// A single player-selectable response and the NPC's unique reply to it.
/// Used inside a <see cref="ScriptedDialogueNode"/> of type <see cref="ScriptedDialogueNodeType.Choice"/>.
/// </summary>
[Serializable]
public class ScriptedDialogueChoice
{
    [Tooltip("Text shown on the player's choice button.")]
    [TextArea(1, 3)]
    public string playerChoiceText;

    [Tooltip("NPC response delivered after the player picks this choice. " +
             "The player must press E / click to continue after it plays.")]
    [TextArea(2, 6)]
    public string npcResponse;

    [Tooltip("Optional Animator trigger fired on the NPC at the start of the response line. " +
             "Leave empty for no animation.")]
    public string animationTrigger;
}

/// <summary>
/// A single step in a <see cref="ScriptedDialogue"/> sequence.
/// </summary>
[Serializable]
public class ScriptedDialogueNode
{
    [Tooltip("Monologue — NPC speaks, player advances.\n" +
             "Choice — NPC speaks, then two choice buttons appear.")]
    public ScriptedDialogueNodeType type = ScriptedDialogueNodeType.Monologue;

    [Tooltip("Line the NPC delivers at the start of this node. Used for both node types.")]
    [TextArea(2, 6)]
    public string npcLine;

    [Tooltip("Optional Animator trigger fired on the NPC at the start of this node's NPC line. " +
             "Leave empty for no animation.")]
    public string animationTrigger;

    [Tooltip("Player choices. Required when Type is Choice. Exactly two entries expected.")]
    public ScriptedDialogueChoice[] choices;
}

/// <summary>
/// Scripted dialogue sequence authored as a ScriptableObject.
///
/// Assign to a <see cref="ScriptedDialogueRunner"/> and call
/// <see cref="ScriptedDialogueRunner.PlayDialogue"/> to play the conversation in-game.
///
/// Monologue nodes: the NPC speaks one line; the player presses E or clicks to advance.
/// Choice nodes: the NPC speaks, the player picks one of two responses, the NPC gives a
/// unique reply, and the sequence continues on a single linear path from the next node.
/// </summary>
[CreateAssetMenu(fileName = "New Scripted Dialogue", menuName = "Dialogue/Scripted Dialogue")]
public class ScriptedDialogue : ScriptableObject
{
    public ScriptedDialogueNode[] nodes;
}
