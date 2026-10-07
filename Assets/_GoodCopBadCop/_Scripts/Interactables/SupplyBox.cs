using System.Collections.Generic;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

public class SupplyBox : PickableObject
{
    public bool canPickUp = false;
    [SerializeField] private Animator _animator;
    [SerializeField] private GameObject contents;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openClip;
    [SerializeField] private AudioClip _closeClip;
    bool isOpen = false;

    private static readonly int BoxOpenHash = Animator.StringToHash("BoxOpen");

    /// <summary>
    /// Items spawned into this box. Once an item is picked up by a player it is
    /// removed from this list so closing the box never re-locks it.
    /// </summary>
    private readonly List<PickableObject> _registeredItems = new List<PickableObject>();

    /// <summary>Networked authoritative state of <see cref="canPickUp"/>. Synced to all clients.</summary>
    private NetworkVariable<bool> _networkCanPickUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Parent transform used to attach per-day items during delivery. Falls back to this transform if contents is unassigned.</summary>
    public Transform ContentsParent => contents != null ? contents.transform : transform;

    /// <summary>
    /// True when no spawned item is still slotted into this box. Computed on demand from
    /// replicated per-item state (containment flag + slot owner), so it is correct on the
    /// server, host, and every client without any event bookkeeping that could go stale
    /// (missed pickup events, despawns while contained, checkpoint restores, late joins).
    /// </summary>
    public bool IsEmpty => GetItemsStillInBox(null) == 0;

