using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.WSA;
using Application = UnityEngine.Application;

// Must execute after PlayerPickupController (default order 0) so LateUpdate fires
// after the folder has already been moved to the body-arm target position.
[DefaultExecutionOrder(1)]
public class FolderController : PickableObject
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isStamped = new NetworkVariable<bool>(false);
    private NetworkVariable<StampContainer.StampType> _syncedStampType = new NetworkVariable<StampContainer.StampType>(
        StampContainer.StampType.Pass,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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

    /// <summary>
    /// Server-authoritative list of documents currently inside this folder.
    /// Populated via RegisterDocumentServerRpc so CleanupSpawnedFolder can despawn
    /// them on the server even though InteractWithItem only fires on the local client.
    /// </summary>
    private readonly List<NetworkObjectReference> _serverDocuments = new List<NetworkObjectReference>();

    /// <summary>
    /// Per-client list of (document, slot) pairs driven by LateUpdate.
    /// Populated by PlaceInSlotClientRpc and cleared by UnregisterDocumentClientRpc.
    /// </summary>
    private readonly List<(PickableObject document, Transform slot)> _localDocuments
        = new List<(PickableObject, Transform)>();

    /// <summary>Registers a document with this folder on the server so it can be despawned later.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void RegisterDocumentServerRpc(NetworkObjectReference documentRef)
    {
        if (!_serverDocuments.Contains(documentRef))
            _serverDocuments.Add(documentRef);
    }

    /// <summary>
    /// Removes a document from the server-side despawn list and broadcasts the removal to
    /// all clients so they stop following it in LateUpdate.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void UnregisterDocumentServerRpc(NetworkObjectReference documentRef)
    {
        _serverDocuments.Remove(documentRef);
        UnregisterDocumentClientRpc(documentRef);
    }

    [ClientRpc]
    private void UnregisterDocumentClientRpc(NetworkObjectReference documentRef)
    {
        if (!documentRef.TryGet(out NetworkObject netObj)) return;
        PickableObject doc = netObj.GetComponent<PickableObject>();
        _localDocuments.RemoveAll(pair => pair.document == doc);
    }

    /// <summary>
    /// Registers a (document, slot) pair on this client for LateUpdate-based following.
    /// Called from PlaceInSlotClientRpc on every machine so all clients track the document.
    /// </summary>
    public void RegisterLocalDocument(PickableObject document, Transform slot)
    {
        _localDocuments.RemoveAll(pair => pair.document == document);
        _localDocuments.Add((document, slot));
    }

    /// <summary>Despawns all server-tracked documents. Called by SuspectController before despawning the folder.</summary>
    public void DespawnTrackedDocuments()
    {
        foreach (NetworkObjectReference docRef in _serverDocuments)
        {
            if (docRef.TryGet(out NetworkObject netObj))
                NetworkHelper.Despawn(netObj);
        }
        _serverDocuments.Clear();
    }

    /// <summary>
    /// Snaps each slotted document to its slot's current world transform.
    /// Runs at execution order 1 — after PlayerPickupController (order 0) has already
    /// moved the folder to the body-arm target — so documents are always in sync with
    /// the folder in the same frame with zero lag.
    /// </summary>
    private void LateUpdate()
    {
        for (int i = _localDocuments.Count - 1; i >= 0; i--)
        {
            var (doc, slot) = _localDocuments[i];
            if (doc == null || slot == null)
            {
                _localDocuments.RemoveAt(i);
                continue;
            }
            doc.transform.position = slot.position;
            doc.transform.rotation = slot.rotation;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        isOpen.OnValueChanged += (oldVal, newVal) => anim.SetBool("Open", newVal);

        // Late-joiner sync: apply stamp visual immediately if already stamped.
        if (isStamped.Value)
            stampContainer.PlaceStamp(_syncedStampType.Value);
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

        if (isStamped.Value == false)
        {
            if (heldItem.ItemData.name == "Stamp_Green" ||  heldItem.ItemData.name == "Stamp_Red" || heldItem.ItemData.name == "Stamp_Yellow")
            {
                Debug.Log("Interact with item");
                var inkStamp = heldItem.ItemData.PickUpPrefab.GetComponent<InkStampPickup>();
                StartUseStampServerRpc(clientId, inkStamp.StampType);
            }
        }
        else
        {
            Debug.Log("Already Stamped");
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
    private void StartUseStampServerRpc(ulong interactingPlayerId, StampContainer.StampType stampType, ServerRpcParams rpcParams = default)
    {
        if (isStamping) return;
        isStamping = true;
        isStamped.Value = true;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        };
        StartUseStampClientRpc(interactingPlayerId, stampType, clientRpcParams);
    }

    [ClientRpc]
    private void StartUseStampClientRpc(ulong interactingPlayerId, StampContainer.StampType stampType, ClientRpcParams clientRpcParams = default)
    {
        // Find the player instance that initiated the stamp
        NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(interactingPlayerId, out var playerObj);
        if (playerObj == null) return;

        playerPickupController = playerObj.GetComponent<PlayerPickupController>();
        StartCoroutine(UseStampSequence(stampType));
    }

    /// <summary>Writes the stamp type on the server, then broadcasts the visual to all clients.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void PlaceStampServerRpc(StampContainer.StampType stampType)
    {
        _syncedStampType.Value = stampType;
        PlaceStampClientRpc(stampType);
    }

    /// <summary>Applies the stamp visual on every client, including the one that triggered the stamp.</summary>
    [ClientRpc]
    private void PlaceStampClientRpc(StampContainer.StampType stampType)
    {
        stampContainer.PlaceStamp(stampType);
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
                PlaceStampServerRpc(stampType);
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