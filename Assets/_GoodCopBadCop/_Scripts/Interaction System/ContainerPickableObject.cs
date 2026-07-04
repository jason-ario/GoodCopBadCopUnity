using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A <see cref="PickableObject"/> that contains a fixed number of individual items that can
/// be extracted one at a time by pressing E. Left-click (LMB) still picks up the container
/// via the standard <see cref="PickableObject.Interact"/> path.
///
/// When the last item is extracted the container despawns automatically.
///
/// Subclasses must implement <see cref="BuildInteractText"/> to provide the label shown in
/// the player's reticle (e.g. "Extract Trash Bag (3 left)").
///
/// Examples:
///   <see cref="TrashBagRoll"/> — holds trash bags, extracted while the roll sits in the world.
///   (future) CigaretteCarton — holds cigarettes, carried in hand and extracted on demand.
/// </summary>
public abstract class ContainerPickableObject : PickableObject
{
    private const int DefaultCapacity = 5;

    [Header("Container")]
    [Tooltip("Total number of items this container holds.")]
    [SerializeField] private int _capacity = DefaultCapacity;

    [Tooltip("PickableItemData for the individual item dispensed on each extraction.")]
    [SerializeField] private PickableItemData _containedItemData;

    [Tooltip("Sound played on every client when an item is successfully extracted.")]
    [SerializeField] private AudioClip _extractSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _itemsRemaining = new(
        DefaultCapacity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Number of items left in this container.</summary>
    public int ItemsRemaining => _itemsRemaining.Value;

    /// <summary>True when all items have been extracted.</summary>
    public bool IsEmpty => _itemsRemaining.Value <= 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = BuildInteractText(DefaultCapacity);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _itemsRemaining.OnValueChanged += OnItemsRemainingChanged;

        // Server sets the authoritative count from the inspector value so prefab variants
        // with different capacities replicate correctly to all clients.
        if (IsServer)
            _itemsRemaining.Value = _capacity;

        // Always sync the text on spawn; OnValueChanged won't fire if the value equals the
        // default (e.g. both are already 5).
        interactText = BuildInteractText(_itemsRemaining.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _itemsRemaining.OnValueChanged -= OnItemsRemainingChanged;
    }

    private void OnItemsRemainingChanged(int previous, int current)
    {
        interactText = BuildInteractText(current);

        if (current < previous && _extractSound != null)
            SFXController.Instance.PlayAtPosition(_extractSound, transform.position);
    }

    /// <summary>
    /// Returns the reticle label for the current remaining count, e.g.
    /// <c>"Extract Trash Bag (3 left)"</c>.
    /// </summary>
    protected abstract string BuildInteractText(int itemsRemaining);

    /// <summary>Always shows the extract hint (key icon + action text) while this container is targeted.</summary>
    public override bool ShowInteractHint => true;

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player presses E while targeting this container (empty-handed).
    /// Spawns one contained item on the server and routes it directly to the requesting
    /// player's hands via a targeted ClientRpc.
    /// LMB pickup is inherited unchanged from <see cref="PickableObject.Interact"/>.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject != null) return;
        if (_itemsRemaining.Value <= 0) return;

        if (_containedItemData == null || _containedItemData.PickUpPrefab == null)
        {
            Debug.LogError($"[{GetType().Name}] _containedItemData is not assigned or missing a pickup prefab.");
            return;
        }

        int itemIndex = ItemDatabase.Instance.GetItemIndex(_containedItemData);
        if (itemIndex < 0)
        {
            Debug.LogError($"[{GetType().Name}] Contained item is not registered in ItemDatabase.");
            return;
        }

        ExtractItemServerRpc(itemIndex);
    }

    // ── Server RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the remaining count, spawns the contained item, directs the requesting
    /// client to pick it up, then decrements the counter. Despawns the container on the
    /// next frame when the last item is taken (giving the ClientRpc time to flush).
    /// RequireOwnership = false so any client can extract regardless of who owns the object.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ExtractItemServerRpc(int itemIndex, ServerRpcParams rpcParams = default)
    {
        if (_itemsRemaining.Value <= 0) return;

        PickableItemData itemData = ItemDatabase.Instance.GetItemByIndex(itemIndex);
        if (itemData == null || itemData.PickUpPrefab == null)
        {
            Debug.LogError($"[{GetType().Name}] Could not resolve PickableItemData for index {itemIndex}.");
            return;
        }

        // Spawn slightly above the container to avoid geometry clipping.
        GameObject spawnedObject = Instantiate(
            itemData.PickUpPrefab,
            transform.position + Vector3.up * 0.5f,
            Quaternion.identity);

        NetworkObject no = spawnedObject.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError($"[{GetType().Name}] Spawned prefab '{itemData.name}' has no NetworkObject.");
            Destroy(spawnedObject);
            return;
        }

        no.Spawn(true);

        ulong clientId = rpcParams.Receive.SenderClientId;
        GiveItemToPlayerClientRpc(
            new NetworkObjectReference(no),
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

        _itemsRemaining.Value--;

        if (_itemsRemaining.Value <= 0)
            StartCoroutine(DespawnNextFrame());
    }

    /// <summary>
    /// Waits one frame so all queued ClientRpcs are flushed before this NetworkObject
    /// is torn down, ensuring the pick-up message reaches the client first.
    /// </summary>
    private IEnumerator DespawnNextFrame()
    {
        yield return null;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    // ── Client RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Received only by the player who triggered the extraction. Resolves the spawned item
    /// and places it in the local player's hands via their <see cref="PlayerPickupController"/>.
    /// </summary>
    [ClientRpc]
    private void GiveItemToPlayerClientRpc(
        NetworkObjectReference itemRef,
        ClientRpcParams clientRpcParams = default)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetObj))
        {
            Debug.LogWarning($"[{GetType().Name}] GiveItemToPlayerClientRpc: could not resolve item NetworkObject.");
            return;
        }

        PickableObject item = itemNetObj.GetComponent<PickableObject>();
        if (item == null)
        {
            Debug.LogWarning($"[{GetType().Name}] GiveItemToPlayerClientRpc: spawned object has no PickableObject.");
            return;
        }

        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();
        if (ppc == null)
        {
            Debug.LogWarning($"[{GetType().Name}] GiveItemToPlayerClientRpc: could not find local PlayerPickupController.");
            return;
        }

        ppc.PickUpObject(item);
    }
}
