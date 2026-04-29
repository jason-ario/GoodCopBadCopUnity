using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DialogueChoiceSystem : NetworkBehaviour
{
    [SerializeField] DialogueChoice[] dialogueChoices;
    [SerializeField] private GameObject dialogueChoiceContainer;
    [SerializeField] private Subtitles subtitlesPrefab;
    [SerializeField] RectTransform subtitlesContainer;

    public void StartDialogueChoices(Transform lookTarget, string[] choices)
    {
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
        UIController.Instance.ShowCursor();
        dialogueChoiceContainer.SetActive(true);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(lookTarget);
        InitializeChoices(choices);
    }
    
    private void InitializeChoices(string[] choices)
    {
        for (var i = 0; i < choices.Length; i++)
        {
            dialogueChoices[i].SetChoiceText(choices[i]);
        }
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
    private void ChooseDialogueChoiceServerRpc(int choiceIndex, string playerName, ServerRpcParams serverRpcParams = default)
    {
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;
        SpawnPlayerSubtitleClientRpc(dialogueChoices[choiceIndex].choiceText, playerName, senderClientId);
        StartCoroutine(NPCRespondToDialogueChoice(choiceIndex));
    }

    [ClientRpc]
    private void SpawnPlayerSubtitleClientRpc(string choiceText, string playerName, ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;

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
        UIController.Instance.HideCursor();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}