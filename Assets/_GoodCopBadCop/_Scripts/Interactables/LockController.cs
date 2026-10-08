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

    [Tooltip("If greater than 0, this padlock automatically unlocks once the campaign reaches this " +
             "day (or later), regardless of whether a key was ever used. Checked on spawn — including " +
             "right after a save file is loaded — via SaveDataManager.CurrentDay. Set to 0 to disable.")]
    [SerializeField] private int _autoUnlockOnDay = 0;

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

    private const float TargetUnlockedGraceSeconds = 0.5f;

    private ILockable _lockable;
    private bool      _unlockVisualsPlayed;
    private Coroutine _targetUnlockedFallback;

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Server-only: set when this padlock was despawned as unlocked. The GameObject now survives
    /// its despawn (see <see cref="DespawnAsUnlocked"/>), so a silent auto-unlock that leaves
    /// <see cref="_isLocked"/> untouched must still report unlocked to readers like
    /// <see cref="GateController"/>.
    /// </summary>
    private bool _despawnedAsUnlocked;

    /// <summary>Whether this padlock is currently locked.</summary>
    public bool IsLocked => !_despawnedAsUnlocked && _isLocked.Value;

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

        // Resolve saved state before subscribing so a silent restore-unlock does not play the
        // unlock animation/sound on the host.
        if (IsServer)
            CheckSavedUnlockState();
        else if (!_isLocked.Value)
        {
            // Joined in the window between the server's silent unlock and its despawn.
            HideLocally();
            return;
        }
        else
            ApplyLockedState(_isLocked.Value);

        _isLocked.OnValueChanged += OnIsLockedChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isLocked.OnValueChanged -= OnIsLockedChanged;

        // Late joiners: the server keeps despawned padlocks in the scene (Despawn(false)), so NGO
        // lists them in the late-join sync and invokes OnNetworkDespawn here without ever spawning
        // them, leaving the GameObject active. Hide it so it is not visible or interactable.
        if (NetworkManager != null && NetworkManager.ShutdownInProgress) return;
        HideLocally();
    }

    private void HideLocally()
    {
        StopAllCoroutines();
        _targetUnlockedFallback = null;
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    protected override string DefaultInteractVerb => "Inspect";

    public override string GetItemUseVerb(PlayerInteractionController player, PickableObject item) => "Unlock";

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
        if (!IsServer || !IsSpawned) return;
        if (!IsLocked) return;

        if (!string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
            SaveDataManager.Instance.SaveUnlockedLock(_lockId);

        _isLocked.Value = false;
        _lockable?.Unlock();
        Debug.Log($"[LockController] ForceUnlock — lock '{_lockId}' unlocked by scripted sequence.");
    }

    /// <summary>
    /// Server-only: silently unlocks this padlock (no animation/sound) if <see cref="_autoUnlockOnDay"/>
    /// is set and <paramref name="day"/> has reached it. Covers day changes that happen after this
    /// padlock already spawned (debug day-skips, JumpToDay), which <see cref="CheckSavedUnlockState"/>
    /// can't see. Safe to call repeatedly. Returns true if it unlocked the padlock.
    /// </summary>
    public bool TryAutoUnlockForDay(int day)
    {
        if (!IsServer || !IsSpawned) return false;
        if (_autoUnlockOnDay <= 0 || day < _autoUnlockOnDay) return false;
        if (!_isLocked.Value) return false;

        if (!string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
            SaveDataManager.Instance.SaveUnlockedLock(_lockId);

        // Leave _isLocked untouched so OnIsLockedChanged doesn't play the unlock animation on
        // any peer; despawning hides the padlock everywhere (see OnNetworkDespawn).
        _lockable?.Unlock();
        DespawnAsUnlocked();
        Debug.Log($"[LockController] '{name}' auto-unlocked for Day {day} (threshold Day {_autoUnlockOnDay}).");
        return true;
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
    /// session, or should be auto-unlocked because <see cref="_autoUnlockOnDay"/> has been reached.
    /// If so, silently applies the unlock to the ILockable and despawns this object without playing
    /// any animation — it simply won't appear in the world.
    /// Otherwise ensures the ILockable is locked, so lockables that default to unlocked
    /// (e.g. <see cref="GateController"/>) are correctly locked at startup.
    /// </summary>
    private void CheckSavedUnlockState()
    {
        bool alreadyUnlocked = !string.IsNullOrEmpty(_lockId)
            && SaveDataManager.Instance != null
            && SaveDataManager.Instance.IsLockUnlocked(_lockId);

        // SaveDataManager.CurrentDay is loaded from disk in SaveDataManager.Awake(), which always
        // runs before any NetworkObject's OnNetworkSpawn — so this is correct immediately after
        // loading a save file, not just during normal play.
        bool dayUnlocked = _autoUnlockOnDay > 0
            && SaveDataManager.Instance != null
            && SaveDataManager.Instance.CurrentDay >= _autoUnlockOnDay;

        if (alreadyUnlocked || dayUnlocked)
        {
            if (dayUnlocked && !alreadyUnlocked && !string.IsNullOrEmpty(_lockId) && SaveDataManager.Instance != null)
                SaveDataManager.Instance.SaveUnlockedLock(_lockId);

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
        DespawnAsUnlocked();
    }

    /// <summary>
    /// Server-only: despawns this padlock WITHOUT destroying it. A destroyed in-scene NetworkObject
    /// is missing from NGO's late-join "despawned in-scene objects" list, so a late joiner would
    /// never be told about it and would keep showing the padlock. Kept (hidden by
    /// <see cref="OnNetworkDespawn"/>), it is listed and hidden on the late joiner too.
    /// </summary>
    private void DespawnAsUnlocked()
    {
        if (!IsServer || !IsSpawned) return;
        _despawnedAsUnlocked = true;
        NetworkObject.Despawn(false);
    }

    private void OnIsLockedChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            PlayUnlockVisuals();

        ApplyLockedState(newValue);
    }

    /// <summary>Plays the unlock trigger + sound and starts the physics/despawn sequence once.</summary>
    private void PlayUnlockVisuals()
    {
        if (_unlockVisualsPlayed) return;
        _unlockVisualsPlayed = true;

        if (lockAnimator != null)
            lockAnimator.SetTrigger(UnlockTrigger);

        if (audioSource != null && unlockSound != null)
            audioSource.PlayOneShot(unlockSound);

        StartCoroutine(UnlockSequenceCoroutine());
    }

    /// <summary>
    /// Client-side safety net, called by the <see cref="ILockable"/> owner (e.g. <see cref="ToolsLocker"/>)
    /// when its own replicated lock state becomes unlocked. The padlock is a separate in-scene
    /// NetworkObject, so a client can miss its unlock delta and/or despawn (e.g. when the server
    /// unlocks and despawns it while the client is still loading/synchronizing the scene) and
    /// would otherwise keep showing a padlock on an unlocked target.
    /// <para><paramref name="immediate"/> = true (late-join/spawn snapshot) hides at once unless the
    /// unlock is already playing; false waits briefly for the padlock's own replication, plays the
    /// unlock visuals locally if they never arrived, then hides.</para>
    /// No-op on the server, which owns the padlock lifecycle.
    /// </summary>
    public void NotifyTargetUnlocked(bool immediate)
    {
        if (this == null || !isActiveAndEnabled) return;
        // Not IsServer: that's per-behaviour and stays false if this padlock never spawned here.
        var netManager = Unity.Netcode.NetworkManager.Singleton;
        if (netManager != null && netManager.IsServer) return;

        if (immediate)
        {
            if (!_unlockVisualsPlayed)
                HideLocally();
            return;
        }

        if (_targetUnlockedFallback == null)
            _targetUnlockedFallback = StartCoroutine(TargetUnlockedFallbackCoroutine());
    }

    private IEnumerator TargetUnlockedFallbackCoroutine()
    {
        // Give the padlock's own replicated unlock a moment to arrive (normal path).
        yield return new WaitForSeconds(TargetUnlockedGraceSeconds);

        if (!_unlockVisualsPlayed)
            PlayUnlockVisuals();

        // Hide locally in case the server's despawn never reaches this padlock.
        yield return new WaitForSeconds(_physicsActivationDelay + _despawnDelay + TargetUnlockedGraceSeconds);
        _targetUnlockedFallback = null;
        HideLocally();
    }

    /// <summary>
    /// Runs on all clients after the unlock NetworkVariable change.
    /// Waits for the shake animation, enables physics on child Rigidbodies so the pieces
    /// fall naturally, then despawns the NetworkObject from the server after a final delay.
    /// </summary>
    private IEnumerator UnlockSequenceCoroutine()
    {
        yield return new WaitForSeconds(_physicsActivationDelay);
        if (lockAnimator != null)
            lockAnimator.enabled = false;
        
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = false;

        if (!IsServer) yield break;

        yield return new WaitForSeconds(_despawnDelay);
        DespawnAsUnlocked();
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
