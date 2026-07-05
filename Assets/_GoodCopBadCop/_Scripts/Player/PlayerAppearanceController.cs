using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Swaps SkinnedMeshRenderer materials and meshes based on player index.
/// Player 1 is the host (clientId 0). Player 2 is any non-host client.
///
/// Runs inside OnNetworkSpawn, which fires on every client — including late-joiners
/// and pure observers — guaranteeing consistent visuals across the network without
/// additional RPCs or NetworkVariables.
/// </summary>
public class PlayerAppearanceController : NetworkBehaviour
{
    /// <summary>
    /// Maps a single SkinnedMeshRenderer to its Player 2 overrides.
    /// Leave a field empty / null to keep the prefab default for that property.
    /// </summary>
    [Serializable]
    public struct AppearanceEntry
    {
        [Tooltip("The SkinnedMeshRenderer to target for this entry.")]
        public SkinnedMeshRenderer renderer;

        [Header("Player 2 Overrides")]
        [Tooltip("Replacement mesh for Player 2. Leave null to keep the prefab mesh.")]
        public Mesh player2Mesh;

        [Tooltip("Replacement material set for Player 2. Leave empty to keep the prefab materials.")]
        public Material[] player2Materials;
    }

    [SerializeField]
    [Tooltip("Renderers whose mesh and/or materials should be swapped when this player is Player 2.")]
    private AppearanceEntry[] _appearanceEntries = Array.Empty<AppearanceEntry>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        //ApplyAppearance();
    }

    /// <summary>
    /// Evaluates the owner's client ID and applies the appropriate appearance overrides.
    /// Called on every client automatically through OnNetworkSpawn.
    /// </summary>
    private void ApplyAppearance()
    {
        bool isPlayer2 = OwnerClientId != NetworkManager.ServerClientId;

        if (!isPlayer2)
            return;

        foreach (var entry in _appearanceEntries)
        {
            if (entry.renderer == null)
            {
                Debug.LogWarning("[PlayerAppearanceController] An AppearanceEntry has a null renderer and will be skipped.", this);
                continue;
            }

            if (entry.player2Mesh != null)
                entry.renderer.sharedMesh = entry.player2Mesh;

            if (entry.player2Materials is { Length: > 0 })
                entry.renderer.sharedMaterials = entry.player2Materials;
        }
    }
}
