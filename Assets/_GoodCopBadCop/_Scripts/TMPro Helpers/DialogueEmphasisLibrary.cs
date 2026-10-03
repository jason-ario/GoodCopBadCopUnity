using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of <see cref="DialogueEmphasisProfile"/>s, loaded from
/// <c>Resources/DialogueEmphasisLibrary</c>. Maps markup ids to profiles and defines which
/// profiles the <c>[[ ]]</c> and <c>{{ }}</c> shortcuts use.
/// </summary>
[CreateAssetMenu(menuName = "Dialogue/Emphasis Library")]
public class DialogueEmphasisLibrary : ScriptableObject
{
    public const string ResourcePath = "DialogueEmphasisLibrary";

    [SerializeField] private List<DialogueEmphasisProfile> profiles = new List<DialogueEmphasisProfile>();
    [Tooltip("Profile id used by [[phrase]] with no id prefix.")]
    [SerializeField] private string bracketDefaultId = "keyword";
    [Tooltip("Profile id used by {{phrase}} with no id prefix.")]
    [SerializeField] private string braceDefaultId = "menace";

    private static DialogueEmphasisLibrary _instance;
    private static bool _loadAttempted;

    public static DialogueEmphasisLibrary Instance
    {
        get
        {
            if (_instance == null && !_loadAttempted)
            {
                _loadAttempted = true;
                _instance = Resources.Load<DialogueEmphasisLibrary>(ResourcePath);
                if (_instance == null)
                    Debug.LogWarning($"[DialogueEmphasis] No library at Resources/{ResourcePath}; emphasis falls back to plain bold.");
            }
            return _instance;
        }
    }

    public IReadOnlyList<DialogueEmphasisProfile> Profiles => profiles;
    public DialogueEmphasisProfile BracketDefault => Get(bracketDefaultId);
    public DialogueEmphasisProfile BraceDefault => Get(braceDefaultId);

    /// <summary>Case-insensitive lookup by <see cref="DialogueEmphasisProfile.id"/>. Null if not found.</summary>
    public DialogueEmphasisProfile Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            if (p != null && string.Equals(p.id, id, System.StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private void OnEnable()
    {
        // Edits in the editor (or a re-import) should be picked up by the static cache.
        if (_instance == null || _instance == this)
        {
            _instance = this;
            _loadAttempted = true;
        }
    }
}
