using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.WSA;
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

    [Header("Documents")] 
    [SerializeField] private MeshRenderer photoID;
    [SerializeField] EntryPermit entryPermit;
    
    [Header("Set Up")]
    [SerializeField] private AudioClip folderPlaceClip;
    [SerializeField] Animator anim;

    /// <summary>IK target position for the camera (first-person) arm at the top of the stamp arc.</summary>
    public Transform stampUpTarget;
    /// <summary>IK target position for the camera (first-person) arm at the bottom of the stamp arc.</summary>
    public Transform stampDownTarget;

    /// <summary>
    /// IK target position for the body (third-person) arm at the top of the stamp arc.
    /// Calibrated to the body skeleton's shoulder origin so the arm reaches the folder correctly
    /// from an observer's perspective. Falls back to stampUpTarget when left unassigned.
    /// </summary>
    [Tooltip("Body-arm IK target for the top of the stamp arc. Needs separate calibration from the camera-arm target. Falls back to stampUpTarget if unassigned.")]
    public Transform bodyStampUpTarget;

    /// <summary>
    /// IK target position for the body (third-person) arm at the bottom of the stamp arc.
    /// Falls back to stampDownTarget when left unassigned.
    /// </summary>
    [Tooltip("Body-arm IK target for the bottom of the stamp arc. Falls back to stampDownTarget if unassigned.")]
    public Transform bodyStampDownTarget;

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
    private NetworkVariable<bool> isHandedOff = new NetworkVariable<bool>();
    public List<PickableObject> documents;

    /// <summary>
    /// Server-authoritative list of documents currently inside this folder.
    /// Populated via RegisterDocumentServerRpc so CleanupSpawnedFolder can despawn
    /// them on the server even though InteractWithItem only fires on the local client.
    /// </summary>
    private readonly List<NetworkObjectReference> _serverDocuments = new List<NetworkObjectReference>();

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
    }

    public override void OnNetworkSpawn()
    {
        // Sync visual state on spawn and when variables change
        isOpen.OnValueChanged += (oldVal, newVal) => anim.SetBool("Open", newVal);

        // Late-joiner sync: apply stamp visual immediately if already stamped.
        if (isStamped.Value)
            stampContainer.PlaceStamp(_syncedStampType.Value);

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

    public void OnHandOff()
    {
        isHandedOff.Value = true;
        SetOpenServerRpc(false);
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
    private void StartUseStampServerRpc(ulong interactingPlayerId, StampContainer.StampType stampType)
    {
        if (isStamping) return;
        if (isOpen.Value) return;
        isStamping = true;
        isStamped.Value = true;

        StartUseStampClientRpc(interactingPlayerId, stampType);
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

        // Freeze the body lean for the duration of the stamp so the spine/shoulder
        // don't shift and fight the IK targets that were baked at sequence start.
        if (isStampingLocalPlayer)
        {
            ppc.PlayerAnimationController.SetBodyLeanDirect(0f);
            ppc.PlayerAnimationController.SuppressLocalBodyLean = true;
        }

        ppc.GetComponent<PlayerMovementController>().LookAtTarget(transform);

        // Resolve body-arm targets, falling back to the camera-arm targets when unassigned.
        Transform bodyUp   = bodyStampUpTarget   != null ? bodyStampUpTarget   : stampUpTarget;
        Transform bodyDown = bodyStampDownTarget != null ? bodyStampDownTarget : stampDownTarget;

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
            // Restore lean to zero — PlayerMovementController will resume driving it naturally.
            ppc.PlayerAnimationController.SetBodyLeanDirect(0f);
            ppc.PlayerAnimationController.SuppressLocalBodyLean = false;
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
        yield return new WaitForSeconds(.4f);
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
            return;
        }

        if (itemName == "Application")
        {
            if (application != null) return;
            player.DropObject(applicationSlot);
            application = pickableObject.GetComponent<ApplicationLetter>();
            application.AddToFolder(this);
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
            if(idCard != null){ idCard.SetInteractable(true); }
            if(application != null){ application.SetInteractable(true); }
        }
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
                Debug.Log($"Checking: {item.AnomalyTypeName} vs {anomaly.GetType().Name}, IsChecked: {item.IsChecked}");
                if (item.AnomalyTypeName == anomaly.GetType().Name && item.IsChecked)
                    return true;
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
}