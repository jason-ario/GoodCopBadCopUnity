using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.WSA;
using Application = UnityEngine.Application;

public class FolderController : PickableObject
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isStamped = new NetworkVariable<bool>(false);

    [Header("Documents")] 
    [SerializeField] private MeshRenderer photoID;
    [SerializeField] EntryPermit entryPermit;
    
    [Header("Set Up")]
    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] Animator anim;
    public Transform stampUpTarget;
    public Transform stampDownTarget;
    [SerializeField] StampContainer stampContainer;
    [SerializeField] private AudioClip stampSound;
    private StampContainer.StampType _stampType;
    
    private bool isStamping;

    [Header("Camera")] 
    [SerializeField] private GameObject cinemachineVirtualCamera;
    [SerializeField] private CinemachineImpulseSource _impulseSource;

    [SerializeField] private Transform cameraRigPos;
    public UnityAction onStampedComplete;
    private bool isOpeningOrClosing;

    [Header("Document Slots")]
    [SerializeField] private Transform idCardSlot;
    [SerializeField] private Transform applicationSlot;
    [SerializeField] private Transform mutationExamSlot;
    [SerializeField] private Transform biologicalExamSlot;
    [SerializeField] private Transform behavioralExamSlot;
    [SerializeField] private Transform documentationExamSlot;
    [SerializeField] private Transform realityExamSlot;

    private FolderItem idCard;
    private FolderItem application;
    private ExamPage behaviorExamPage;
    private ExamPage realityExamPage;
    private ExamPage mutationExamPage;
    private ExamPage biologicalExamPage;
    private ExamPage documentationExamPage;

    public bool IsOpen => isOpen.Value;
    public bool IsStamped => isStamped.Value;
    public StampContainer.StampType StampType => stampContainer.Stamp;
    private NetworkVariable<bool> isHandedOff = new NetworkVariable<bool>();
    public List<PickableObject> documents;

    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        isOpen.OnValueChanged += (oldVal, newVal) => anim.SetBool("Open", newVal);
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (isHandedOff.Value) return;
        
        base.Interact(player);
    }

    public void OnHandOff()
    {
        isHandedOff.Value = true;
        isOpen.Value = false;
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject heldItem)
    { 
        base.InteractWithItem(playerInteractionController, heldItem);
        ulong clientId = playerInteractionController.NetworkObjectId;

        if (heldItem.ItemData.name == "Stamp_Green" ||  heldItem.name == "Stamp_Red" || heldItem.name == "Stamp_Yellow")
        {
            Debug.Log("Interact with item");
            var inkStamp = heldItem.ItemData.PickUpPrefab.GetComponent<InkStampPickup>();
            StartUseStampServerRpc(clientId, inkStamp.StampType);
        }

        if (heldItem.ItemData.name is "ID card" or "Application" or "Behavior Exam Page" or "Mutation Exam Page" or "Documentation Exam Page" or "Reality Exam Page" or "Biological Exam Page" )
        {
            AddDocument(heldItem, playerInteractionController.pickupController, true);
        }

        if (heldItem.ItemData.name is "Documentation Exam Notebook" or "Reality Exam Notebook" or "Behavior Exam Notebook" or "Mutation Exam Notebook" or "Biological Exam Notebook")
        {
            if (HasNotebookPage(heldItem.ItemData.name) == false)
            {
                AddNotebookPaper(heldItem.ItemData.name, playerInteractionController.pickupController);
            }
        }
    }

    bool HasNotebookPage(string itemName)
    {
        if (itemName == "Mutation Exam Notebook")
        {
            return mutationExamPage != null;
        }
        if (itemName == "Behavior Exam Notebook")
        {
            return behaviorExamPage != null;
        }
        if (itemName == "Reality Exam Notebook")
        {
            return realityExamPage != null;
        }
        if (itemName == "Documentation Exam Notebook")
        {
            return documentationExamPage != null;
        }
        if (itemName == "Biological Exam Notebook")
        {
            return biologicalExamPage != null;
        }

        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void StartUseStampServerRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        if (isStamping) return;
        StartUseStampClientRpc(interactingPlayerId, stampType);
    }

    [ClientRpc]
    private void StartUseStampClientRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        // Find the player instance that initiated the stamp
        NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(interactingPlayerId, out var playerObj);
        if (playerObj == null) return;

        playerPickupController = playerObj.GetComponent<PlayerPickupController>();
        StartCoroutine(UseStampSequence(stampType));
    }

    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);
        
        if (isOpen.Value)
        {
            player.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", true);
            if(idCard != null){ idCard.SetInteractable(false); }
            if(application != null){ application.SetInteractable(false); }
        }
    }
    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);
        player.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
    }

    IEnumerator UseStampSequence(StampContainer.StampType stampType)
    {
        GetComponent<HighlightPlus.HighlightEffect>().highlighted = false;
        isStamping = true;
        isStamped.Value = true;
        
        // Only lock controls for the local player who is actually interacting
        bool isLocal = playerPickupController.IsLocalPlayer;
        if (isLocal) PlayerInstance.Instance.CanControl = false;

        if (IsOwner)
        {
            cinemachineVirtualCamera.SetActive(true);
        }
        yield return new WaitForSeconds(.25f);

        playerPickupController.GetComponent<PlayerMovementController>().LookAtTarget(transform);

        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.position = stampDownTarget.position;
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampUpTarget.position, .5f);

        
        
        
        playerPickupController.PlayerMovementController.CameraTransform.DOMove(cameraRigPos.transform.position, .25f); 
        playerPickupController.PlayerMovementController.CameraTransform.DORotate(cameraRigPos.transform.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.TurnRightArmRigOnAndOff(.5f,.5f);
        yield return new WaitForSeconds(.5f);

        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampDownTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampDownTarget.rotation.eulerAngles, .25f);
        SFXController.Instance.Play(stampSound);
     
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampDownTarget.position, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampDownTarget.position, .25f)
            .OnComplete(() =>
            {
                // Only server handles logic state changes
                if (IsServer) stampContainer.PlaceStamp(stampType);
            });

        yield return new WaitForSeconds(.2f);
        _impulseSource.GenerateImpulse();
        yield return new WaitForSeconds(.25f);


        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.RightArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DORotate(
            stampUpTarget.rotation.eulerAngles, .25f);
        playerPickupController.PlayerAnimationController.CamRightArmIKTarget.transform.DOMove(stampUpTarget.position, .25f);
        yield return new WaitForSeconds(.5f);

        isStamping = false;
        
        if (IsOwner)
        {
            cinemachineVirtualCamera.SetActive(false);
        }
        if (isLocal) PlayerInstance.Instance.CanControl = true;
        playerPickupController.PlayerMovementController.ResetCameraPos(false, .5f);
        GetComponent<HighlightPlus.HighlightEffect>().highlighted = true;

        onStampedComplete?.Invoke();
    }

    public override void OnStartUse()
    {
        if (isOpeningOrClosing == false && isOpen.Value == false)
        {
            Debug.Log("Open");
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", true);
            StopAllCoroutines();
            StartCoroutine(WaitAndOpen());
        }
        else
        {
            Debug.Log("Close");
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
            StopAllCoroutines();
            isOpen.Value = false;
            anim.SetBool("Open", false);
            isOpeningOrClosing = false;
        }
    }
    
    IEnumerator WaitAndOpen()
    {
        isOpeningOrClosing = true;
        yield return new WaitForSeconds(.4f);
        isOpen.Value = true;
        anim.SetBool("Open", true);
        isOpeningOrClosing = false;
    }

    public void AddDocument(PickableObject pickableObject, PlayerPickupController player, bool dropObject)
    {
        string itemName = pickableObject.ItemData.name;
        Debug.Log("Try add document");

        if (itemName == "ID card")
        {
            if (idCard != null)
            {
                return;
            }
            
            player.DropObject(idCardSlot);
            idCard = pickableObject.GetComponent<IDCard>();
            
            idCard.AddToFolder(this);
        }

        if (itemName == "Application")
        {
            if (application != null)
            {
                return;
            }
            
            player.DropObject(applicationSlot);
            application = pickableObject.GetComponent<ApplicationLetter>();
            application.AddToFolder(this);
        }
        
        if (itemName == "Behavior Exam Page")
        {
            if (behaviorExamPage != null)
            {
                return;
            }
            
            behaviorExamPage = pickableObject.GetComponent<ExamPage>();

            if (dropObject)
            {
                player.DropObject(behavioralExamSlot);
            }
            else
            {
                behaviorExamPage.SetParent(behavioralExamSlot);
            }
            
            behaviorExamPage.AddToFolder(this);
        }
        
        if (itemName == "Mutation Exam Page")
        {
            if (mutationExamPage != null)
            {
                return;
            }
            
            mutationExamPage = pickableObject.GetComponent<ExamPage>();

            if (dropObject)
            {
                player.DropObject(mutationExamSlot);
            }
            else
            {
                mutationExamPage.SetParent(mutationExamSlot);
            }
            
            mutationExamPage.AddToFolder(this);
        }
        
        if (itemName == "Reality Exam Page")
        {
            if (realityExamPage != null)
            {
                return;
            }
            
            realityExamPage = pickableObject.GetComponent<ExamPage>();

            if (dropObject)
            {
                player.DropObject(realityExamSlot);
            }
            else
            {
                realityExamPage.SetParent(realityExamSlot);
            }
            
            realityExamPage.AddToFolder(this);
        }
        
        if (itemName == "Documentation Exam Page")
        {
            if (documentationExamPage != null)
            {
                return;
            }
            
            documentationExamPage = pickableObject.GetComponent<ExamPage>();

            if (dropObject)
            {
                player.DropObject(documentationExamSlot);
            }
            else
            {
                documentationExamPage.SetParent(documentationExamSlot);
            }
            
            documentationExamPage.AddToFolder(this);
        }
        
        if (itemName == "Biological Exam Page")
        {
            if (biologicalExamPage != null)
            {
                return;
            }
            
            biologicalExamPage = pickableObject.GetComponent<ExamPage>();

            if (dropObject)
            {
                player.DropObject(biologicalExamSlot);
            }
            else
            {
                biologicalExamPage.SetParent(biologicalExamSlot);
            }
            
            biologicalExamPage.AddToFolder(this);
        }
    }

    public void RemoveDocument(PickableObject pickableObject, PlayerPickupController player)
    {
        string itemName = pickableObject.ItemData.name;
        Debug.Log("Try remove document");
        
        if (itemName == "ID card")
        {
            idCard = null;
        }
        
        if (itemName == "Application")
        {
            application = null;
        }
        
        if (itemName == "Mutation Exam Page")
        {
            mutationExamPage = null;
        }
        
        if (itemName == "Behavior Exam Page")
        {
            behaviorExamPage = null;
        }
        
        if (itemName == "Reality Exam Page")
        {
            realityExamPage = null;
        }
        
        if (itemName == "Documentation Exam Page")
        {
            documentationExamPage = null;
        }
        
        if (itemName == "Biological Exam Page")
        {
            biologicalExamPage = null;
        }
    }

    public void AddNotebookPaper(string itemName, PlayerPickupController player)
    {
        if (itemName == "Mutation Exam Notebook")
        {
            player.HeldObject.GetComponent<ExamNotebook>().AddToFolder(this);
        }
        
        if (itemName == "Behavior Exam Notebook")
        {
            player.HeldObject.GetComponent<ExamNotebook>().AddToFolder(this);
        }
        
        if (itemName == "Reality Exam Notebook")
        {
            player.HeldObject.GetComponent<ExamNotebook>().AddToFolder(this);
        }
        
        if (itemName == "Biological Exam Notebook")
        {
            player.HeldObject.GetComponent<ExamNotebook>().AddToFolder(this);
        }
        
        if (itemName == "Documentation Exam Notebook")
        {
            player.HeldObject.GetComponent<ExamNotebook>().AddToFolder(this);
        }
    }

    public override void OnDropped()
    {
        base.OnDropped();
        if (isOpen.Value)
        {
            if(idCard != null){ idCard.SetInteractable(true); }
            if(application != null){ application.SetInteractable(true); }
        }
    }

    public bool ExamContainsAnomaly(Anomaly anomaly)
    {
        // Get all exam pages that are added to the folder
        ExamPage[] examPages = new ExamPage[] 
        { 
            behaviorExamPage, 
            realityExamPage, 
            mutationExamPage, 
            biologicalExamPage, 
            documentationExamPage 
        };

        // Check each exam page for the anomaly
        foreach (ExamPage examPage in examPages)
        {
            if (examPage == null) continue; 
            Debug.Log("Checking exam page:" + examPage.name);

            // Get the checklist items from the exam page
            ChecklistItem[] checklistItems = examPage.GetComponent<ExamPage>().ChecklistItems;
            
            if (checklistItems == null) continue;

            // Check each checklist item
            foreach (ChecklistItem item in checklistItems)
            {
                Debug.Log($"Checking: {item.AnomalyTypeName} vs {anomaly.GetType().Name}, IsChecked: {item.IsChecked}");
                
                // Check if this checklist item references the anomaly type and is checked
                if (item.AnomalyTypeName == anomaly.GetType().Name && item.IsChecked)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public Anomaly[] GetAnomaliesInFolder()
    {
        List<Anomaly> anomaliesFound = new List<Anomaly>();
    
        // Check all exam pages in the folder
        ExamPage[] examPages = new ExamPage[]
        {
            behaviorExamPage,
            mutationExamPage,
            biologicalExamPage,
            documentationExamPage,
            realityExamPage
        };
    
        // Iterate through each exam page
        foreach (ExamPage examPage in examPages)
        {
            if (examPage == null) continue;
        
            // Get all checklist items in this exam page
            ChecklistItem[] checklistItems = examPage.ChecklistItems;
        
            // Check which items have been marked as checked
            foreach (ChecklistItem item in checklistItems)
            {
                if (item != null && item.IsChecked && item.AnomalyTypeReference is Anomaly anomaly)
                {
                    // Add the anomaly if it's not already in the list
                    if (!anomaliesFound.Contains(anomaly))
                    {
                        anomaliesFound.Add(anomaly);
                    }
                }
            }
        }
    
        return anomaliesFound.ToArray();
    }
}