using System;
using System.Collections;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class IntroTutorialManager : MonoBehaviour
{
    [SerializeField] private Chair[] chairs;
    [SerializeField] private Chair singlePlayerChair;
    [SerializeField] private PickableItemData coffee;

    [SerializeField] private Animator rollingShutter;
    [SerializeField] private SuspectCharacter vlad;

    public void StartIntroTutorial()
    {
        //Force player into seat
        int chairIndex = (int)PlayerInstance.Instance.OwnerClientId;
        Chair chair = chairs[chairIndex];
        
        bool isSinglePlayer = GameManager.Instance.IsSinglePlayer;
        if (isSinglePlayer) chair = singlePlayerChair;

        chair.gameObject.SetActive(true);
        
        foreach (var c in chairs)
        {
            c.gameObject.SetActive(!isSinglePlayer);
        }
        
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = false;
        PlayerInstance.Instance.transform.position = chair.SitPos.position;
        PlayerInstance.Instance.transform.rotation = chair.SitPos.rotation;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().PickUpObject(coffee);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCantSitOrStand(false);
        chair.SitImmediate(PlayerInstance.Instance.GetComponent<PlayerInteractionController>());
        
        StartCoroutine(StartIntro());
    }

    IEnumerator StartIntro()
    {
        vlad.gameObject.SetActive(true);
        vlad.GetComponent<FLookAnimator>().SetLookTarget(Camera.main.transform);
        yield return new WaitForSeconds(4f);
        rollingShutter.SetBool("Open", true);
        yield return new WaitForSeconds(3f); 
        DialogueManager.Instance.SayDialogue(vlad, "So you’re the replacement, huh?", true);
        vlad.animator.SetTrigger("TalkSlightSurprise");
        yield return new WaitForSeconds(4f); 
        DialogueManager.Instance.SayDialogue(vlad, "You're even skinnier and more pathetic than the last guy", true);
        vlad.animator.SetTrigger("TalkDismissing");
        yield return new WaitForSeconds(5f); 
        DialogueManager.Instance.SayDialogue(vlad, "And he didn’t last very long.", true);
        vlad.animator.SetTrigger("TalkShrug");
        yield return new WaitForSeconds(3f); 
        DialogueManager.Instance.SayDialogue(vlad, "I give you a week. Maybe two if you’re lucky.", true);
        vlad.animator.SetTrigger("TalkLookAway");
        yield return new WaitForSeconds(4f); 
        DialogueManager.Instance.SayDialogue(vlad, "Anyway. We’ve got work to do.", true);
        vlad.animator.SetTrigger("TalkCocky");
        yield return new WaitForSeconds(3f); 
        DialogueManager.Instance.SayDialogue(vlad, "Town still needs supplies and someone has to keep the infected out", true);
        yield return new WaitForSeconds(3f); 
        vlad.GivePaperwork();
        DialogueManager.Instance.SayDialogue(vlad, "The system is simple. Follow protocol.", true);
        yield return new WaitForSeconds(3f); 
        vlad.animator.SetTrigger("TalkCocky");
        DialogueManager.Instance.SayDialogue(vlad, "First, coffee breaks over. Put down that coffee and grab this folder, will ya?", true);
        yield return new WaitForSeconds(5f);
        TutorialUIManager.Instance.SetTutorialText("Hold <sprite=1> to put down the coffee.");
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject += PutDownCoffee;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = true;
        PlayerInstance.Instance.SetCanInteract(true);
    }

    void PutDownCoffee()
    {
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject -= PutDownCoffee;
        TutorialUIManager.Instance.HideTutorialText();
        StartCoroutine(GrabFolderTutorial());
    }

    IEnumerator GrabFolderTutorial()
    {
        yield return new WaitForSeconds(.5f);
        TutorialUIManager.Instance.SetTutorialText("Grab the folder with <sprite=0>");
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = folderController;
        folderController.OnInteract += OnGrabbedFolder;
    }
    
    void OnGrabbedFolder()
    {
        TutorialUIManager.Instance.HideTutorialText();
    }
}
