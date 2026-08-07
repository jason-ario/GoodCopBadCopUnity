using System;
using UnityEngine;

/// <summary>
/// Screen-space emote wheel overlay. The wheel visuals (background, dividers, and one
/// <see cref="EmoteButton"/> per slot) are pre-placed in the scene/prefab hierarchy under this
/// object rather than generated at runtime. Each <see cref="EmoteButton"/> calls
/// <see cref="PlayEmote(string)"/> directly when clicked, which fires
/// <see cref="OnEmoteSelected"/> with the matching emote's index.
///
/// Lives on a hidden Canvas/Panel in the scene UI hierarchy (e.g. inside Player UI).
/// Discovered at runtime by <see cref="EmoteInputController"/> via the static Instance.
/// </summary>
public class EmoteWheelUI : MonoBehaviour
{
    public static EmoteWheelUI Instance { get; private set; }

    [Header("Emotes")]
    [Tooltip("Order does not need to match the pre-placed button layout — buttons look themselves " +
             "up by Name via PlayEmote(string).")]
    [SerializeField] private EmoteDefinition[] _emotes = new EmoteDefinition[]
    {
        new EmoteDefinition { Name = "Wave",          AnimBoolName = "Waving",       Duration = 2.5f },
        new EmoteDefinition { Name = "Shrug",         AnimBoolName = "Shrug",        Duration = 2.0f },
        new EmoteDefinition { Name = "Dance",         AnimBoolName = "Dance",        Duration = 4.0f },
        new EmoteDefinition { Name = "Thumbs Up",     AnimBoolName = "ThumbsUp",     Duration = 2.0f },
        new EmoteDefinition { Name = "Puke",          AnimBoolName = "Puke",         Duration = 3.0f },
        new EmoteDefinition { Name = "Cough",         AnimBoolName = "Cough",        Duration = 2.5f },
        new EmoteDefinition { Name = "Point",         AnimBoolName = "Point",        Duration = 2.0f },
        new EmoteDefinition { Name = "Middle Finger", AnimBoolName = "MiddleFinger", Duration = 1.5f },
    };

    /// <summary>Fired with the selected emote index when the player clicks an emote button.</summary>
    public event Action<int> OnEmoteSelected;

    /// <summary>Read-only access to the emote definitions so <see cref="EmoteInputController"/> can look up data.</summary>
    public EmoteDefinition[] Emotes => _emotes;

    // ─── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ─── Public API ─────────────────────────────────────────────────────────

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        foreach (EmoteButton button in GetComponentsInChildren<EmoteButton>(includeInactive: true))
            button.Deselect();

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by an <see cref="EmoteButton"/> when clicked. Looks up the emote by
    /// <see cref="EmoteDefinition.Name"/> and fires <see cref="OnEmoteSelected"/> with its index.
    /// </summary>
    public void PlayEmote(string emoteName)
    {
        int index = Array.FindIndex(_emotes, e => e.Name == emoteName);
        if (index < 0)
        {
            Debug.LogWarning($"[EmoteWheelUI] No emote found named '{emoteName}'.", this);
            return;
        }

        PlayEmote(index);
    }

    /// <summary>Fires <see cref="OnEmoteSelected"/> for the emote at <paramref name="index"/>.</summary>
    public void PlayEmote(int index)
    {
        if (index < 0 || index >= _emotes.Length) return;
        OnEmoteSelected?.Invoke(index);
    }
}
