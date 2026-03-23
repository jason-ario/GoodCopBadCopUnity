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
    
    [Header("Photo")]
    [SerializeField] Texture2D photoMaterial;
    public Texture2D PhotoMaterial => photoMaterial;
    
    [Header("Anomalies")]
    [SerializeField] AnomalyController anomalyController;
    public AnomalyController AnomalyController => anomalyController;

    public bool IsInfected
    {
        get
        {
            return false;
        }
    }

    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }

    [SerializeField] private ParticleSystem[] vomitParticles;
    
    // Folder giving animation
    public enum FolderGivingAnimation
    {
        HandOver,
        Throw
    }

    [System.Serializable]
    public struct FolderGivingAnimationData
    {
        public FolderGivingAnimation animation;
        public string animationTriggerName;
    }

    [SerializeField] private FolderGivingAnimationData[] folderGivingAnimationDatas;
    [SerializeField] private FolderGivingAnimation _folderGivingAnimation = FolderGivingAnimation.HandOver;
    private FolderGivingAnimationData _folderGivingAnimationData;
    [SerializeField] private Transform handSpawnPos;

    protected override void Awake()
    {
        base.Awake();
        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand);
    }

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
            base.InteractWithItem(playerInteractionController, itemData);
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
        DialogueManager.Instance.SayDialogue(this,"You.. You're a traitor!!");
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
    
    public void GivePaperwork()
    {
        StartCoroutine(GivePaperworkCoroutine());
    }
    
    IEnumerator GivePaperworkCoroutine()
    {
        animator.SetTrigger(_folderGivingAnimationData.animationTriggerName);
        
        if (_folderGivingAnimation == FolderGivingAnimation.Throw)
        {
            yield return new WaitForSeconds(.8f);
            SuspectController.Instance.SpawnAndThrowPaperwork(handSpawnPos); 
        }
        else
        {
            yield return new WaitForSeconds(1f);
            SuspectController.Instance.SpawnPaperwork();
        }
    }
    
    public void PrepareAnomalies()
    {
        anomalyController.ResetAvailableAnomalies();
    }

    public void TriggerAnomaly()
    {
        anomalyController.TriggerAnomaly();
    }
    
    public void SetFolderGivingAnimation(FolderGivingAnimation folderGivingAnimation)
    {
        foreach (var folderGivingAnimationData in folderGivingAnimationDatas)
        {
            if (folderGivingAnimationData.animation == folderGivingAnimation)
            {
                _folderGivingAnimationData = folderGivingAnimationData;
                _folderGivingAnimation = folderGivingAnimation;
            }
        }
    }
}
