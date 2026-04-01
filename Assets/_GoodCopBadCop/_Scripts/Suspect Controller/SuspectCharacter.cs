using System;
using System.Collections;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;

public enum CharacterStatus
{
    Resident,
    Visitor,
    Deceased
}

public class SuspectCharacter : Interactable
{
    [Header("Suspect Data")]
    [SerializeField] private SuspectData suspectData;
    public SuspectData Data => suspectData;
    
    [SerializeField] string reasonForEntry;
    [SerializeField] string expirationDate;
    [SerializeField] bool sealActive;
    
    public string ReasonForEntry => reasonForEntry;
    public string ExpirationDate=> expirationDate;
    public bool SealActive => sealActive;
    
    [Header("Character State")]
    public CharacterStatus characterStatus;
    
    [Header("Suspect Set Up")]
    public FLookAnimator lookAnimator;
    public Animator animator;
    public AudioSource audioSource;
    [SerializeField] Texture2D idPhoto;
    public Texture2D IDPhoto => idPhoto;
    [SerializeField] Collider interactionCollider;
    public bool givesFolder = true;
    [SerializeField] private GameObject bloodExplosion;
    public Transform lookPos;
    public Vector3 standPosOffset;
    public bool attackImmediately;
    [SerializeField] private ParticleSystem[] vomitParticles;

    #region Folder
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
    #endregion 
    
    private bool _facingPlayer;

    [Header("Anomalies")]
    [SerializeField] AnomalyController anomalyController;
    public AnomalyController AnomalyController => anomalyController;

    public bool IsInfected => false;

    protected override void Awake()
    {
        base.Awake();
        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand);
        _folderGivingAnimationData = folderGivingAnimationDatas[0];
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
        _facingPlayer = true;
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
        if (_facingPlayer)
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
