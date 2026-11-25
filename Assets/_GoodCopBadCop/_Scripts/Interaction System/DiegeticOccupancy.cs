using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks which player (if any) currently occupies a diegetic-view interactable
/// (Tools Locker, Mini Fridge, Bunker Door, Electrical Panel, Lever, Power Switch) and prevents a
/// second player from entering the same view while another player is already using it.
/// Attach to the same GameObject as the interactable's <see cref="NetworkObject"/>.
///
/// Server-authoritative rules (identical for host and remote clients):
///   - A claim is keyed by the RPC sender, never by a client-supplied id.
///   - Re-claiming a slot you already hold succeeds (stale self-claim never reads "busy").
///   - A release only clears the slot if the sender is the current occupant, so a client whose
///     claim lost a race can't free someone else's slot.
///   - A losing claim is told so (<see cref="ClaimRejectedClientRpc"/>) and its view is closed.
///   - The slot is freed automatically if the occupant disconnects.
///
/// Local leak guard: when <see cref="TryClaim"/> is given the view it is claiming for, the
/// claiming peer releases automatically as soon as that view is not open. Any path that claims
/// and then fails to open (dialogue / cutscene refusal, missing view) or closes without releasing
/// can therefore no longer leave the interactable "busy" for everyone else.
/// </summary>
public class DiegeticOccupancy : NetworkBehaviour
{
    private static string BusyMessage => Loc.T("Interaction is busy");
    private const ulong NoOccupant = ulong.MaxValue;

    private readonly NetworkVariable<ulong> _occupantClientId = new NetworkVariable<ulong>(
        NoOccupant,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>True while any player currently occupies this interactable.</summary>
    public bool IsOccupied => _occupantClientId.Value != NoOccupant;

    /// <summary>True while a player other than the local one occupies this interactable.</summary>
    public bool IsOccupiedByOther => IsOccupied && _occupantClientId.Value != LocalClientIdOrNone;

    /// <summary>
    /// True from the moment this peer sends a claim until it releases. On a remote client the
    /// replicated <see cref="IsOccupied"/> lags the claim by a round trip, so an immediate
    /// rollback (e.g. the view refused to open) would otherwise skip the release RPC and leave
    /// the interactable stuck "busy" once the claim lands on the server.
    /// </summary>
    private bool _claimedLocally;

    /// <summary>View the local claim was made for; watched so the claim can't outlive it.</summary>
    private DiegeticViewController _claimView;

    private ulong LocalClientIdOrNone =>
        NetworkManager != null ? NetworkManager.LocalClientId : NoOccupant;

    /// <summary>
    /// Attempts to claim occupancy for <paramref name="player"/>. If another player already
    /// occupies the interactable, shows a "busy" notification and returns false. Otherwise sends a
    /// server-authoritative claim and returns true.
    /// </summary>
    /// <param name="view">
    /// Optional view this claim is for. When given, the claim is released automatically whenever
    /// that view is not open (refused to open, closed by any path), and closed if the server
    /// rejects the claim because another player got there first.
    /// </param>
    public bool TryClaim(PlayerInteractionController player, DiegeticViewController view = null)
    {
        if (IsOccupiedByOther)
        {
            UIController.Instance?.ShowErrorNotification(BusyMessage);
            return false;
        }

        if (player != null)
        {
            _claimedLocally = true;
            _claimView = view;
            ClaimServerRpc();
        }

        return true;
    }

    /// <summary>
    /// Releases the local peer's claim. Safe to call even when not currently occupied. The server
    /// ignores it unless this peer is the current occupant.
    /// </summary>
    public void Release()
    {
        bool heldByMe = IsOccupied && _occupantClientId.Value == LocalClientIdOrNone;
        if (!heldByMe && !_claimedLocally) return;

        _claimedLocally = false;
        _claimView = null;
        if (IsSpawned) ReleaseServerRpc();
    }

    /// <summary>
    /// Server-only: frees the slot if <paramref name="clientId"/> is the current occupant. Use from
    /// server code acting on behalf of a client (e.g. a "player closed the view" ServerRpc).
    /// </summary>
    public void ReleaseForClientServer(ulong clientId)
    {
        if (!IsServer) return;
        if (_occupantClientId.Value == clientId)
            _occupantClientId.Value = NoOccupant;
    }

    /// <summary>Server-only: frees the slot unconditionally (e.g. the interactable is being reset).</summary>
    public void ForceReleaseServer()
    {
        if (!IsServer) return;
        _occupantClientId.Value = NoOccupant;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        _claimedLocally = false;
        _claimView = null;
    }

    private void Update()
    {
        // Leak guard: a local claim must never outlive the view it was made for. The claim and
        // Open() happen in the same call, so by the time Update runs the view is either open or
        // was refused / has since closed.
        if (_claimedLocally && _claimView != null && !_claimView.IsOpen)
            Release();
    }

    // A client that quits mid-interaction (Alt+F4, crash, timeout) never sends ReleaseServerRpc,
    // so the server must free the slot itself. Otherwise the interactable stays "busy" for good,
    // even after the player reconnects, because they come back with a new client ID.
    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || NetworkManager.Singleton == null || NetworkManager.Singleton.ShutdownInProgress) return;
        if (_occupantClientId.Value == clientId)
            _occupantClientId.Value = NoOccupant;
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ClaimServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Ignore a claim that arrives for a client that already left, so it can't lock the slot
        // after OnClientDisconnected has already run.
        if (NetworkManager != null && !NetworkManager.ConnectedClients.ContainsKey(senderId)) return;

        ulong current = _occupantClientId.Value;
        if (current == senderId) return; // Already ours — idempotent re-claim.

        if (current != NoOccupant)
        {
            // Lost the race: another player's claim landed first.
            ClaimRejectedClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
            });
            return;
        }

        _occupantClientId.Value = senderId;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseServerRpc(ServerRpcParams rpcParams = default)
    {
        ReleaseForClientServer(rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ClaimRejectedClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!_claimedLocally) return;

        DiegeticViewController view = _claimView;
        _claimedLocally = false;
        _claimView = null;

        UIController.Instance?.ShowErrorNotification(BusyMessage);
        if (view != null && view.IsOpen)
            view.Close();
    }
}
