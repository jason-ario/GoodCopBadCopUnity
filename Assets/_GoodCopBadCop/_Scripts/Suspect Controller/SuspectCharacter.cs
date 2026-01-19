using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class SuspectCharacter : Interactable
{
    public FLookAnimator lookAnimator;
    public Animator animator;
    public AudioSource audioSource;
    public string suspectName;
    public Color suspectNameColor;
    
    [TextArea(3, 10)]
    public string entryDialogue;
    public AudioClip[] voiceAudioClips;
    public Transform lookPos;

    [Header("Dialogue")]
    public Response[] dialogueResponses; 
    [SerializeField] Collider interactionCollider;

    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }
    
    public override void Interact(PlayerInteractionController player)
    {
        DialogueManager.Instance.InitiateChoices();
    }

    public void SetCanInteract(bool b)
    {
        interactionCollider.enabled = false;
    }
}
