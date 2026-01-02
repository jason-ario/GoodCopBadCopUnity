using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class SuspectCharacter : NetworkBehaviour
{
    public FLookAnimator lookAnimator;
    public Animator animator;
    public AudioSource audioSource;
    [TextArea(3, 10)]
    public string entryDialogue;
    public AudioClip[] voiceAudioClips;
}
