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

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_isPicked) return;
        choiceTextUI.text = "> " + choiceText;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_isPicked) return;
        choiceTextUI.text = choiceText;
    }

    public void OnChooseChoice()
    {
        dialogueChoiceSystem.ChooseDialogueChoice(choiceIndex);
    }
}
