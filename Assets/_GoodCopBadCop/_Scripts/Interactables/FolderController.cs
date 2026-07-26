using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using Application = UnityEngine.Application;

// DefaultExecutionOrder removed — LateUpdate was replaced by per-document SocketFollow components
// (execution order 2) which track slot transforms directly and are unaffected by the folder's
// own script order.
public class FolderController : PickableObject
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isStamped = new NetworkVariable<bool>(false);
    private NetworkVariable<StampContainer.StampType> _syncedStampType = new NetworkVariable<StampContainer.StampType>(
        StampContainer.StampType.Pass,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    
    [Header("Set Up")]
    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] private AudioClip folderOpenClip;
    [SerializeField] private AudioClip folderCloseClip;
    [SerializeField] Animator anim;

    /// <summary>IK target position for the camera (first-person) arm at the top of the stamp arc.</summary>
    public Transform stampUpTarget;
    /// <summary>IK target position for the camera (first-person) arm at the bottom of the stamp arc.</summary>
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

    /// <summary>
    /// Five generic exam-page slots used as a FIFO queue.
    /// Pages fill slot 0 → 4 in order of insertion. When a page is removed all
    /// subsequent pages shift down so the queue stays contiguous from slot 0.
    /// </summary>
    [SerializeField] private Transform[] examPageSlots = new Transform[5];

    /// <summary>
    /// Slots where physical evidence items (FolderProofDocument) are placed.
    /// Fill these in the folder prefab Inspector — one Transform child per slot.
    /// </summary>
    [SerializeField] private Transform[] _evidenceSlots = new Transform[5];

    private FolderItem idCard;
    private FolderItem application;

    // Server-side ordered list: element i is the ExamPage currently in examPageSlots[i].
    // null entries mean the slot is empty. Only written on the server.
    private readonly ExamPage[] _examPageQueue = new ExamPage[5];

    // Synced queue occupancy: each slot stores the item-data name of the page it holds,
    // or an empty string when the slot is free. Written by the server, read by all clients.
    // Used by every client to guard against duplicate adds and notebook interactions.
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _queueSlot0 =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _queueSlot1 =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _queueSlot2 =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _queueSlot3 =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> _queueSlot4 =
        new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<Unity.Collections.FixedString64Bytes>[] _queueSlots;

    public bool IsOpen => isOpen.Value;
    public bool IsStamped => isStamped.Value;
    public StampContainer.StampType StampType => stampContainer.Stamp;
    public bool IsHandedOff => isHandedOff.Value;
    private NetworkVariable<bool> isHandedOff = new NetworkVariable<bool>();
    public List<PickableObject> documents;

    /// <summary>
    /// Fired on the local client whenever a document is successfully added to this folder.
    /// The argument is the document that was just filed.
    /// Fires for ID card and Application only — not exam pages.
    /// </summary>
    public static event System.Action<PickableObject> OnDocumentAdded;

    /// <summary>
    /// Fired on the local client the moment any FolderController is picked up by a player.
    /// Passes the picked-up instance so subscribers can track the live network object.
    /// Subscribe in tutorial scripts to react when the player grabs the folder from the drawer.
    /// </summary>
    public static event System.Action<FolderController> OnFolderEquipped;

    /// <summary>
    /// Fired on the local client when the folder is handed off to the window slot and
    /// the verdict is delivered. Subscribe in tutorial scripts to complete the final tutorial beat.
    /// </summary>
    public static event System.Action OnFolderHandedOff;

    /// <summary>
    /// Fired on the local client the moment any FolderController completes its stamp sequence.
    /// Static so subscribers don't need to track a specific instance.
    /// </summary>
    public static event System.Action OnAnyFolderStamped;

    /// <summary>
    /// Server-authoritative list of documents currently inside this folder.
    /// Populated via RegisterDocumentServerRpc so CleanupSpawnedFolder can despawn
    /// them on the server even though InteractWithItem only fires on the local client.
    /// </summary>
    private readonly List<NetworkObjectReference> _serverDocuments = new List<NetworkObjectReference>();

    /// <summary>
    /// Server-authoritative list of evidence documents filed in this folder.
    /// Stores the category alongside each document reference so PayOutResults can
    /// award bonuses without re-resolving the component at scoring time.
    /// </summary>
    private readonly List<(AnomalyCategory category, NetworkObjectReference docRef)> _evidenceDocuments
        = new List<(AnomalyCategory, NetworkObjectReference)>();

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

        // Clear the document from the authoritative exam-page queue so it can be re-placed later.
        // RemoveDocument only works correctly on the server (where _examPageQueue is populated and
        // _queueSlots writes are authoritative), so this is the right place to call it.
        if (documentRef.TryGet(out NetworkObject netObj))
        {
            PickableObject doc = netObj.GetComponent<PickableObject>();
            if (doc != null)
            {
                RemoveDocument(doc, null);
                documents.Remove(doc);
            }
        }

        UnregisterDocumentClientRpc(documentRef);
    }

    [ClientRpc]
    private void UnregisterDocumentClientRpc(NetworkObjectReference documentRef)
    {
        if (!documentRef.TryGet(out NetworkObject netObj)) return;
        PickableObject doc = netObj.GetComponent<PickableObject>();
        if (doc != null) doc.ClearSocketFollow();
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
        _evidenceDocuments.Clear();
    }

    /// <summary>
    /// Server-authoritative registration of a FolderProofDocument placed in this folder.
    /// Records the document in both the general despawn list and the evidence-scoring list.
    /// Called from the client that placed the item; always runs on the server.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RegisterEvidenceDocumentServerRpc(NetworkObjectReference docRef, int categoryInt)
    {
        if (!_serverDocuments.Contains(docRef))
            _serverDocuments.Add(docRef);

        AnomalyCategory category = (AnomalyCategory)categoryInt;
        _evidenceDocuments.Add((category, docRef));

        Debug.Log($"[FolderController] Evidence registered: {category} ({_evidenceDocuments.Count} total).");
    }

    /// <summary>
    /// Returns how many evidence documents have been filed for each category.
    /// Keyed by AnomalyCategory; missing entries mean zero evidence for that category.
    /// Must only be called on the server (scoring is server-authoritative).
    /// </summary>
    public Dictionary<AnomalyCategory, int> GetEvidenceCountByCategory()
    {
        var result = new Dictionary<AnomalyCategory, int>();
        foreach (var (category, _) in _evidenceDocuments)
        {
            result.TryGetValue(category, out int current);
            result[category] = current + 1;
        }
        return result;
    }

    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        isOpen.OnValueChanged += OnIsOpenChanged;

        // Late-joiner sync: apply stamp visual immediately if already stamped.
        if (isStamped.Value)
            stampContainer.PlaceStamp(_syncedStampType.Value);

        // Fire the static stamp event on all clients whenever isStamped becomes true.
        // This ensures Day_01's OnFolderStamped handler fires on the server regardless
        // of which client triggered the stamp sequence.
        isStamped.OnValueChanged += (_, newValue) =>
        {
            if (newValue)
                OnAnyFolderStamped?.Invoke();
        };

        // Fire the static hand-off event on all clients whenever isHandedOff becomes true.
        isHandedOff.OnValueChanged += (_, newValue) =>
        {
            if (newValue)
                OnFolderHandedOff?.Invoke();
        };

        // Build the array accessor for the five synced queue slots.
        _queueSlots = new NetworkVariable<Unity.Collections.FixedString64Bytes>[]
        {
            _queueSlot0, _queueSlot1, _queueSlot2, _queueSlot3, _queueSlot4
        };
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (isHandedOff.Value) return;
        
        base.Interact(player);
    }

    /// <summary>
    /// Drives the animator and document interactability whenever the folder's open state changes.
    /// Called on every client via the NetworkVariable callback so all machines stay in sync,
    /// regardless of which client owns the folder or filed the documents.
    /// </summary>
    private void OnIsOpenChanged(bool oldVal, bool newVal)
    {
        anim.SetBool("Open", newVal);

        AudioClip clip = newVal ? folderOpenClip : folderCloseClip;
        SFXController.Instance.PlayAtPosition(clip, transform.position);

        // Only update document interactability when the folder is NOT being held.
        // While held, OnEquipped / OnDropped manage it; here we handle desk-placed state.
        if (IsHeld) return;

        // Use the networked path on the server so the NetworkVariable override is cleared.
        // The local SetInteractable is insufficient here — it is overridden by
        // ApplyNetworkInteractableState on any client where _networkInteractableOverride != -1
        // (e.g. a tutorial-locked document that was filed while the folder was closed).
        // Routing through the server ensures the NetworkVariable update reaches every client.
        if (IsServer)
        {
            if (idCard != null)       idCard.SetInteractableNetworked(newVal);
            if (application != null)  application.SetInteractableNetworked(newVal);

            // Exam pages are tracked separately in _examPageQueue — apply the same open-state
            // change so they become interactable when the folder opens and non-interactable
            // when it closes, matching the behaviour of idCard and application.
            foreach (ExamPage examPage in _examPageQueue)
            {
                if (examPage != null) examPage.SetInteractableNetworked(newVal);
            }
        }
    }

    public void OnHandOff()
    {
        isHandedOff.Value = true;
        SetOpenServerRpc(false);
        // OnFolderHandedOff is now fired via isHandedOff.OnValueChanged on all clients.
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject heldItem)
    {
        // Block all item interactions while the folder is being held by any player.
        // The holding player's client disables colliders optimistically in PickUpObject,
        // and _holdingClientId propagates the same state to every other client via
        // OnHoldingClientChanged → SetInteractable. This guard is a belt-and-suspenders
        // check that also catches edge cases where the collider state arrives late.
        Debug.Log($"[FolderController] InteractWithItem on client {NetworkManager.Singleton.LocalClientId}: held={heldItem?.ItemData?.name ?? "NULL"}, IsHeld={IsHeld}");
        if (IsHeld) return;

        base.InteractWithItem(playerInteractionController, heldItem);
        ulong clientId = playerInteractionController.NetworkObjectId;

        if (isStamped.Value == false)
        {
            if (heldItem.ItemData.name == "Stamp_Green" ||  heldItem.ItemData.name == "Stamp_Red" || heldItem.ItemData.name == "Stamp_Yellow")
            {
                if (isOpen.Value)
                {
                    // Cannot stamp while the folder is open — close it instead.
                    Debug.Log("[FolderController] Stamp blocked: folder is open. Closing folder.");
                    if (playerPickupController != null)
                        playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
                    StopAllCoroutines();
                    SetOpenServerRpc(false);
                    isOpeningOrClosing = false;
                    return;
                }

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
        else if (heldItem.GetComponent<FolderProofDocument>() != null)
        {
            // Evidence items are detected by component rather than name so any proof document
            // prefab can be added without updating this whitelist.
            AddDocument(heldItem, playerInteractionController.pickupController, true);
        }

        if (heldItem.ItemData.name is "Documentation Exam Notebook" or "Reality Exam Notebook" or "Behavior Exam Notebook" or "Mutation Exam Notebook" or "Biological Exam Notebook")
        {
            Debug.Log($"[FolderController] Notebook interaction on client {NetworkManager.Singleton.LocalClientId}: held={heldItem.ItemData.name}, hasPage={HasNotebookPage(heldItem.ItemData.name)}");
            if (HasNotebookPage(heldItem.ItemData.name) == false)
            {
                AddNotebookPaper(heldItem.ItemData.name, playerInteractionController.pickupController);
            }
        }
    }

    bool HasNotebookPage(string itemName)
    {
        // Map notebook name → corresponding page item-data name.
        string pageName = itemName switch
        {
            "Mutation Exam Notebook"       => "Mutation Exam Page",
            "Behavior Exam Notebook"       => "Behavior Exam Page",
            "Reality Exam Notebook"        => "Reality Exam Page",
            "Documentation Exam Notebook"  => "Documentation Exam Page",
            "Biological Exam Notebook"     => "Biological Exam Page",
            _                              => null,
        };

        if (pageName == null) return false;
        return IsExamPageTypeInQueue(pageName);
    }

    /// <summary>
    /// Returns true if a page with the given item-data name is already present in the queue.
    /// On the server, reads the authoritative C# array. On clients, reads the synced
    /// NetworkVariables so the check works correctly everywhere.
    /// </summary>
    private bool IsExamPageTypeInQueue(string pageName)
    {
        if (IsServer)
        {
            foreach (ExamPage page in _examPageQueue)
            {
                if (page != null && page.ItemData.name == pageName) return true;
            }
            return false;
        }

        if (_queueSlots == null) return false;
        Unity.Collections.FixedString64Bytes key = pageName;
        foreach (var slot in _queueSlots)
        {
            if (slot.Value == key) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the index (0–4) of the first free queue slot, or -1 if all slots are full.
    /// On the server, reads the authoritative C# array. On clients, reads the synced
    /// NetworkVariables.
    /// </summary>
    private int GetFreeQueueSlotIndex()
    {
        if (IsServer)
        {
            for (int i = 0; i < _examPageQueue.Length; i++)
            {
                if (_examPageQueue[i] == null) return i;
            }
            return -1;
        }

        if (_queueSlots == null) return -1;
        for (int i = 0; i < _queueSlots.Length; i++)
        {
            if (_queueSlots[i].Value.IsEmpty) return i;
        }
        return -1;
    }

    [ServerRpc(RequireOwnership = false)]
    private void StartUseStampServerRpc(ulong interactingPlayerId, StampContainer.StampType stampType, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[FolderController] StartUseStampServerRpc: isStamping={isStamping} isOpen={isOpen.Value} NetworkObjectId={NetworkObjectId}");
        if (isStamping) { Debug.LogWarning("[FolderController] Stamp blocked: already stamping."); return; }
        if (isOpen.Value) { Debug.LogWarning("[FolderController] Stamp blocked: folder is open."); return; }

        if (stampType == StampContainer.StampType.Quarantine && IsQuarantineFull())
        {
            Debug.LogWarning("[FolderController] Quarantine stamp blocked: quarantine slots are full.");
            QuarantineFullClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            });
            return;
        }

        // Consume one ink use for limited stamp types; block and notify the requesting client if none remain.
        if (StampInkManager.Instance != null && !StampInkManager.Instance.ConsumeInk(stampType))
        {
            Debug.LogWarning($"[FolderController] Stamp blocked: no ink remaining for {stampType}.");
            NotEnoughInkClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            });
            return;
        }

        isStamping = true;
        isStamped.Value = true;
        Debug.Log($"[FolderController] isStamped set to true on server. NetworkObjectId={NetworkObjectId}");

        StartUseStampClientRpc(interactingPlayerId, stampType);
    }

    private bool IsQuarantineFull()
    {
        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
        return SuspectRunRecords.Instance != null
               && !SuspectRunRecords.Instance.HasQuarantineSlot(currentDay);
    }

    [ClientRpc]
    private void QuarantineFullClientRpc(ClientRpcParams clientRpcParams = default)
    {
        UIController.Instance?.ShowShopNotification("<color=red>5/5 quarantine slots full!</color>");
    }

    [ClientRpc]
    private void NotEnoughInkClientRpc(ClientRpcParams clientRpcParams = default)
    {
        UIController.Instance?.ShowShopNotification("<color=red>NOT ENOUGH INK</color>");
    }

    [ClientRpc]
    private void StartUseStampClientRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        // Find the player instance that initiated the stamp
        NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(interactingPlayerId, out var playerObj);
        if (playerObj == null) return;

        // Lock the folder against pickup on every client for the duration of the sequence.
        SetInteractable(false);

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
        OnFolderEquipped?.Invoke(this);

        // Notify the server so server-side tutorial coroutines that wait on OnFolderEquipped
        // receive the event even when a non-host client picks up the folder.
        NotifyFolderEquippedServerRpc();

        if (isOpen.Value)
        {
            player.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", true);
            if(idCard != null){ idCard.SetInteractable(false); }
            if(application != null){ application.SetInteractable(false); }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyFolderEquippedServerRpc()
    {
        // On the host/server the local OnFolderEquipped already fired in OnEquipped.
        // Only re-fire for a dedicated server (IsHost is false) or when the picking
        // client is not the host.
        if (!IsHost)
            OnFolderEquipped?.Invoke(this);
    }

    /// <summary>
    /// Called by the client that added an ID card or Application so the server:
    ///   1. Fires OnDocumentAdded for server-side tutorial coroutines.
    ///   2. Broadcasts the document association to every other client via ClientRpc so
    ///      they resolve their local idCard/application references and make the document
    ///      interactable when the folder is open.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SyncDocumentAddedServerRpc(NetworkObjectReference documentRef, string itemName, ServerRpcParams rpcParams = default)
    {
        // Resolve the document on the server so we can fix its interactable state.
        // The document was previously locked via SetInteractableNetworked(false) before it was
        // filed — _networkInteractableOverride is 0 on every client. We must clear that override
        // so OnIsOpenChanged (which now uses SetInteractableNetworked) drives the state correctly.
        // Using UnlockInteractableNetworked resets to -1 so normal holder/open-state logic applies.
        if (documentRef.TryGet(out NetworkObject netObj))
        {
            PickableObject doc = netObj.GetComponent<PickableObject>();
            if (doc != null)
            {
                // Clear the tutorial override — OnIsOpenChanged will immediately re-apply the
                // correct state via SetInteractableNetworked when the folder next opens or closes.
                // Apply the current open state right now so clients see the correct state immediately.
                doc.SetInteractableNetworked(isOpen.Value);

                // Only fire OnDocumentAdded for non-host filers — the host already fired it
                // locally in AddDocument, so re-firing here would double-count in the tutorial.
                bool senderIsHost = rpcParams.Receive.SenderClientId == 0ul;
                if (!senderIsHost)
                    OnDocumentAdded?.Invoke(doc);
            }
        }

        // Broadcast to all clients except the sender so they resolve the reference locally.
        ClientRpcParams clientParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = GetAllClientsExcept(rpcParams.Receive.SenderClientId)
            }
        };
        SyncDocumentAddedClientRpc(documentRef, itemName, clientParams);
    }

    /// <summary>
    /// Received on every client except the one that originally filed the document.
    /// Resolves the local reference and registers the folder association so that
    /// RemovePromFolder / document-removal logic works on this client too.
    /// Interactable state is already driven by the server via SetInteractableNetworked in
    /// SyncDocumentAddedServerRpc — we must NOT call local SetInteractable here because
    /// it fights with the NetworkVariable override and produces different results per client.
    /// </summary>
    [ClientRpc]
    private void SyncDocumentAddedClientRpc(NetworkObjectReference documentRef, string itemName, ClientRpcParams clientParams = default)
    {
        if (!documentRef.TryGet(out NetworkObject netObj)) return;
        PickableObject doc = netObj.GetComponent<PickableObject>();
        if (doc == null) return;

        if (itemName == "ID card")
        {
            if (idCard != null) return;
            idCard = doc.GetComponent<IDCard>();
            if (idCard != null)
            {
                idCard.insideThisFolder = this;
                documents.Add(idCard);
            }
            OnDocumentAdded?.Invoke(doc);
        }
        else if (itemName == "Application")
        {
            if (application != null) return;
            application = doc.GetComponent<ApplicationLetter>();
            if (application != null)
            {
                application.insideThisFolder = this;
                documents.Add(application);
            }
            OnDocumentAdded?.Invoke(doc);
        }
    }

    /// <summary>Returns all currently connected client IDs except the one specified.</summary>
    private System.Collections.Generic.List<ulong> GetAllClientsExcept(ulong excludedClientId)
    {
        var ids = new System.Collections.Generic.List<ulong>();
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != excludedClientId)
                ids.Add(id);
        }
        return ids;
    }
    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);
        player.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
    }

    IEnumerator UseStampSequence(StampContainer.StampType stampType)
    {
        GetComponent<HighlightPlus.HighlightEffect>().highlighted = false;

        // Capture into a local so that any subsequent write to the instance field
        // (e.g. from OnEquipped on another interaction) cannot corrupt this coroutine
        // across its yield points.
        PlayerPickupController ppc = playerPickupController;

        // Only the stamping player's local client locks controls and activates the cinematic camera.
        bool isStampingLocalPlayer = ppc.IsLocalPlayer;
        if (isStampingLocalPlayer) PlayerInstance.Instance.CanControl = false;

        if (isStampingLocalPlayer)
        {
            cinemachineVirtualCamera.SetActive(true);
        }

        ppc.PlayerAnimationController.SetAnimBoolLocal("HoldingStamp", true);

        // Detach the external IK target so LateUpdate stops overriding the rig target
        // for the duration of the sequence, then restore it when the sequence completes.
        // We then drive rightArmRigIKTarget (the fixed rig constraint target) directly via DOTween.
        Transform savedRightArmIKTarget = ppc.PlayerAnimationController.RightArmIKTarget;
        ppc.PlayerAnimationController.RightArmIKTarget = null;
        // Tell the proxy passthrough in Update not to overwrite the rig target while the
        // stamp sequence owns it directly via DOTween.
        ppc.PlayerAnimationController.DriveRightArmRigTargetDirectly = true;

        Transform savedCamRightArmRigIKTarget = null;
        if (isStampingLocalPlayer)
        {
            savedCamRightArmRigIKTarget = ppc.PlayerAnimationController.CamRightArmRigIKTarget;
            ppc.PlayerAnimationController.CamRightArmRigIKTarget = null;
        }

        yield return new WaitForSeconds(.25f);

        // Drive a full forward lean on all clients for the duration of the stamp so the
        // character visibly hunches toward the folder from a third-person perspective.
        // LockBodyLeanFactor prevents PlayerMovementController from overriding the scripted
        // lean each frame. The lean still applies to bones so the body tilts forward and
        // ApplyLocalBodyLean + SolveTwoBoneIK bend the arm naturally from the closer shoulder.
        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.SetBodyLeanDirect(1f, 1f);
            ppc.PlayerAnimationController.LockBodyLeanFactor = true;
        }

        ppc.GetComponent<PlayerMovementController>().LookAtTarget(transform);

        // Resolve body-arm targets, falling back to the camera-arm targets when unassigned.
        Transform bodyUp   = stampUpTarget;
        Transform bodyDown = stampDownTarget;

        // Diagnostic: log the IK target state on every client so we can verify what's null.
        Debug.Log($"[FolderController] UseStampSequence — client={NetworkManager.Singleton.LocalClientId} " +
                  $"isStampingLocalPlayer={isStampingLocalPlayer} " +
                  $"ppc={ppc?.name ?? "NULL"} " +
                  $"RightArmRigIKTarget={ppc?.PlayerAnimationController?.RightArmRigIKTarget?.name ?? "NULL"} " +
                  $"bodyUp={bodyUp?.name ?? "NULL"} bodyDown={bodyDown?.name ?? "NULL"}");

        // Drive the fixed rig constraint target directly (external passthrough is null'd above).
        // This works on all clients because the coroutine is started via ClientRpc.
        if (ppc.PlayerAnimationController.RightArmRigIKTarget != null)
        {
            ppc.PlayerAnimationController.RightArmRigIKTarget.position = bodyDown.position;
            ppc.PlayerAnimationController.RightArmRigIKTarget.DORotate(
                bodyUp.rotation.eulerAngles, .25f);
            ppc.PlayerAnimationController.RightArmRigIKTarget.DOMove(bodyUp.position, .5f);
        }
        else
        {
            Debug.LogError("[FolderController] RightArmRigIKTarget is null on this client — body arm IK will not drive during stamp.");
        }

        // Drive the camera-arm IK target only on the stamping player's local client.
        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.CamRightArmIKTarget.position = stampDownTarget.position;
            ppc.PlayerAnimationController.CamRightArmIKTarget.DORotate(
                stampUpTarget.rotation.eulerAngles, .25f);
            ppc.PlayerAnimationController.CamRightArmIKTarget.DOMove(stampUpTarget.position, .5f);

            ppc.PlayerMovementController.CameraTransform.DOMove(cameraRigPos.transform.position, .25f);
            ppc.PlayerMovementController.CameraTransform.DORotate(cameraRigPos.transform.rotation.eulerAngles, .25f);
        }

        ppc.PlayerAnimationController.TurnRightArmRigOnAndOff(.5f, .5f);
        yield return new WaitForSeconds(.5f);

        ppc.PlayerAnimationController.RightArmRigIKTarget?.DORotate(
            bodyDown.rotation.eulerAngles, .25f);
        ppc.PlayerAnimationController.RightArmRigIKTarget?.DOMove(bodyDown.position, .25f);

        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.CamRightArmIKTarget?.DORotate(
                stampDownTarget.rotation.eulerAngles, .25f);
            ppc.PlayerAnimationController.CamRightArmIKTarget?.DOMove(stampDownTarget.position, .25f);
        }

        SFXController.Instance.Play(stampSound);

        // Only the stamping player sends the ServerRpc — avoids duplicate calls from every client.
        if (isStampingLocalPlayer)
        {
            yield return new WaitForSeconds(.25f);
            PlaceStampServerRpc(stampType);
        }

        yield return new WaitForSeconds(.2f);
        _impulseSource.GenerateImpulse();
        yield return new WaitForSeconds(.25f);

        ppc.PlayerAnimationController.RightArmRigIKTarget?.DORotate(
            bodyUp.rotation.eulerAngles, .25f);
        ppc.PlayerAnimationController.RightArmRigIKTarget?.DOMove(bodyUp.position, .25f);

        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.CamRightArmIKTarget?.DORotate(
                stampUpTarget.rotation.eulerAngles, .25f);
            ppc.PlayerAnimationController.CamRightArmIKTarget?.DOMove(stampUpTarget.position, .25f);
        }

        yield return new WaitForSeconds(.5f);

        isStamping = false;

        // Restore the external IK target so the pickup system resumes driving the rig target,
        // and release the direct-drive lock so the proxy passthrough resumes.
        ppc.PlayerAnimationController.RightArmIKTarget = savedRightArmIKTarget;
        ppc.PlayerAnimationController.DriveRightArmRigTargetDirectly = false;

        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.CamRightArmRigIKTarget = savedCamRightArmRigIKTarget;
        }

        ppc.PlayerAnimationController.SetAnimBoolLocal("HoldingStamp", false);

        if (isStampingLocalPlayer)
        {
            cinemachineVirtualCamera.SetActive(false);
            // Release the lean lock before zeroing so PlayerMovementController can resume
            // driving the lean factor naturally through SetLocalBodyLeanFactor.
            ppc.PlayerAnimationController.LockBodyLeanFactor = false;
            ppc.PlayerAnimationController.SetBodyLeanDirect(0f);
            PlayerInstance.Instance.CanControl = true;
            ppc.PlayerMovementController.ResetCameraPos(false, .5f);
        }

        GetComponent<HighlightPlus.HighlightEffect>().highlighted = true;

        // Restore interactability now that the sequence is done.
        // Skip if the folder was picked up mid-sequence — _holdingClientId already
        // controls the collider state in that case and must not be overridden here.
        if (!IsHeld)
            SetInteractable(true);

        onStampedComplete?.Invoke();
        Debug.Log($"[FolderController] UseStampSequence complete — isStamped NetworkVariable will fire OnAnyFolderStamped on all clients. NetworkObjectId={NetworkObjectId}");
        // OnAnyFolderStamped is now fired via isStamped.OnValueChanged on all clients.
    }

    public override void OnStartUse()
    {
        if (isOpeningOrClosing == false && isOpen.Value == false)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", true);
            StopAllCoroutines();
            StartCoroutine(WaitAndOpen());
        }
        else
        {
            playerPickupController.PlayerAnimationController.SetAnimBool("HoldingFolderOpen", false);
            StopAllCoroutines();
            SetOpenServerRpc(false);
            isOpeningOrClosing = false;
        }
    }

    /// <summary>Routes open/close state changes through the server so the NetworkVariable write is always authoritative.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void SetOpenServerRpc(bool open)
    {
        isOpen.Value = open;
    }

    IEnumerator WaitAndOpen()
    {
        isOpeningOrClosing = true;
        yield return new WaitForSeconds(.1f);
        SetOpenServerRpc(true);
        isOpeningOrClosing = false;
    }

    /// <summary>
    /// Returns the slot Transform for the given item name.
    /// For exam pages this is the next free slot in the FIFO queue; for ID card and
    /// Application it returns their dedicated slots.
    /// Returns null if the item type is unknown or all exam slots are full.
    /// </summary>
    public Transform GetSlotForPage(string itemName)
    {
        if (itemName == "ID card")        return idCardSlot;
        if (itemName == "Application")    return applicationSlot;

        // For any exam page, find the current slot this type already occupies (used
        // when resolving an existing placement from the server side after AddDocument),
        // or fall back to the next free slot when called from RequestAddToFolderServerRpc.
        if (_queueSlots != null)
        {
            Unity.Collections.FixedString64Bytes key = itemName;
            for (int i = 0; i < _queueSlots.Length; i++)
            {
                if (_queueSlots[i].Value == key)
                    return (i < examPageSlots.Length) ? examPageSlots[i] : null;
            }

            // Not yet in queue — return the next free slot (used during AddDocument flow).
            int free = GetFreeQueueSlotIndex();
            return (free >= 0 && free < examPageSlots.Length) ? examPageSlots[free] : null;
        }

        return null;
    }

    /// <summary>
    /// Registers a page into a slot using the same network-safe path that DropObject uses:
    /// PlaceInSlotServerRpc disables NT everywhere and broadcasts PlaceInSlotClientRpc so all
    /// clients register the document in FolderController.LateUpdate.
    /// </summary>
    private void PlacePageInSlotNetworked(PickableObject page, Transform slot)
    {
        NetworkObject slotOwner = slot.GetComponentInParent<NetworkObject>();
        if (slotOwner == null)
        {
            // Fallback: local constraint only (should never happen in practice).
            Debug.LogError($"[FolderController] PlacePageInSlotNetworked: no NetworkObject parent found for slot '{slot.name}' — falling back to local constraint.");
            page.SetParent(slot);
            return;
        }

        Debug.Log($"[FolderController] PlacePageInSlotNetworked: server prep for {page.name} → slot={slot.name} slotOwner={slotOwner.name} IsSpawned={page.NetworkObject?.IsSpawned}");

        // Server-side prep: release the constraint, hand ownership back to server, and stop NGO
        // from replicating parent changes. ExamNotebook.NotifyPagePlacedInFolderClientRpc (sent by
        // RequestAddToFolderServerRpc after AddDocument) handles all client-side detach and slot
        // registration via the notebook's NetworkObject, which reliably reaches all clients.
        page.RemoveParent();
        page.NetworkObject.RemoveOwnership();
        page.NetworkObject.AutoObjectParentSync = false;

        RegisterDocumentServerRpc(new NetworkObjectReference(page.NetworkObject));
    }

    // Mirrors PlayerPickupController.GetRelativePath so we can build slot paths from here.
    // Public so ExamNotebook.RequestAddToFolderServerRpc can compute the slot path when
    // broadcasting NotifyPagePlacedInFolderClientRpc.
    public static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root) return string.Empty;
        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return string.Join("/", parts);
    }

    public void AddDocument(PickableObject pickableObject, PlayerPickupController player, bool dropObject)
    {
        string itemName = pickableObject.ItemData.name;
        Debug.Log("Try add document");

        if (itemName == "ID card")
        {
            if (idCard != null) return;
            player.DropObject(idCardSlot);
            idCard = pickableObject.GetComponent<IDCard>();
            idCard.AddToFolder(this);
            OnDocumentAdded?.Invoke(pickableObject);
            // Sync the association to all other clients and notify the server for tutorial coroutines.
            SyncDocumentAddedServerRpc(new NetworkObjectReference(pickableObject.NetworkObject), itemName);
            return;
        }

        if (itemName == "Application")
        {
            if (application != null) return;
            player.DropObject(applicationSlot);
            application = pickableObject.GetComponent<ApplicationLetter>();
            application.AddToFolder(this);
            OnDocumentAdded?.Invoke(pickableObject);
            // Sync the association to all other clients and notify the server for tutorial coroutines.
            SyncDocumentAddedServerRpc(new NetworkObjectReference(pickableObject.NetworkObject), itemName);
            return;
        }

        // --- Evidence documents: any PickableObject with a FolderProofDocument component ---
        FolderProofDocument proofDoc = pickableObject.GetComponent<FolderProofDocument>();
        if (proofDoc != null)
        {
            AddEvidenceDocument(pickableObject, proofDoc, player);
            return;
        }

        // --- Exam pages: queue logic ---
        bool isExamPage = itemName is
            "Behavior Exam Page" or "Mutation Exam Page" or "Reality Exam Page" or
            "Documentation Exam Page" or "Biological Exam Page";

        if (!isExamPage) return;

        // One page per type.
        if (IsExamPageTypeInQueue(itemName)) return;

        int slotIndex = GetFreeQueueSlotIndex();
        if (slotIndex < 0)
        {
            Debug.LogWarning($"[FolderController] AddDocument: all {examPageSlots.Length} exam slots are full — cannot add {itemName}.");
            return;
        }

        Transform targetSlot = examPageSlots[slotIndex];
        ExamPage examPage = pickableObject.GetComponent<ExamPage>();

        if (dropObject)
        {
            // Direct-drop path: called on the local client from InteractWithItem.
            // The slot selection and NetworkVariable write must happen on the server
            // so all clients see the correct occupancy. We pass the slot index so the
            // server can write _examPageQueue and _queueSlots authoritatively.
            RegisterExamPageInQueueServerRpc(new NetworkObjectReference(pickableObject.NetworkObject), slotIndex);
            player.DropObject(targetSlot);
        }
        else
        {
            // Notebook path: always called on the server from RequestAddToFolderServerRpc.
            _examPageQueue[slotIndex] = examPage;
            _queueSlots[slotIndex].Value = itemName;
            PlacePageInSlotNetworked(examPage, targetSlot);
        }

        examPage.AddToFolder(this);
    }

    /// <summary>
    /// Places a FolderProofDocument item in the next free evidence slot, snapping it
    /// visually to the folder and registering it server-side for scoring.
    /// </summary>
    private void AddEvidenceDocument(PickableObject pickableObject, FolderProofDocument proofDoc, PlayerPickupController player)
    {
        // Find a free evidence slot.
        int freeSlot = -1;
        for (int i = 0; i < _evidenceSlots.Length; i++)
        {
            if (_evidenceSlots[i] != null && _evidenceSlots[i].childCount == 0)
            {
                freeSlot = i;
                break;
            }
        }

        if (freeSlot < 0)
        {
            Debug.LogWarning("[FolderController] All evidence slots are full — cannot add more evidence.");
            return;
        }

        // Snap the item to its slot (same pattern as ID card / application placement).
        player.DropObject(_evidenceSlots[freeSlot]);

        // Notify the suspect this evidence has been filed.
        proofDoc.OnPlacedInFolder(this);

        // Register server-side for scoring and despawn.
        RegisterEvidenceDocumentServerRpc(
            new NetworkObjectReference(pickableObject.NetworkObject),
            (int)proofDoc.Category);

        Debug.Log($"[FolderController] Evidence '{pickableObject.ItemData.name}' ({proofDoc.Category}) filed in slot {freeSlot}.");
    }

    /// <summary>
    /// Server-authoritative registration of a directly-dropped exam page into the queue.
    /// Called from AddDocument (dropObject=true) so the _examPageQueue and _queueSlots
    /// writes always happen on the server regardless of which client initiated the drop.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RegisterExamPageInQueueServerRpc(NetworkObjectReference pageRef, int slotIndex)
    {
        if (!pageRef.TryGet(out NetworkObject netObj)) return;
        ExamPage examPage = netObj.GetComponent<ExamPage>();
        if (examPage == null) return;
        if (slotIndex < 0 || slotIndex >= _examPageQueue.Length) return;

        _examPageQueue[slotIndex] = examPage;
        _queueSlots[slotIndex].Value = examPage.ItemData.name;

        // Apply the folder's current open state so a page added to a closed folder is
        // immediately non-interactable on all clients, and one added to an open folder
        // remains interactable — matching the SyncDocumentAddedServerRpc behaviour for
        // idCard and application.
        examPage.SetInteractableNetworked(isOpen.Value);

        Debug.Log($"[FolderController] RegisterExamPageInQueueServerRpc: registered {examPage.ItemData.name} at queue slot {slotIndex}");
    }

    public void RemoveDocument(PickableObject pickableObject, PlayerPickupController player)
    {
        string itemName = pickableObject.ItemData.name;
        Debug.Log("Try remove document");
        
        if (itemName == "ID card")
        {
            idCard = null;
            return;
        }
        
        if (itemName == "Application")
        {
            application = null;
            return;
        }

        // --- Exam pages: find, remove, and compact the queue ---
        int removedIndex = -1;
        for (int i = 0; i < _examPageQueue.Length; i++)
        {
            if (_examPageQueue[i] != null && _examPageQueue[i].ItemData.name == itemName)
            {
                removedIndex = i;
                break;
            }
        }

        if (removedIndex < 0) return;

        // Shift all entries after the removed index one step forward.
        int lastIndex = _examPageQueue.Length - 1;
        for (int i = removedIndex; i < lastIndex; i++)
        {
            _examPageQueue[i]    = _examPageQueue[i + 1];
            _queueSlots[i].Value = _queueSlots[i + 1].Value;

            // Shift the page's SocketFollow target to the new slot so it tracks correctly.
            if (_examPageQueue[i] != null)
            {
                ShiftDocumentSlotClientRpc(
                    new NetworkObjectReference(_examPageQueue[i].NetworkObject),
                    i);
            }
        }

        // Clear the last slot.
        _examPageQueue[lastIndex]    = null;
        _queueSlots[lastIndex].Value = default;
    }

    /// <summary>
    /// Broadcasts a slot-shift to all clients so every machine's SocketFollow component
    /// on the page points to the correct new slot Transform after a page is removed from the queue.
    /// </summary>
    [ClientRpc]
    private void ShiftDocumentSlotClientRpc(NetworkObjectReference pageRef, int newSlotIndex)
    {
        if (!pageRef.TryGet(out NetworkObject netObj)) return;
        PickableObject doc = netObj.GetComponent<PickableObject>();
        if (doc == null || newSlotIndex < 0 || newSlotIndex >= examPageSlots.Length) return;

        // Redirect the SocketFollow to the new slot — no list bookkeeping needed.
        doc.SetSocketFollow(examPageSlots[newSlotIndex]);
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
            // Route through the server so the NetworkVariable override is updated for all clients.
            // The local SetInteractable alone is overridden by ApplyNetworkInteractableState on any
            // client where _networkInteractableOverride != -1 (e.g. a tutorial-locked document).
            if (IsServer)
            {
                if (idCard != null)       idCard.SetInteractableNetworked(true);
                if (application != null)  application.SetInteractableNetworked(true);

                foreach (ExamPage examPage in _examPageQueue)
                {
                    if (examPage != null) examPage.SetInteractableNetworked(true);
                }
            }
        }
    }

    /// <summary>
    /// Returns the set of category type names (e.g. "PhysicalAnomaly") for every checked
    /// checkbox across all exam pages in this folder. Used by the verdict scoring system
    /// to determine which categories the player identified.
    /// Checklist items reference specific anomaly leaf classes (e.g. "NameWrongAnomaly"),
    /// so each checked item's full type hierarchy is walked up to and including its category
    /// base class — mirroring the matching done by <see cref="ExamContainsAnomaly"/> and
    /// <see cref="AnomalyController.HasActiveAnomalyOfCategory"/> — so a checked leaf-level
    /// item is correctly recognized as identifying its parent category.
    /// </summary>
    public HashSet<string> GetCheckedCategoryNames()
    {
        var result = new HashSet<string>();

        foreach (ExamPage examPage in _examPageQueue)
        {
            if (examPage == null) continue;
            foreach (ChecklistItem item in examPage.ChecklistItems)
            {
                if (item == null || !item.IsChecked || string.IsNullOrEmpty(item.AnomalyTypeName))
                    continue;

                result.Add(item.AnomalyTypeName);

                // Walk up the type hierarchy so a leaf anomaly name (e.g. "NameWrongAnomaly")
                // also registers its category ancestors (e.g. "DocumentationAnomaly").
                System.Type t = System.Type.GetType(item.AnomalyTypeName);
                while (t != null && t != typeof(Anomaly))
                {
                    result.Add(t.Name);
                    t = t.BaseType;
                }
            }
        }

        return result;
    }

    public bool ExamContainsAnomaly(Anomaly anomaly)
    {
        foreach (ExamPage examPage in _examPageQueue)
        {
            if (examPage == null) continue;
            Debug.Log("Checking exam page:" + examPage.name);

            ChecklistItem[] checklistItems = examPage.ChecklistItems;
            if (checklistItems == null) continue;

            foreach (ChecklistItem item in checklistItems)
            {
                // Walk the anomaly's type hierarchy so that a category-level reference
                // (e.g. AnomalyTypeName = "PhysicalAnomaly") matches any subclass
                // (e.g. BlueVeinsAnomaly : PhysicalAnomaly).
                System.Type t = anomaly.GetType();
                while (t != null)
                {
                    Debug.Log($"Checking: {item.AnomalyTypeName} vs {t.Name}, IsChecked: {item.IsChecked}");
                    if (item.AnomalyTypeName == t.Name && item.IsChecked)
                        return true;
                    if (t == typeof(Anomaly)) break;
                    t = t.BaseType;
                }
            }
        }

        return false;
    }

    public Anomaly[] GetAnomaliesInFolder()
    {
        List<Anomaly> anomaliesFound = new List<Anomaly>();

        foreach (ExamPage examPage in _examPageQueue)
        {
            if (examPage == null) continue;

            ChecklistItem[] checklistItems = examPage.ChecklistItems;
            foreach (ChecklistItem item in checklistItems)
            {
                if (item != null && item.IsChecked && item.AnomalyTypeReference is Anomaly anomaly)
                {
                    if (!anomaliesFound.Contains(anomaly))
                        anomaliesFound.Add(anomaly);
                }
            }
        }

        return anomaliesFound.ToArray();
    }

    /// <summary>
    /// Returns true if this folder is currently held by another player and contains a page
    /// matching <paramref name="itemName"/> in its synced queue slots.
    /// Safe to call on any client — uses only networked state (_holdingClientId, _queueSlots).
    /// </summary>
    public bool IsPageHeldByAnotherPlayer(string itemName)
    {
        if (!IsHeldByOtherPlayer) return false;
        if (_queueSlots == null) return false;

        Unity.Collections.FixedString64Bytes key = itemName;
        foreach (var slot in _queueSlots)
        {
            if (slot.Value == key) return true;
        }
        return false;
    }
}