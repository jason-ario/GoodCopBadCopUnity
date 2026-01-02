using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public class SuspectCharacter : NetworkBehaviour
{
    public FLookAnimator lookAnimator;
    public Animator animator;
    public SuspectData suspectData;
    public AudioSource audioSource;
}
