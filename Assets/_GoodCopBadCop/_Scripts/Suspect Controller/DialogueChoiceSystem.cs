using UnityEngine;

public class DialogueChoiceSystem : MonoBehaviour
{
    [SerializeField] DialogueChoice[] dialogueChoices;
    [SerializeField] private GameObject dialogueChoiceContainer;
    
    public void StartDialogueChoices()
    {
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
        dialogueChoiceContainer.SetActive(true);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(SuspectController.Instance.suspectCharacter.lookPos);
        InitializeChoices();
    }
    
    private void InitializeChoices()
    {
        dialogueChoices[0].SetChoiceText("State your reason for crossing.");
        dialogueChoices[1].SetChoiceText("What were you doing during the blast?");
        dialogueChoices[2].SetChoiceText("Show me your hands.");
    }

    public void ChooseDialogueChoice(int choiceIndex)
    {
        dialogueChoiceContainer.SetActive(false);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}
