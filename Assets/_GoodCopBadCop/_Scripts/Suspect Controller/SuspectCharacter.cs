using System;
using System.Collections;
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

    public bool givesFolder = true;

    [SerializeField] private GameObject bloodExplosion;
    public bool attackImmediately;
    private bool facingPlayer;
    public Vector3 standPosOffset;
    
    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }

    [SerializeField] private ParticleSystem[] vomitParticles;
    
    public override void Interact(PlayerInteractionController player)
    {
        DialogueManager.Instance.InitiateChoices();
    }

    public void SetCanInteract(bool b)
    {
        interactionCollider.enabled = false;
    }
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData itemData)
    {
        if (itemData == null)
        {
            DialogueManager.Instance.InitiateChoices();
        }

        if (itemData.name == "Shotgun")
        {
            GetShot();
        }
    }

    public void GetShot()
    {
        if (NetworkManager.Singleton.IsClient)
        {
            GetShotServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void GetShotServerRpc()
    {
        GetShotClientRpc();
    }

    [ClientRpc]
    private void GetShotClientRpc()
    {
        bloodExplosion.SetActive(true);
        animator.SetTrigger("Die");
    }
    public void AimAtPlayer()
    {
        StartCoroutine(StartFiring());
    }

    public void StartVomiting()
    {
        foreach (var vomitParticle in vomitParticles)
        {
            vomitParticle.Play();
        }
    }

    public void StopVomiting()
    {
        foreach (var vomitParticle in vomitParticles)
        {
            vomitParticle.Stop();
        }
    }

    IEnumerator StartFiring()
    {
        facingPlayer = true;
        yield return new WaitForSeconds(1);
        animator.SetBool("Aiming Rifle", true);
        DialogueManager.Instance.SayDialogue("You.. You're a traitor!!");
        yield return new WaitForSeconds(2);
        animator.SetBool("FiringRifle", true);

        while (true)
        {
            PlayerInstance.Instance.HurtPlayer();
            yield return new WaitForSeconds(.5f);
        }

        yield break;
    }

    private void Update()
    {
        if (facingPlayer)
        {
            Vector3 targetPosition = PlayerInstance.Instance.transform.position;
            targetPosition.y = transform.position.y; // Keep the target at the same height
            transform.LookAt(targetPosition);
        }
    }
}
