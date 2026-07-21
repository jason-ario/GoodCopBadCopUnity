using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DialogueChoice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string choiceText;
    [SerializeField] private TextMeshProUGUI choiceTextUI;
    [SerializeField] DialogueChoiceSystem dialogueChoiceSystem;
    [SerializeField] int choiceIndex;
    
    private static readonly Color PickedColor = new Color(1f, 0.65f, 0f); // orange

    private bool _isPicked;

    public void SetChoiceText(string text)
    {
        choiceText = text;
        choiceTextUI.text = text;
    }

    /// <summary>
    /// Applies or clears the "pending pick" visual. Orange tint + checkmark prefix when picked;
    /// restored to white with normal text when cleared.
    /// </summary>
    public void SetPickedState(bool picked)
    {
        _isPicked = picked;
        choiceTextUI.color = picked ? PickedColor : Color.white;
        choiceTextUI.text  = picked ? "✓ " + choiceText : choiceText;
    }

    /// <summary>
    /// Applies or clears the hover "> " prefix visually. Used by the controller navigation
    /// path in <see cref="DialogueChoiceSystem"/> to show selection without mouse input.
    /// No-ops when this choice has already been picked.
    /// </summary>
    public void SetHoverState(bool hovered)
    {
        if (_isPicked) return;
        choiceTextUI.text = hovered ? "> " + choiceText : choiceText;
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHoverState(true);

    public void OnPointerExit(PointerEventData eventData) => SetHoverState(false);

    public void OnChooseChoice()
    {
        dialogueChoiceSystem.ChooseDialogueChoice(choiceIndex);
    }
}
