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

    [Header("Stamps")] 
    [SerializeField] private InkStamp greenStamp;
    [SerializeField] private InkStamp yellowStamp;
    [SerializeField] private InkStamp redStamp;

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

    //TUTORIAL PART 1
    IEnumerator StartIntro()
    {
        vlad.gameObject.SetActive(true);
        vlad.GetComponent<FLookAnimator>().SetLookTarget(Camera.main.transform);
        yield return new WaitForSeconds(4f);

        rollingShutter.SetBool("Open", true);
        yield return new WaitForSeconds(3f);

        yield return new DialogueSequence()
            .Say(vlad, "So you're the replacement, huh?", clearHistory: true, waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkSlightSurprise"))
            .Say(vlad, "You're even skinnier and more pathetic than the last guy", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkDismissing"))
            .Say(vlad, "And he didn't last very long.", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkShrug"))
            .Say(vlad, "I give you a week. Maybe two if you're lucky.", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkLookAway"))
            .Say(vlad, "Anyway. We've got work to do.", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkCocky"))
            .Say(vlad, "Town still needs supplies and someone has to keep the infected out", waitForInput: true)
            .Say(vlad, "The system is simple. Follow protocol.", waitForInput: true,
                onShow: () => vlad.GivePaperwork())
            .Say(vlad, "First, coffee breaks over. Put down that coffee and grab this folder, will ya?",
                waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkCocky"))
            .Play();

        TutorialUIManager.Instance.SetTutorialText("Hold <sprite=1> to put down the coffee.");
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject += PutDownCoffee;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = true;
    }

    //TUTORIAL PART 2
    void PutDownCoffee()
    {
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = false;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject -= PutDownCoffee;
        PlayerInstance.Instance.SetCanInteract(false);
        TutorialUIManager.Instance.HideTutorialText();
        StartCoroutine(GrabFolderTutorial());
    }

    //TUTORIAL PART 3
    IEnumerator GrabFolderTutorial()
    {
        yield return new WaitForSeconds(.5f);
        TutorialUIManager.Instance.SetTutorialText("Grab the folder with <sprite=0>");
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        PlayerInstance.Instance.SetCanInteract(true);
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = folderController;
        folderController.OnInteract += OnGrabbedFolder;
    }
    
    //TUTORIAL PART 4
    void OnGrabbedFolder()
    {
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = null;
        TutorialUIManager.Instance.HideTutorialText();
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        folderController.OnInteract -= OnGrabbedFolder;
        StartCoroutine(PassTutorial());
    }

    //TUTORIAL PART 5
    IEnumerator PassTutorial()
    {
        yield return new DialogueSequence()
            .Say(vlad, "Three options", clearHistory: true, waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkPoint"))
            .Say(vlad, "Pass. Quarantine. Or Kill", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkPoint"))
            .Say(vlad, "You decide who gets through based on whether they're infected...", waitForInput: true)
            .Say(vlad, "Or about to be.", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkDismissing"))
            .Say(vlad, "We don't want them spreading that sickness inside town. Obviously.", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkShrug"))
            .Say(vlad, "If someone is healthy, and their mind is still in one piece…", waitForInput: true)
            .Say(vlad, "You let them through", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkPoint"))
            .Say(vlad, "Simple as that", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkShrug"))
            .Say(vlad, "Go ahead. Stamp me green", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkShrug"))
            .Say(vlad, "Feeling healthier than an ox today", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkBigCocky"))
            .Say(vlad, "ha ha ha ha", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("TalkBigLaugh"))
            .Play();
        
        yield return new WaitForSeconds(.5f);
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = true;
        PlayerInstance.Instance.SetCanInteract(true);
        TutorialUIManager.Instance.SetTutorialText("Grab the green stamp with <sprite=0>");
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = greenStamp;
        greenStamp.OnInteract += OnGrabbedGreenStamp;
    }

    public void OnGrabbedGreenStamp()
    {
        greenStamp.OnInteract -= OnGrabbedGreenStamp;
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = folderController;
        folderController.onStampedComplete += OnStampedFolderGreen;
        TutorialUIManager.Instance.HideTutorialText();
        TutorialUIManager.Instance.SetTutorialText("Stamp the folder with <sprite=0>");
    }

    public void OnStampedFolderGreen()
    {
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = null;
        TutorialUIManager.Instance.HideTutorialText();
        StartCoroutine(StampedGreen());
    }

    IEnumerator StampedGreen()
    {
        yield return new WaitForSeconds(1f);
        yield return new DialogueSequence()
            .Say(vlad, "Look at that,", clearHistory: true, waitForInput: true, 
                onShow: () => vlad.animator.SetTrigger("TalkSarcasticNod"))
            .Say(vlad, "You can perform basic motor functions", waitForInput: true,
                onShow: () => vlad.animator.SetTrigger("Give"))
            .Play();

        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        NetworkHelper.DespawnWithChildren(folderController.GetComponent<NetworkObject>());
        yield return new WaitForSeconds(.5f);
        
        yield return new WaitForSeconds(.5f);
    }
}
