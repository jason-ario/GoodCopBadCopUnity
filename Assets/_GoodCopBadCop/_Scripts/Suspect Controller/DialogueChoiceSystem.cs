using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DialogueChoiceSystem : NetworkBehaviour
{
    [SerializeField] DialogueChoice[] dialogueChoices;
    [SerializeField] private GameObject dialogueChoiceContainer;
    [SerializeField] private Subtitles subtitlesPrefab;
    [SerializeField] RectTransform subtitlesContainer;

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
       CloseDialogueChoices();
        string playerName = GetPlayerName();
        
        // Request server to broadcast this choice
        ChooseDialogueChoiceServerRpc(choiceIndex, playerName);
    }
    
    private string GetPlayerName()
    {
        var transport = Unity.Netcode.NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        
        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
        {
            return Steamworks.SteamClient.Name;
        }
        else
        {
            // Fallback for LAN/Unity Transport - use a generic name or client ID
            return $"Player {Unity.Netcode.NetworkManager.Singleton.LocalClientId}";
        }
    }


    [ServerRpc(RequireOwnership = false)]
    private void ChooseDialogueChoiceServerRpc(int choiceIndex, string playerName)
    {
        SpawnPlayerSubtitleClientRpc(dialogueChoices[choiceIndex].choiceText, playerName);
        StartCoroutine(NPCRespondToDialogueChoice(choiceIndex));
    }

    [ClientRpc]
    private void SpawnPlayerSubtitleClientRpc(string choiceText, string playerName)
    {
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.darkCyan, true);
    }
    
    IEnumerator NPCRespondToDialogueChoice(int choiceIndex)
    {
        yield return new WaitForSeconds(1);
        SuspectController.Instance.RespondToDialogueChoice(choiceIndex);
    }
    
    public void CloseDialogueChoices()
    {
        dialogueChoiceContainer.SetActive(false);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}