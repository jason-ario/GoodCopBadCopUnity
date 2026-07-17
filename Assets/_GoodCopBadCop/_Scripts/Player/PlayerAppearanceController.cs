using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Activates and deactivates GameObjects based on whether this player instance
/// is Player 1 (host / ServerClientId) or Player 2 (any non-host client).
///
/// Assign each player's root appearance objects (body, arms rig, etc.) to the
/// corresponding array in the Inspector. On NetworkSpawn the correct set is
/// enabled and the other is disabled on every client, including late-joiners.
/// </summary>
public class PlayerAppearanceController : NetworkBehaviour
{
    [SerializeField]
    [Tooltip("GameObjects to activate when this player is Player 1 (host).")]
    private GameObject[] _player1Objects = System.Array.Empty<GameObject>();

    [SerializeField]
    [Tooltip("GameObjects to activate when this player is Player 2 (non-host client).")]
    private GameObject[] _player2Objects = System.Array.Empty<GameObject>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ApplyAppearance();
    }

    /// <summary>
    /// Enables the correct set of appearance objects and disables the other.
    /// </summary>
    private void ApplyAppearance()
    {
        bool isPlayer1 = OwnerClientId == NetworkManager.ServerClientId;

        SetActive(_player1Objects, isPlayer1);
        SetActive(_player2Objects, !isPlayer1);
    }

    private static void SetActive(GameObject[] objects, bool active)
    {
        foreach (var go in objects)
        {
            if (go == null)
            {
                Debug.LogWarning("[PlayerAppearanceController] A null entry was found in an appearance array and will be skipped.");
                continue;
            }

            go.SetActive(active);
        }
    }
}
