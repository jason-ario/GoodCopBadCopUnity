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
        vlad.animator.SetTrigger("TalkCocky");

    }
}
