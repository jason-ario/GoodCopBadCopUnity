using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Scene-placed <see cref="NetworkBehaviour"/> that relays Day 2 server-driven state
/// changes to all connected clients.
///
/// Pattern mirrors <see cref="TutorialTaskSync"/>:
///   1. The server calls a public method on this singleton.
///   2. The method fires a <c>…ClientRpc</c> that executes on every client (including host).
///   3. The ClientRpc delegates back to <see cref="Day_02.Instance"/> for the local state change.
///
/// Requires a <c>NetworkObject</c> component on the same GameObject.
/// Place this component on a dedicated child under ---CampaignManager, named
/// "--- Day 02 Network Sync", alongside a <c>NetworkObject</c> component.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Day02NetworkSync : NetworkBehaviour
{
    public static Day02NetworkSync Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (Instance == this) Instance = null;
    }

    // ── Dead Animal ───────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the dead animal prop on all clients. Server-only.
    /// Called by <see cref="Day_02"/> during the post-shift out-back sequence.
    /// </summary>
    public void ActivateDeadAnimal()
    {
        if (!IsServer) return;
        ActivateDeadAnimalClientRpc();
    }

    [ClientRpc]
    private void ActivateDeadAnimalClientRpc()
    {
        Day_02.Instance?.ActivateDeadAnimalLocal();
    }
}
