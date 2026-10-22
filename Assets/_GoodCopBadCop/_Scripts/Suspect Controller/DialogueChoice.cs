using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DialogueChoice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>English source text (also used for history and subtitle RPCs). Never displayed decorated as-is.</summary>
    public string choiceText;
    [SerializeField] private TextMeshProUGUI choiceTextUI;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    [SerializeField] int choiceIndex;
    
    private static readonly Color PickedColor = new Color(1f, 0.65f, 0f); // orange

    private bool _isPicked;
    private bool _isHovered;

    private void OnEnable() { Loc.LanguageChanged += RefreshText; }

    private void OnDisable() { Loc.LanguageChanged -= RefreshText; }

    public void SetChoiceText(string text)
    {
        choiceText = text;
        _isHovered = false;
        RefreshText();
    }

    /// <summary>
    /// Applies or clears the "pending pick" visual. Orange tint + underline when picked;
    /// restored to white with normal text when cleared. Underline uses TMP rich text (&lt;u&gt;),
    /// so it renders correctly regardless of font glyph support.
    /// </summary>
    public void SetPickedState(bool picked)
    {
        _isPicked = picked;
        _isHovered = false;
        choiceTextUI.color = picked ? PickedColor : Color.white;
        RefreshText();
    }

    /// <summary>
    /// Applies or clears the hover "> " prefix visually. Used by the controller navigation
    /// path in <see cref="DialogueChoiceSystem"/> to show selection without mouse input.
    /// No-ops when this choice has already been picked.
    /// </summary>
    public void SetHoverState(bool hovered)
    {
        if (_isPicked) return;
        _isHovered = hovered;
        RefreshText();
    }

    /// <summary>
    /// The plain label is the English source, translated at render time by <c>LocalizationTextRuntime</c>.
    /// Decorated labels ("&gt; " prefix, underline) wouldn't match any source entry, so they are built from the
    /// translated text and marked <see cref="Loc.Verbatim"/>; re-run on language change to stay current.
    /// </summary>
    private void RefreshText()
    {
        if (choiceTextUI == null) return;
        if (_isPicked) choiceTextUI.text = Loc.Verbatim("<u>" + Loc.TranslateSource(choiceText) + "</u>");
        else if (_isHovered) choiceTextUI.text = Loc.Verbatim("> " + Loc.TranslateSource(choiceText));
        else choiceTextUI.text = choiceText;
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHoverState(true);

    public void OnPointerExit(PointerEventData eventData) => SetHoverState(false);

    public void OnChooseChoice()
    {
        dialogueChoiceSystem.ChooseDialogueChoice(choiceIndex);
    }
}
