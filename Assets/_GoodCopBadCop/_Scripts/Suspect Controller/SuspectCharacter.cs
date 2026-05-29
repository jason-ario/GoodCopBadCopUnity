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
    [SerializeField] private SpeakingInteraction speaking;
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

    /// <summary>Returns true if this suspect has at least one active anomaly.</summary>
    public bool IsInfected => anomalyController != null && anomalyController.activeAnomalies.Count > 0;

    //Responses
    public int ChosenEntryReasonIndex = -1;
    public int ChosenSymptomResponseIndex = -1;
    public int ChosenWhoDoYouLiveWithIndex = -1;

    public int radiationAmount = 10;
    private Vector2 radiationNormal = new Vector2(0, 30);
    private Vector2 radiationSuspicious = new Vector2(31, 70);
    private Vector2 radiationInfected = new Vector2(71, 100);

    /// <summary>The SpeakingInteraction component that handles networked speech and dialogue choices.</summary>
    public SpeakingInteraction Speaking => speaking;


    protected override void Awake()
    {
        base.Awake();
        handSpawnPos = animator.GetBoneTransform(HumanBodyBones.RightHand); 
        suspectRecordViewer = GetComponent<SuspectRecordViewer>(); 
        
        if (folderGivingAnimationDatas != null && folderGivingAnimationDatas.Length > 0)
        {
            _folderGivingAnimationData = folderGivingAnimationDatas[0];
        }

        if (suspectData != null)
            interactText = $"{suspectData.FirstName}";
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

        // Relay server-chosen tentacle indices to clients so they show the same
        // tentacles without running independent RNG.
        foreach (var kvp in anomalyController.TentacleAnomalyIndices)
            SyncTentacleAnomalyClientRpc(kvp.Key, kvp.Value);

        // Relay server-chosen tumor indices to clients so they show the same
        // tumors without running independent RNG.
        foreach (var kvp in anomalyController.TumorAnomalyIndices)
            SyncTumorAnomalyClientRpc(kvp.Key, kvp.Value);

        // Relay InitializeDisabled calls to clients for shader-driven anomalies
        // (e.g. lesions, black eyes, blue veins) that don't carry index data.
        foreach (int siblingIndex in anomalyController.DisabledAnomalySiblingIndices)
            SyncInitializeDisabledClientRpc(siblingIndex);
    }

    /// <summary>
    /// Initializes the suspect with exactly <paramref name="count"/> anomalies chosen from the
    /// currently unlocked pool. The clean-chance roll is bypassed. Used for tutorial suspects
    /// that must always exhibit a specific number of anomalies.
    /// </summary>
    /// <param name="count">Exact number of anomalies to force.</param>
    public void InitializeWithExactAnomalyCount(int count)
    {
        anomalyController.InitializeWithExactAnomalyCount(count);

        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
            suspectRecordViewer.SetRecord(record);
        else
            Debug.Log("No record found for " + suspectData.name);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        foreach (var kvp in anomalyController.TentacleAnomalyIndices)
            SyncTentacleAnomalyClientRpc(kvp.Key, kvp.Value);

        foreach (var kvp in anomalyController.TumorAnomalyIndices)
            SyncTumorAnomalyClientRpc(kvp.Key, kvp.Value);

        // Relay InitializeDisabled calls to clients for shader-driven anomalies
        // (e.g. lesions, black eyes, blue veins) that don't carry index data.
        foreach (int siblingIndex in anomalyController.DisabledAnomalySiblingIndices)
            SyncInitializeDisabledClientRpc(siblingIndex);
    }

    /// <summary>
    /// Initializes the suspect with no anomalies. Used for tutorial suspects that must
    /// be clean regardless of the anomaly distribution settings.
    /// </summary>
    public void InitializeClean()
    {
        anomalyController.InitializeClean();
        SuspectRecord record = SuspectRunRecords.Instance.GetRecord(suspectData);
        if (record != null)
            suspectRecordViewer.SetRecord(record);
        else
            Debug.Log("No record found for " + suspectData.name);

        ChosenEntryReasonIndex = UnityEngine.Random.Range(0, 2);
        ChosenSymptomResponseIndex = UnityEngine.Random.Range(0, 2);
        ChosenWhoDoYouLiveWithIndex = UnityEngine.Random.Range(0, 2);

        // Relay InitializeDisabled calls to clients for all anomalies (suspect is clean).
        foreach (int siblingIndex in anomalyController.DisabledAnomalySiblingIndices)
            SyncInitializeDisabledClientRpc(siblingIndex);
    }

    /// <summary>
    /// Tells clients which tentacle indices the server activated for a specific
    /// RandomTentacleAnomaly, identified by its sibling index in the hierarchy.
    /// </summary>
    [ClientRpc]
    private void SyncTentacleAnomalyClientRpc(int siblingIndex, int[] activeIndices)
    {
        if (IsServer) return;
        anomalyController.ApplyTentacleIndicesOnClient(siblingIndex, activeIndices);
    }

    /// <summary>
    /// Tells clients which tumor indices the server activated for a specific
    /// RandomTumorAnomaly, identified by its sibling index in the hierarchy.
    /// </summary>
    [ClientRpc]
    private void SyncTumorAnomalyClientRpc(int siblingIndex, int[] activeIndices)
    {
        if (IsServer) return;
        anomalyController.ApplyTumorIndicesOnClient(siblingIndex, activeIndices);
    }

    /// <summary>
    /// Tells clients to call InitializeDisabled on the anomaly at the given sibling index.
    /// Used for shader-driven anomalies (e.g. lesions, black eyes, blue veins) that were
    /// not selected and need their shader state cleared on all clients.
    /// </summary>
    [ClientRpc]
    private void SyncInitializeDisabledClientRpc(int siblingIndex)
    {
        if (IsServer) return;
        anomalyController.ApplyInitializeDisabledOnClient(siblingIndex);
    }

    /// <summary>
    /// Calls InitializeDisabled on all non-active anomalies across every category and
    /// replicates the call to clients. Invoke this when the suspect arrives at the booth
    /// to ensure locked-category anomalies (excluded from the initial activation pass)
    /// also have their shader state cleaned up.
    /// </summary>
    public void InitializeDisabledOnArrival()
    {
        anomalyController.InitializeDisabledOnArrival();
        InitializeDisabledOnArrivalClientRpc();
    }

    /// <summary>Mirrors the server-side InitializeDisabledOnArrival call to all clients.</summary>
    [ClientRpc]
    private void InitializeDisabledOnArrivalClientRpc()
    {
        if (IsServer) return;
        anomalyController.InitializeDisabledOnArrival();
    }

    public override void Interact(PlayerInteractionController player)
    {
        speaking.InitiateChoices();
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
            speaking.InitiateChoices();
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
        speaking.Say("You.. You're a traitor!!");
        yield return new WaitForSeconds(2);
        animator.SetBool("FiringRifle", true);

        while (true)
        {
            PlayerInstance.Instance.PlayerHealth.TakeDamage(1f);
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

    /// <summary>
    /// Returns the response string for the given choice index based on the current day band.
    /// If StoryMismatchAnomaly is active on this suspect, the mismatch answer for the current
    /// day band is served instead, provided one has been authored. Falls back to the normal
    /// answer if the mismatch field is empty.
    /// Returns null if the index is out of range or the resolved answer text is empty.
    /// </summary>
    public string GetQuestionResponse(int choiceIndex)
    {
        if (suspectData.questionResponses == null || choiceIndex >= suspectData.questionResponses.Length)
            return null;

        SuspectData.QuestionResponseSet set = suspectData.questionResponses[choiceIndex];

        string answer;
        if (ShiftManager.Instance.IsEarlyDays)
            answer = set.earlyDaysAnswer;
        else if (ShiftManager.Instance.IsMidDays)
            answer = set.midDaysAnswer;
        else
            answer = set.finalDaysAnswer;

        if (anomalyController != null && anomalyController.ActiveCountOfType<StoryMismatchAnomaly>() > 0)
        {
            string mismatch;
            if (ShiftManager.Instance.IsEarlyDays)
                mismatch = set.mismatchEarlyDaysAnswer;
            else if (ShiftManager.Instance.IsMidDays)
                mismatch = set.mismatchMidDaysAnswer;
            else
                mismatch = set.mismatchFinalDaysAnswer;

            if (!string.IsNullOrEmpty(mismatch))
                answer = mismatch;
        }

        return string.IsNullOrEmpty(answer) ? null : answer;
    }
}