using System;
using System.Collections;
using System.Collections.Generic;
using FIMSpace.FLook;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using Random = System.Random;


public class SuspectCharacter : Interactable
{
    [Header("Suspect Data")] [SerializeField]
    private SuspectData suspectData;

    public SuspectData Data => suspectData;
    public string ExpirationDate => suspectData.EntryPermitExpiryDate;

    [Header("Suspect State")] public int InfectionScore;

    [Header("Suspect Set Up")] public FLookAnimator lookAnimator;
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
    SuspectRecordViewer suspectRecordViewer;


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

    [Header("Anomalies")] [SerializeField] private AnomalyController anomalyController;
    public AnomalyController AnomalyController => anomalyController;

    //Responses
    public int ChosenEntryReasonIndex = -1;
    public int ChosenSymptomResponseIndex = -1;
    public int ChosenWhoDoYouLiveWithIndex = -1;

    public int radiationAmount = 10;
    private Vector2 radiationNormal = new Vector2(0, 30);
    private Vector2 radiationSuspicious = new Vector2(31, 70);
    private Vector2 radiationInfected = new Vector2(71, 100);

    public string[] defaultDialogueChoices = new string[]
    {
        "Where are you coming from?",
        "Have you been experiencing any strange symptoms lately?",
        "Who do you live with?"
    };

    private string[] choices;
    
    public string[] defaultDialogueResponses = new string[]
    {
        "Where are you coming from?", 
        "Have you been experiencing any strange symptoms lately?", 
        "Who do you live with?"
    };  


    protected override void Awake()
    {
        base.Awake();
        choices = defaultDialogueChoices;
        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand); 
        suspectRecordViewer = GetComponent<SuspectRecordViewer>(); 
        
        if (folderGivingAnimationDatas != null && folderGivingAnimationDatas.Length > 0)
        {
            _folderGivingAnimationData = folderGivingAnimationDatas[0];
        }
    }

    public void Initialize()
    {
        anomalyController.Initialize();
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
        {
            suspectRecordViewer.SetRecord(record);
        }
        else
        {
            Debug.Log("No record found for " + suspectData.name);
        }
        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);
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
        
        // Get entry dialogues
        SuspectData.DialogueByVerdict dialogueByVerdict = suspectData.entryDialogues;
        
        //Second: Get the day band, 1-10, 11-20, 21-30 etc
        int dayN0 = ShiftManager.Instance.CurrentDay;
        string[] entryDialogues;
        
        if (dayN0 < 11)
        {
            entryDialogues = dialogueByVerdict.dialoguesEarlyDays;
        } else if (dayN0 < 21)
        {
            entryDialogues = dialogueByVerdict.dialoguesMidDays;
        }
        else
        {
            entryDialogues = dialogueByVerdict.dialoguesFinalDays;
        }

        return entryDialogues[UnityEngine.Random.Range(0, entryDialogues.Length)];
    }
}