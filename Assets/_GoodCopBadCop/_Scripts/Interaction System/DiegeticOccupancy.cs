using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks which player (if any) currently occupies a diegetic-view interactable
/// (Tools Locker, Mini Fridge, Bunker Door, Electrical Panel) and prevents a second
/// player from entering the same view while another player is already using it.
/// Attach to the same GameObject as the interactable's <see cref="NetworkObject"/>.
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

    /// <summary>
    /// True from the moment this peer sends a claim until it releases. On a remote client the
    /// replicated <see cref="IsOccupied"/> lags the claim by a round trip, so an immediate
    /// rollback (e.g. the view refused to open) would otherwise skip the release RPC and leave
    /// the interactable stuck "busy" once the claim lands on the server.
    /// </summary>
    private bool _claimedLocally;

    /// <summary>
    /// Attempts to claim occupancy for <paramref name="player"/>. If the interactable is
    /// already occupied by someone else, shows a "busy" notification and returns false.
    /// Otherwise claims it (server-authoritative) and returns true.
    /// </summary>
    public bool TryClaim(PlayerInteractionController player)
    {
        if (IsOccupied)
        {
            UIController.Instance?.ShowErrorNotification(BusyMessage);
            return false;
        }

        if (player != null)
        {
            ClaimServerRpc(player.OwnerClientId);
            _claimedLocally = true;
        }

        return true;
    }

    /// <summary>Releases occupancy. Safe to call even when not currently occupied.</summary>
    public void Release()
    {
        if (!IsOccupied && !_claimedLocally) return;
        _claimedLocally = false;
        ReleaseServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ClaimServerRpc(ulong clientId)
    {
        if (_occupantClientId.Value == NoOccupant)
            _occupantClientId.Value = clientId;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseServerRpc()
    {
        _occupantClientId.Value = NoOccupant;
    }
}
