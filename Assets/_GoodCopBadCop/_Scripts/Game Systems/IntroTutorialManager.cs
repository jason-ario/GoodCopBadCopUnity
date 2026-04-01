using System;
using System.Collections;
using DG.Tweening;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class IntroTutorialManager : MonoBehaviour
{
    [SerializeField] private Chair[] chairs;
    [SerializeField] private Chair singlePlayerChair;
    [SerializeField] private PickableObject coffee;

    [Header("Characters")] 
    [SerializeField] private SuspectCharacter vlad;
    [SerializeField] private SuspectCharacter[] guards;
    [SerializeField] private GameObject guardsContainer;

    [Header("Stamps")] 
    [SerializeField] private InkStamp greenStamp;
    [SerializeField] private InkStamp yellowStamp;
    [SerializeField] private InkStamp redStamp;

    [Header("Environment")] 
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private Animator gate1;
    [SerializeField] private Animator gate2;
    [SerializeField] private Animator door;

    [SerializeField] private AudioClip introScream;

    public enum StartingSection
    {
        Beginning,
        GuardQuarantine
    }
    
    [SerializeField] private StartingSection startingSection;
    
    [System.Serializable]
    public struct MovementSequence
    {
        public Transform[] positions;
    }

    [SerializeField] private MovementSequence vladMovementSequence;
    [SerializeField] private MovementSequence guardMovementSequence;

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
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCantSitOrStand(false);
        chair.SitImmediate(PlayerInstance.Instance.GetComponent<PlayerInteractionController>());

        if (startingSection == StartingSection.GuardQuarantine)
        {
            SkipToQuarantineTutorial();
        }
        else
        {
            PlayerInstance.Instance.GetComponent<PlayerPickupController>().PickUpObject(coffee);
            StartCoroutine(StartIntro());
        }
    }

    //TUTORIAL PART 1
    IEnumerator StartIntro()
    {
        //SFXController.Instance.Play(introScream,.5f);
        //yield return new WaitForSeconds(18);
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
            .Say(vlad, "First, coffee breaks over. Put that down and grab this folder, will ya?",
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
        PlayerInstance.Instance.SetCanInteract(true);
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = folderController;
        folderController.OnInteract += OnGrabbedFolder;
    }

    void OnGrabbedFolder()
    {
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        folderController.OnInteract -= OnGrabbedFolder;
        PlayerInstance.Instance.SetCanInteract(false);
        TutorialUIManager.Instance.SetTutorialText("Hold <sprite=1> to put down the folder.");
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject += OnPutDownFolder;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = true;
    }
    
    void OnPutDownFolder()
    {
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = false;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject -= OnPutDownFolder;
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = null;
        TutorialUIManager.Instance.HideTutorialText();
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

        PlayerInstance.Instance.SetCanInteract(true);
        TutorialUIManager.Instance.SetTutorialText("Place the stamp back on the ink pad with <sprite=0>");
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = greenStamp;
        greenStamp.OnInteractWithItem += PlacedStampBack;
    }

    public void PlacedStampBack()
    {
        TutorialUIManager.Instance.HideTutorialText();
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = null;
        greenStamp.OnInteract -= PlacedStampBack;
        StartCoroutine(VladMoveToBoothSequence());
        PlayerInstance.Instance.SetCanInteract(false);
    }

    IEnumerator VladMoveToBoothSequence()
    {
        yield return new WaitForSeconds(.5f);
        rollingShutter.SetBool("Open", false);
        yield return new WaitForSeconds(.5f);
        
        Sequence vladMovement = DOTween.Sequence();
        vladMovement.Append(vlad.transform.DORotate(vladMovementSequence.positions[0].rotation.eulerAngles, .25f));
        vladMovement.AppendCallback(() => vlad.animator.SetBool("Walking", true));
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[0].position, .5f)).OnComplete(() => vlad.animator.SetTrigger("OpenDoorInwards"));
        vladMovement.AppendCallback(() => vlad.animator.SetBool("Walking", false));
        vladMovement.AppendCallback(() => gate1.SetBool("Open", true));
        vladMovement.AppendInterval(.125f);
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[1].position, .5f));
        vladMovement.AppendInterval(.25f);
        vladMovement.AppendCallback(() => gate1.SetBool("Open", false));
        vladMovement.AppendInterval(.125f);
        vladMovement.Append(vlad.transform.DORotate(vladMovementSequence.positions[2].rotation.eulerAngles, .25f)).OnComplete(() => vlad.animator.SetTrigger("OpenDoorInwards"));
        vladMovement.AppendCallback(() => gate2.SetBool("Open", true));
        vladMovement.AppendInterval(.125f);
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[2].position, .5f));
        vladMovement.AppendCallback(() => gate2.SetBool("Open", false));
        vladMovement.AppendInterval(.25f);
        vladMovement.AppendCallback(() => vlad.animator.SetBool("Walking", true));
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[3].position, 1)).OnComplete(() => vlad.animator.SetTrigger("OpenDoorInwards"));
        vladMovement.Join(vlad.transform.DORotate(vladMovementSequence.positions[3].rotation.eulerAngles, 1));
        vladMovement.AppendCallback(() => door.SetBool("OpenedIn", true));
        vladMovement.AppendInterval(.25f);
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[4].position, .5f));
        vladMovement.Append(vlad.transform.DOMove(vladMovementSequence.positions[5].position, .5f)).OnComplete(() => vlad.animator.SetTrigger("OpenDoorInwards"));
        vladMovement.AppendCallback(() => door.SetBool("OpenedIn", false));
        vladMovement.Join(vlad.transform.DORotate(vladMovementSequence.positions[5].rotation.eulerAngles, .5f));
        vladMovement.AppendCallback(() => vlad.animator.SetBool("Walking", false)).OnComplete(VladEntersBooth);
    }

    void VladEntersBooth()
    {
        StartCoroutine(VladEntersBoothSequence());
    }
    
    public void SkipToQuarantineTutorial()
    {
        StartCoroutine(VladEntersBoothSequence());   
    }
    IEnumerator VladEntersBoothSequence()
    {
        vlad.GetComponent<FLookAnimator>().SetLookTarget(Camera.main.transform);
        guards[0].GetComponent<FLookAnimator>().SetLookTarget(Camera.main.transform);
        vlad.gameObject.SetActive(true);
        vlad.transform.position = vladMovementSequence.positions[5].position;
        vlad.transform.rotation = vladMovementSequence.positions[5].rotation;
        yield return new DialogueSequence()
            .Say(vlad, "“Now let’s talk about people who are… less than healthy.", clearHistory: true, waitForInput: true, 
                onShow: () => vlad.animator.SetTrigger("TalkLookAway"))
            .Play();
        
        rollingShutter.SetBool("Open", true);
        guardsContainer.SetActive(true);
        foreach (var character in guardsContainer.GetComponentsInChildren<SuspectCharacter>())
        {
            character.GetComponent<NetworkObject>().Spawn();
        }

        yield return new WaitForSeconds(2f);
        StartQuarantineTutorial();
    }

    void StartQuarantineTutorial()
    {
        StartCoroutine(GuardCoughsSequence());
    }

    IEnumerator GuardCoughsSequence()
    {
        yield return new WaitForSeconds(1f);
        guards[0].GivePaperwork();
        yield return new WaitForSeconds(2f);
        yield return new DialogueSequence()
            .Say(guards[0], "COUGH COUGH", clearHistory: true, waitForInput: true,
                onShow: () => guards[0].animator.SetTrigger("FakeCough"))
            .Say(guards[0], "Ohhh nooo…", clearHistory: true, waitForInput: true)
            .Say(guards[0], "I don't feel so good...", clearHistory: true, waitForInput: true)
            .Play();

        for (int i = 1; i < 3; i++)
        {
            guards[i].animator.SetTrigger("ShortLaugh");
        }
        
        vlad.animator.SetTrigger("TalkPoint");
        
        yield return new DialogueSequence()
            .Say(vlad, "When someone shows symptoms of radiation sickness…", clearHistory: true, waitForInput: true,
                onShow: () => guards[0].animator.SetTrigger("FakeCough"))
            .Say(vlad, "They go to quarantine", clearHistory: true, waitForInput: true)
            .Say(vlad, "No exceptions", clearHistory: true, waitForInput: true)
            .Say(vlad, "Use the yellow stamp.", clearHistory: true, waitForInput: true)
            .Play();
        
        PlayerInstance.Instance.SetCanInteract(true);

        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = folderController;
        folderController.OnInteract += OnPickedUpQuarantineFolder;
        TutorialUIManager.Instance.SetTutorialText("Stamp the folder with the YELLOW stamp");

    }

    void OnPickedUpQuarantineFolder()
    {
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        folderController.OnInteract -= OnPickedUpQuarantineFolder;
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject += OnPutQuarantineFolderDown;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = true;
    }
    
    void OnPutQuarantineFolderDown(){
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().OnPlaceObject -= OnPutQuarantineFolderDown;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().CanPickUpAndPlace = false;

        TutorialUIManager.Instance.SetTutorialText("Grab the yellow stamp with <sprite=0>");

        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        folderController.onStampedComplete += OnStampedFolderYellow;
        
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = yellowStamp;

    }
    
    public void OnStampedFolderYellow()
    {
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = null;
        StartCoroutine(StampedYellow());
    }
    
    IEnumerator StampedYellow()
    {
        yield break;
        
        FolderController folderController = GameObject.FindAnyObjectByType<FolderController>();
        folderController.onStampedComplete -= OnStampedFolderYellow;
        PlayerInstance.Instance.SetCanInteract(false);

        NetworkHelper.DespawnWithChildren(folderController.GetComponent<NetworkObject>());

        TutorialUIManager.Instance.SetTutorialText("Place the stamp back on the ink pad with <sprite=0>");
        PlayerInstance.Instance.GetComponent<PlayerInteractionController>().onlyAllowedInteractable = yellowStamp;
        yellowStamp.OnInteractWithItem += PlacedStampBack;
        TutorialUIManager.Instance.HideTutorialText();
    }

}
