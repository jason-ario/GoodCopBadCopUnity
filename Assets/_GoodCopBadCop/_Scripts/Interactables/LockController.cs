using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A padlock that must be opened with a specific key before the target <see cref="ILockable"/> can
/// be used.
///
/// Setup:
///  1. Assign <see cref="lockAnimator"/> to the padlock's Animator.
///  2. Assign <see cref="lockableTarget"/> to the <see cref="ToolsLocker"/> (or any ILockable).
///  3. In <c>itemsThatCanInteractWith</c> (inherited from Interactable) add the
///     <see cref="PickableItemData"/> of the required key so the interaction system routes the
///     key→padlock interaction through <see cref="InteractWithItem"/>.
/// </summary>
public class LockController : Interactable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string UnlockTrigger    = "Unlock";
    private const string LockedShakeTrigger = "LockedTriedOpening";

    // ── Serialized fields ─────────────────────────────────────────────────────

    [Header("Lock")]
    [Tooltip("Animator on the padlock mesh. Must have an 'Unlock' trigger parameter.")]
    [SerializeField] private Animator lockAnimator;

    [Tooltip("The MonoBehaviour on the target that implements ILockable (e.g. ToolsLocker).")]
    [SerializeField] private MonoBehaviour lockableTarget;

    [Tooltip("Unique identifier for this padlock. Must be unique across all locks in the save. " +
             "Used to persist unlock state across sessions.")]
    [SerializeField] private string _lockId;

    [Header("Audio")]
    [Tooltip("Played when a player tries to interact without the required key.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   lockedSound;
    [SerializeField] private AudioClip   unlockSound;

    [Header("Unlock Sequence")]
    [Tooltip("Seconds after the unlock trigger before child Rigidbodies go non-kinematic.")]
    [SerializeField] private float _physicsActivationDelay = 0.5f;
    [Tooltip("Seconds after physics activation before the padlock despawns.")]
    [SerializeField] private float _despawnDelay = 2f;

    // ── Private state ─────────────────────────────────────────────────────────

    private ILockable _lockable;

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Whether this padlock is currently locked.</summary>
    public bool IsLocked => _isLocked.Value;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _lockable = lockableTarget as ILockable;

        if (lockableTarget != null && _lockable == null)
            Debug.LogError($"[LockController] '{lockableTarget.name}' does not implement ILockable.", this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isLocked.OnValueChanged += OnIsLockedChanged;

        if (IsServer)
            CheckSavedUnlockState();
        else
            ApplyLockedState(_isLocked.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isLocked.OnValueChanged -= OnIsLockedChanged;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the player interacts with the padlock without holding the required key.
    /// Plays the locked-shake feedback.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value)
        {
            PlayLockedAnimationServerRpc();
            return;
        }
    }

    /// <summary>
    /// Called when the player is holding the required key and clicks the padlock.
    /// Triggers the server-authoritative unlock sequence and consumes the key.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        if (!_isLocked.Value) return;
        UnlockServerRpc(item.NetworkObject);
    }

    /// <summary>
    /// Unlocks the padlock without requiring a physical key — intended for scripted sequences
    /// (e.g. Vlad's Day 2 demo walk). Persists the unlock to save data when a lock ID is set.
    /// Must be called on the server.
    /// </summary>
    public void ForceUnlock()
    {
        if (!IsServer) return;
        if (!_isLocked.Value) return;

        if (!string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
            SaveDataManager.Instance.SaveUnlockedLock(_lockId);

        _isLocked.Value = false;
        _lockable?.Unlock();
        Debug.Log($"[LockController] ForceUnlock — lock '{_lockId}' unlocked by scripted sequence.");
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void UnlockServerRpc(NetworkObjectReference keyRef)
    {
        if (!_isLocked.Value) return;

        if (!string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
            SaveDataManager.Instance.SaveUnlockedLock(_lockId);

        _isLocked.Value = false;
        _lockable?.Unlock();

        // Consume the key — it is a one-use item.
        if (keyRef.TryGet(out NetworkObject keyObj))
            NetworkHelper.Despawn(keyObj);
    }

    /// <summary>
    /// Syncs the locked-shake animation and sound to all clients when the padlock is
    /// interacted with directly (without the required key).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void PlayLockedAnimationServerRpc() => PlayLockedAnimationClientRpc();

    [ClientRpc]
    private void PlayLockedAnimationClientRpc()
    {
        PlayLockedAnimation();

        if (audioSource != null && lockedSound != null)
            audioSource.PlayOneShot(lockedSound);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Server-only: called on spawn to check whether this lock was already unlocked in a previous
    /// session. If so, silently applies the unlock to the ILockable and despawns this object
    /// without playing any animation — it simply won't appear in the world.
    /// Otherwise ensures the ILockable is locked, so lockables that default to unlocked
    /// (e.g. <see cref="GateController"/>) are correctly locked at startup.
    /// </summary>
    private void CheckSavedUnlockState()
    {
        bool alreadyUnlocked = !string.IsNullOrEmpty(_lockId)
            && SaveDataManager.Instance != null
            && SaveDataManager.Instance.IsLockUnlocked(_lockId);

        if (alreadyUnlocked)
        {
            _isLocked.Value = false;
            _lockable?.Unlock();
            StartCoroutine(DespawnNextFrameCoroutine());
        }
        else
        {
            // Lock has not been unlocked (or has no persisted save state) — ensure the
            // ILockable reflects the locked state. This is important for lockables like
            // GateController that default to unlocked, and must run even when this padlock
            // has no _lockId or there's no SaveDataManager available.
            _lockable?.Lock();
            ApplyLockedState(_isLocked.Value);
        }
    }

    /// <summary>Waits one frame so the NetworkObject is fully spawned before despawning.</summary>
    private IEnumerator DespawnNextFrameCoroutine()
    {
        yield return null;
        NetworkObject.Despawn();
    }

    private void OnIsLockedChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
        {
            lockAnimator?.SetTrigger(UnlockTrigger);

            if (audioSource != null && unlockSound != null)
                audioSource.PlayOneShot(unlockSound);

            StartCoroutine(UnlockSequenceCoroutine());
        }

        ApplyLockedState(newValue);
    }

    /// <summary>
    /// Runs on all clients after the unlock NetworkVariable change.
    /// Waits for the shake animation, enables physics on child Rigidbodies so the pieces
    /// fall naturally, then despawns the NetworkObject from the server after a final delay.
    /// </summary>
    private IEnumerator UnlockSequenceCoroutine()
    {
        yield return new WaitForSeconds(_physicsActivationDelay);
        lockAnimator.enabled = false;
        
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = false;

        if (!IsServer) yield break;

        yield return new WaitForSeconds(_despawnDelay);
        NetworkObject.Despawn();
    }

    /// <summary>
    /// Keeps the locked state applied locally (currently a no-op placeholder for future
    /// visual state changes such as hiding/showing the shackle).
    /// </summary>
    private void ApplyLockedState(bool locked) { }

    /// <summary>
    /// Triggers the locked-shake animation on this padlock locally.
    /// Called either from <see cref="PlayLockedAnimationClientRpc"/> or directly by
    /// <see cref="ToolsLocker"/> from within its own ClientRpc.
    /// </summary>
    public void PlayLockedAnimation()
    {
        lockAnimator?.SetTrigger(LockedShakeTrigger);
    }
}
