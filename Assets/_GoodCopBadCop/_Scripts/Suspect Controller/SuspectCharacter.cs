using System;
using System.Collections;
using System.Collections.Generic;
using FIMSpace.FLook;
using Unity.Netcode;
using UnityEngine;
using Random = System.Random;

public enum CharacterStatus
{
    Resident,
    Visitor,
    Quarantined,
    Deceased
}

public class SuspectCharacter : Interactable
{
    [Header("Suspect Data")]
    [SerializeField] private SuspectData suspectData;
    public SuspectData Data => suspectData;
    
    [SerializeField] bool sealActive;
    
    public string ReasonForEntry => suspectData.reasonsForEntry[UnityEngine.Random.Range(0, suspectData.reasonsForEntry.Length)];
    public string ExpirationDate => suspectData.EntryPermitExpiryDate;
    public bool SealActive => sealActive;
    
    [Header("Character State")]
    public CharacterStatus characterStatus;

    [Header("Runtime Record")]
    [SerializeField] private bool autoInitializeFromDatabase = true;
    private SuspectRecord _record;
    public SuspectRecord Record => _record;
    
    [Header("Suspect Set Up")]
    public FLookAnimator lookAnimator;
    public Animator animator;
    public AudioSource audioSource;
    [SerializeField] Texture2D idPhoto;
    public Texture2D IDPhoto => idPhoto;
    [SerializeField] Collider interactionCollider;
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
    [SerializeField] private AnomalyController anomalyController;
    public AnomalyController AnomalyController => anomalyController;

    public bool IsInfected => _record != null && _record.InfectionScore >= 50;
    public int InfectionScore => _record != null ? _record.InfectionScore : 0;
    
    string[] choices = new string[]
    {
        "State your reason for crossing.", 
        "What were you doing during the blast?", 
        "Show me your hands."
    };  


    protected override void Awake()
    {
        base.Awake();

        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand);

        if (folderGivingAnimationDatas != null && folderGivingAnimationDatas.Length > 0)
        {
            _folderGivingAnimationData = folderGivingAnimationDatas[0];
        }
    }

    private void Start()
    {
        if (autoInitializeFromDatabase && suspectData != null && SuspectDatabase.Instance != null)
        {
            InitializeFromDatabase();
        }
    }

    public void InitializeFromDatabase()
    {
        SuspectRecord record = SuspectDatabase.Instance.GetRecord(suspectData);
        Initialize(record);
    }

    public void Initialize(SuspectRecord record)
    {
        _record = record;

        if (_record == null)
        {
            Debug.LogError($"SuspectCharacter '{name}' initialized with null record.");
            return;
        }

        suspectData = _record.Data;
        characterStatus = _record.Status;

        ApplyRecordData();
        ApplyInfectionState();
    }

    private void ApplyRecordData()
    {
        if (_record == null)
            return;

        characterStatus = _record.Status;
    }

    private void ApplyInfectionState()
    {
        if (_record == null)
            return;

        if (anomalyController == null)
        {
            Debug.LogWarning($"SuspectCharacter '{name}' has no AnomalyController assigned.");
            return;
        }

        anomalyController.GenerateAndApplyAnomalies(_record.InfectionScore);
    }

    public override void Interact(PlayerInteractionController player)
    {
      
        DialogueManager.Instance.InitiateChoices(lookPos, choices);
    }

    public void SetCanInteract(bool canInteract)
    {
        if (interactionCollider != null)
            interactionCollider.enabled = canInteract;
    }
    
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        if (item == null)
        {
            
            DialogueManager.Instance.InitiateChoices(lookPos, choices);
            return;
        }

        if (item.ItemData.name == "Shotgun")
        {
            base.InteractWithItem(playerInteractionController, item);
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
        if (bloodExplosion != null)
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
        DialogueManager.Instance.SayDialogue(this, "You.. You're a traitor!!");
        yield return new WaitForSeconds(2);
        animator.SetBool("FiringRifle", true);

        while (true)
        {
            PlayerInstance.Instance.HurtPlayer();
            yield return new WaitForSeconds(.5f);
        }
    }

    private void Update()
    {
        if (_facingPlayer)
        {
            Vector3 targetPosition = PlayerInstance.Instance.transform.position;
            targetPosition.y = transform.position.y;
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
        yield return new WaitForSeconds(1f);
        SuspectController.Instance.SpawnPaperwork();
    }
    public void RegenerateAnomaliesFromCurrentScore()
    {
        if (_record == null || anomalyController == null)
            return;

        anomalyController.GenerateAndApplyAnomalies(_record.InfectionScore);
    }
    
    public void SetFolderGivingAnimation(FolderGivingAnimation folderGivingAnimation)
    {
        foreach (var folderGivingAnimationData in folderGivingAnimationDatas)
        {
            if (folderGivingAnimationData.animation == folderGivingAnimation)
            {
                _folderGivingAnimationData = folderGivingAnimationData;
                _folderGivingAnimation = folderGivingAnimation;
                return;
            }
        }
    }
    
    public string GetEntryDialogue()
    {
        string entryDialogue = "";
        if(InfectionScore >= 50)
        {
            entryDialogue = suspectData.anomalyEntryDialogues[
                UnityEngine.Random.Range(0, suspectData.anomalyEntryDialogues.Length)];
        }
        else
        {
            entryDialogue =
                suspectData.anomalyEntryDialogues[
                    UnityEngine.Random.Range(0, suspectData.entryDialogues.Length)];
        }

        return entryDialogue;
    }
}