    /// <summary>
    /// Counts spawned items still contained in this box and not held by anyone. Optionally
    /// collects their names for diagnostics.
    /// </summary>
    public int GetItemsStillInBox(List<string> names)
    {
        NetworkManager nm = NetworkManager;
        if (nm == null || nm.SpawnManager == null || NetworkObject == null) return 0;

        int count = 0;
        foreach (NetworkObject netObj in nm.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || netObj == NetworkObject) continue;
            if (!netObj.TryGetComponent(out PickableObject item)) continue;
            if (item.IsHeld || !item.IsContainedInSupplyBoxOf(NetworkObject)) continue;

            count++;
            names?.Add(item.name);
        }
        return count;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        
    }

    // ── Network Lifecycle ─────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCanPickUp.OnValueChanged += OnNetworkCanPickUpChanged;
        canPickUp = _networkCanPickUp.Value;
        UpdateInteractText();
    }

    public override void OnNetworkDespawn()
    {
        // Never leave contained items invisible if the box goes away while hidden for the guidebook.
        SetContentsHiddenForGuidebook(false);

        // Unlock any items that are still registered (e.g. box destroyed while carrying items).
        // Must run before base.OnNetworkDespawn() so item NetworkVariables are still active.
        if (IsServer)
        {
            foreach (PickableObject item in _registeredItems)
            {
                if (item != null && item.IsSpawned)
                    item.UnlockInteractableNetworked();
            }
        }

        base.OnNetworkDespawn();
        _networkCanPickUp.OnValueChanged -= OnNetworkCanPickUpChanged;
    }

    private void OnNetworkCanPickUpChanged(bool previous, bool current)
    {
        canPickUp = current;
        UpdateInteractText();
    }

    private void UpdateInteractText()
    {
        // Prompts are input-agnostic now: the reticle shows the key icon / "Hold E to open|close".
        interactText = "Supply Box";
    }

    // ── Item Registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Registers a spawned item so the box can manage its interactability (lock while the box
    /// is closed/carried, unlock when open). Emptiness is not derived from this list — see
    /// <see cref="IsEmpty"/>.
    /// </summary>
    public void RegisterItem(PickableObject item)
    {
        if (item == null || _registeredItems.Contains(item)) return;
        _registeredItems.Add(item);
    }

    /// <summary>Clears all registered items, e.g. when the box is despawned for a new delivery.</summary>
    public void ClearRegisteredItems() => _registeredItems.Clear();

    /// <summary>
    /// Server-only: drops registered items that have left the box (taken, released from
    /// containment, or despawned) so later open/close/carry lock changes never touch them.
    /// </summary>
    private void PruneRegisteredItems()
    {
        _registeredItems.RemoveAll(item =>
            item == null || !item.IsSpawned || !item.IsContainedInSupplyBoxOf(NetworkObject));
    }

    // ── Server-Side Item Lock Helpers ─────────────────────────────────────────

    /// <summary>Unlocks all registered items so normal holder-based interactability applies.</summary>
    private void UnlockItemsOnServer()
    {
        PruneRegisteredItems();
        foreach (PickableObject item in _registeredItems)
            item.UnlockInteractableNetworked();
    }

    /// <summary>Permanently locks all registered items regardless of holder state.</summary>
    private void LockItemsOnServer()
    {
        PruneRegisteredItems();
        foreach (PickableObject item in _registeredItems)
            item.LockInteractableNetworked();
    }

    /// <summary>Locks only items that are not currently held by a player.</summary>
    private void LockUnheldItemsOnServer()
    {
        PruneRegisteredItems();
        foreach (PickableObject item in _registeredItems)
            if (!item.IsHeld) item.LockInteractableNetworked();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnlockItemsServerRpc() => UnlockItemsOnServer();

    [ServerRpc(RequireOwnership = false)]
    private void LockItemsServerRpc() => LockItemsOnServer();

    // ── SetCanPickUp ──────────────────────────────────────────────────────────

    /// <summary>Sets <see cref="canPickUp"/> on all clients via the server.</summary>
    public void SetCanPickUpNetworked(bool value)
    {
        if (IsServer)
            _networkCanPickUp.Value = value;
        else
            SetCanPickUpServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetCanPickUpServerRpc(bool value) => _networkCanPickUp.Value = value;

    // ── Delivery RPCs ─────────────────────────────────────────────────────────

    /// <summary>Resets the box to its closed, non-pickable state for a fresh delivery.</summary>
    [ClientRpc]
    public void ResetForDeliveryClientRpc()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, false);
        UpdateInteractText();
    }

    /// <summary>Immediately enables interaction components on all clients, bypassing NetworkVariable latency.</summary>
    [ClientRpc]
    public void FinalizeDeliveryClientRpc()
    {
        SetInteractable(true);
        canPickUp = true;
        UpdateInteractText();
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Tap Interact — pick up the box (once it's allowed to be carried).
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (canPickUp)
            base.Interact(player);
    }

    /// <summary>Only offer the tap prompt when a tap would actually pick the box up.</summary>
    public override bool ShowsInteractPrompt(PlayerInteractionController player) =>
        canPickUp && base.ShowsInteractPrompt(player);

    /// <summary>Hold Interact — open / close. Always available, held item or not.</summary>
    public override string GetHoldInteractVerb(PlayerInteractionController player) =>
        isOpen ? "close" : "open";

    /// <summary>Hold Interact — toggle open / closed.</summary>
    public override void InteractHold(PlayerInteractionController player)
    {
        if (!isOpen)
            OpenBoxNetworked();
        else
            CloseBoxNetworked();
    }

    // ── Open / Close (networked) ──────────────────────────────────────────────

    /// <summary>Unlocks registered items then triggers the open animation on all clients.</summary>
    public void OpenBoxNetworked()
    {
        if (IsServer)
        {
            UnlockItemsOnServer();
            OpenBoxClientRpc();
        }
        else
            OpenBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void OpenBoxServerRpc()
    {
        UnlockItemsOnServer();
        OpenBoxClientRpc();
    }

    [ClientRpc]
    private void OpenBoxClientRpc()
    {
        if (!isOpen)
        {
            OpenBox();
            UpdateInteractText();
        }
    }

    /// <summary>Locks remaining (un-picked-up) items then triggers the close animation on all clients.</summary>
    public void CloseBoxNetworked()
    {
        if (IsServer)
        {
            LockUnheldItemsOnServer();
            CloseBoxClientRpc();
        }
        else
            CloseBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CloseBoxServerRpc()
    {
        LockUnheldItemsOnServer();
        CloseBoxClientRpc();
    }

    [ClientRpc]
    private void CloseBoxClientRpc()
    {
        if (isOpen)
        {
            CloseBox();
            UpdateInteractText();
        }
    }

    // ── SetInteractable ───────────────────────────────────────────────────────

    public override void SetInteractable(bool value)
    {
        base.SetInteractable(value);

        if (TryGetComponent(out BoxCollider boxCollider))
            boxCollider.enabled = value;

        UpdateInteractText();
    }

    // ── Local Visual State ────────────────────────────────────────────────────

    void OpenBox()
    {
        isOpen = true;
        if (contents != null)
            contents.SetActive(true);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, true);
        if (_audioSource != null && _openClip != null)
            _audioSource.PlayOneShot(_openClip);
    }

    void CloseBox()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, false);
        if (_audioSource != null && _closeClip != null)
            _audioSource.PlayOneShot(_closeClip);
    }

    // ── Box Pickup / Drop ─────────────────────────────────────────────────────

    public override void OnPickedUp()
    {
        base.OnPickedUp();
        // Lock all registered items while the box is being carried.
        if (IsServer)
            LockItemsOnServer();
        else
            LockItemsServerRpc();
    }

    public override void OnDropped()
    {
        base.OnDropped();
        // Contents must never stay invisible once the box leaves the hand.
        SetContentsHiddenForGuidebook(false);
        // Restore item interactability only if the box was open when put down.
        if (!isOpen) return;
        if (IsServer)
            UnlockItemsOnServer();
        else
            UnlockItemsServerRpc();
    }

    // ── Guidebook ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Renderers on contained items this box hid for the guidebook, so exactly those are restored.
    /// Contained items follow the box via a ParentConstraint instead of being children, so hiding
    /// the box GameObject alone leaves them floating in front of the camera / body guidebook.
    /// </summary>
    private readonly List<Renderer> _contentsHiddenForGuidebook = new List<Renderer>();

    public override void OnHiddenForGuidebook()
    {
        base.OnHiddenForGuidebook();
        SetContentsHiddenForGuidebook(true);
    }

    public override void OnShownAfterGuidebook()
    {
        base.OnShownAfterGuidebook();
        SetContentsHiddenForGuidebook(false);
    }

    protected override void OnHiddenForGuidebookNetworked(bool hidden) =>
        SetContentsHiddenForGuidebook(hidden);

    private void SetContentsHiddenForGuidebook(bool hidden)
    {
        if (!hidden)
        {
            foreach (Renderer r in _contentsHiddenForGuidebook)
                if (r != null) r.enabled = true;
            _contentsHiddenForGuidebook.Clear();
            return;
        }

        NetworkManager nm = NetworkManager;
        if (nm == null || nm.SpawnManager == null || NetworkObject == null) return;

        foreach (NetworkObject netObj in nm.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || netObj == NetworkObject) continue;
            if (!netObj.TryGetComponent(out PickableObject item)) continue;
            if (item.IsHeld || !item.IsContainedInSupplyBoxOf(NetworkObject)) continue;

            foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                _contentsHiddenForGuidebook.Add(r);
            }
        }
    }
}
