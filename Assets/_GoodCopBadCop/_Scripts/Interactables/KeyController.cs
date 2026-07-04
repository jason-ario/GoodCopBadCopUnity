using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A one-use key item that unlocks a matching <see cref="LockController"/>.
///
/// Setup:
///  1. Set <see cref="_lockId"/> to the exact same value as the target
///     <see cref="LockController._lockId"/>.
///  2. Assign the key's <see cref="PickableItemData"/> asset to the <c>itemData</c>
///     field (inherited from <see cref="PickableObject"/>).
///  3. Reference that same <see cref="PickableItemData"/> in
///     <see cref="Interactable.itemsThatCanInteractWith"/> on the <see cref="LockController"/>
///     so the interaction system routes a key→padlock click through
///     <see cref="LockController.InteractWithItem"/>.
/// </summary>
public class KeyController : PickableObject
{
    [Tooltip("Must match the Lock ID on the target LockController.")]
    [SerializeField] private string _lockId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server only: if the matching lock was already unlocked in a previous session,
        // this key was already consumed — despawn it silently next frame.
        if (IsServer && !string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
        {
            if (SaveDataManager.Instance.IsLockUnlocked(_lockId))
                StartCoroutine(DespawnNextFrameCoroutine());
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Waits one frame so the NetworkObject is fully spawned before despawning.</summary>
    private IEnumerator DespawnNextFrameCoroutine()
    {
        yield return null;
        NetworkHelper.Despawn(NetworkObject);
    }
}
