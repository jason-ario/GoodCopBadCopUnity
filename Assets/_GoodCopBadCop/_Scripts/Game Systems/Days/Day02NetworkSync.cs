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

    // ── Mail Sorting Tutorial ─────────────────────────────────────────────────

    /// <summary>
    /// Notifies all clients that Vlad's tool locker sequence has finished and the sorting-mail
    /// tutorial overlay should show. Server-only. Called by <see cref="Day_02"/> right after the
    /// tool locker dialogue completes.
    /// </summary>
    public void ShowMailSortingTutorial()
    {
        if (!IsServer) return;
        ShowMailSortingTutorialClientRpc();
    }

    [ClientRpc]
    private void ShowMailSortingTutorialClientRpc()
    {
        Day_02.Instance?.ShowMailSortingTutorialLocal();
    }

    // ── Fence Repair Tutorial ───────────────────────────────────────────────────

    /// <summary>
    /// Notifies all clients that Vlad's tool locker sequence has finished and the "Fix Perimeter
    /// Fences" objective should be added to the tutorial objective list. Server-only. Called by
    /// <see cref="Day_02"/> right after the tool locker dialogue completes.
    /// </summary>
    public void ShowFixFencesObjective()
    {
        if (!IsServer) return;
        ShowFixFencesObjectiveClientRpc();
    }

    [ClientRpc]
    private void ShowFixFencesObjectiveClientRpc()
    {
        Day_02.Instance?.EnsureFixFencesObjective();
    }
}
