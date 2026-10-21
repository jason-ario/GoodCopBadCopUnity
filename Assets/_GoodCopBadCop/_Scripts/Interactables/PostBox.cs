using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PostBox / mailbox interactable.
///
/// Mutant Activity threat: players deposit MutantBits here to fill the box, then call HQ for
/// pickup and a coupon reward (see <see cref="CollectableContainer"/>).
///
/// Daily newspaper: <see cref="DailyNewspaperSpawnManager"/> calls <see cref="PlaceNewspaperServer"/>
/// when today's paper lands in the box. While it waits, the mailbox (not the paper) is the target
/// and is held highlighted; Interact takes the paper straight into the player's hands, after which
/// the paper is a normal pickup and the mailbox highlight is released.
///
/// Setup requirements:
///   - NetworkObject on the PostBox GameObject (already present in scene).
///   - HighlightEffect (required by Interactable).
///   - Collider set to the Interactable layer.
///   - The MutantBit PickableItemData asset added to itemsThatCanInteractWith in the Inspector.
///   - Set _capacity to the desired max bits per collection (default 10).
///   - Set _couponRewardPerCollection to the desired coupon payout.
/// </summary>
public class PostBox : CollectableContainer
{
    [Header("Mail Audio")]
    [Tooltip("Played on every client when mail (the postman's newspaper) is put in the mailbox.")]
    [SerializeField] private AudioClip _mailDropSound;
    [SerializeField, Range(0f, 1f)] private float _mailDropVolume = 1f;
    [Tooltip("3D AudioSource on the mailbox the mail sound plays through. Auto-resolved from this " +
             "GameObject if left empty.")]
    [SerializeField] private AudioSource _mailAudioSource;

    // NetworkObjectId of the newspaper waiting in the box; 0 = empty.
    private readonly NetworkVariable<ulong> _waitingNewspaperId = new(
        0UL,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True while today's newspaper is waiting in the mailbox.</summary>
    public bool HasNewspaper => IsSpawned && _waitingNewspaperId.Value != 0UL;

    protected override void Awake()
    {
        base.Awake();

        if (_mailAudioSource == null)
            _mailAudioSource = GetComponent<AudioSource>();
        if (_mailAudioSource != null)
            GoodCopBadCop.Audio.GameAudioMixer.Route(_mailAudioSource);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _waitingNewspaperId.OnValueChanged += OnWaitingNewspaperChanged;
        ApplyNewspaperHighlight();
    }

    public override void OnNetworkDespawn()
    {
        _waitingNewspaperId.OnValueChanged -= OnWaitingNewspaperChanged;
        SetForceHighlight(false, HighlightHold.MailboxNewspaper);
        base.OnNetworkDespawn();
    }

    // ── Newspaper (server API) ────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Registers <paramref name="paper"/> as waiting in the mailbox: the paper stops
    /// being a target, the mailbox becomes the target and is highlighted.
    /// </summary>
    public void PlaceNewspaperServer(Newspaper paper, bool playMailSound)
    {
        if (!IsServer || paper == null || !paper.IsSpawned) return;

        ClearNewspaperServer();
        paper.SetInMailboxServer(true);
        _waitingNewspaperId.Value = paper.NetworkObjectId;

        if (playMailSound)
            PlayMailDropSoundClientRpc();
    }

    /// <summary>Server-only. Empties the mailbox (e.g. before the paper is despawned).</summary>
    public void ClearNewspaperServer()
    {
        if (!IsServer) return;

        Newspaper paper = ResolveWaitingNewspaper();
        if (paper != null)
            paper.SetInMailboxServer(false);

        _waitingNewspaperId.Value = 0UL;
    }

    private Newspaper ResolveWaitingNewspaper()
    {
        ulong id = _waitingNewspaperId.Value;
        if (id == 0UL || NetworkManager == null || NetworkManager.SpawnManager == null) return null;

        return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject netObj)
            ? netObj.GetComponent<Newspaper>()
            : null;
    }

    // ── Interact ──────────────────────────────────────────────────────────────

    public override bool ShowsInteractPrompt(PlayerInteractionController player)
    {
        if (HasNewspaper)
            return player.pickupController == null || player.pickupController.CanPickUpIntoHand;
        return base.ShowsInteractPrompt(player);
    }

    public override string GetInteractVerb(PlayerInteractionController player)
        => HasNewspaper ? Loc.N("Take newspaper") : base.GetInteractVerb(player);

    public override void Interact(PlayerInteractionController player)
    {
        if (HasNewspaper)
        {
            if (player.pickupController != null && !player.pickupController.CanPickUpIntoHand) return;
            TakeNewspaperServerRpc();
            return;
        }

        base.Interact(player);
    }

    /// <summary>
    /// Server takes the paper out of the box (first request wins) and routes it to the requesting
    /// player's hands via a targeted ClientRpc. Clearing the mailbox state re-enables the paper as
    /// a normal pickup and releases the mailbox highlight on every client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void TakeNewspaperServerRpc(ServerRpcParams rpcParams = default)
    {
        Newspaper paper = ResolveWaitingNewspaper();
        if (paper == null || paper.IsHeld)
        {
            ClearNewspaperServer();
            return;
        }

        ClearNewspaperServer();

        GiveNewspaperClientRpc(
            new NetworkObjectReference(paper.NetworkObject),
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } }
            });
    }

    /// <summary>Received only by the player who took the paper. Puts it in their hands.</summary>
    [ClientRpc]
    private void GiveNewspaperClientRpc(NetworkObjectReference paperRef, ClientRpcParams rpcParams = default)
    {
        if (!paperRef.TryGet(out NetworkObject paperNO)) return;

        PickableObject paper = paperNO.GetComponent<PickableObject>();
        if (paper == null) return;

        PlayerPickupController ppc = NetworkManager.Singleton.LocalClient?.PlayerObject
            ?.GetComponent<PlayerPickupController>();

        ppc?.PickUpObject(paper);
    }

    // ── Highlight / audio ─────────────────────────────────────────────────────

    private void OnWaitingNewspaperChanged(ulong previous, ulong current) => ApplyNewspaperHighlight();

    private void ApplyNewspaperHighlight()
        => SetForceHighlight(_waitingNewspaperId.Value != 0UL, HighlightHold.MailboxNewspaper);

    [ClientRpc]
    private void PlayMailDropSoundClientRpc()
    {
        if (_mailDropSound == null) return;

        if (_mailAudioSource != null)
            _mailAudioSource.PlayOneShot(_mailDropSound, _mailDropVolume);
        else
            Debug.LogWarning("[PostBox] No AudioSource on the mailbox — mail drop sound skipped.", this);
    }

    // ── Mutant bits ───────────────────────────────────────────────────────────

    public override string GetItemUseVerb(PlayerInteractionController player, PickableObject item) => Loc.N("Deposit");

    /// <summary>
    /// Called by PlayerInteractionController when the player left-clicks the PostBox
    /// while holding a MutantBit. Despawns the bit and deposits it into the container.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        MutantBit bit = item as MutantBit;

        if (IsFull || IsAwaitingPickup || bit == null) return;

        base.InteractWithItem(player, item);

        // Release the bit from the player's hand (skips DropServerRpc so the bit
        // is not re-enabled as an interactable before it is despawned).
        player.pickupController.ReleaseHeldObjectForThrow();

        // Despawn the bit network-wide.
        bit.DespawnServerRpc();

        // Increment the fill counter on the server.
        DepositServerRpc();
    }

    protected override string GetDefaultInteractText() => $"Post Box ({FillCount}/{Capacity})";
    protected override string GetFullInteractText()    => "Call HQ for Pickup";
}
