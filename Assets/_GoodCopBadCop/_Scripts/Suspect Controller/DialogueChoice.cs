using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DialogueChoice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string choiceText;
    [SerializeField] private TextMeshProUGUI choiceTextUI;
    
    public void SetChoiceText(string text)
    {
        choiceText = text;
        choiceTextUI.text = text;
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        choiceTextUI.text = "> " + choiceText;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        choiceTextUI.text = choiceText;
    }
}
