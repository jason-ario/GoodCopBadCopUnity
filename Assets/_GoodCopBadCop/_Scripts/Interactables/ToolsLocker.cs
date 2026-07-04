using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ToolsLocker : Interactable, ILockable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string AnimOpenParam        = "Open";
    private const string AnimLockedShakeParam = "LockedTriedOpening";
    private const string StateClosed          = "Locker Closed";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on all clients whenever any tool locker transitions to open.
    /// Used by tutorial systems to detect when a player opens the locker.
    /// </summary>
    public static event Action OnAnyLockerOpened;

    // ── Serialized fields ─────────────────────────────────────────────────────

    [SerializeField] private Animator anim;

    [Header("Decor")]
    [Tooltip("Decoration GameObjects — activated as soon as the door begins to open, deactivated when fully closed.")]
    [SerializeField] private GameObject[] _decorations;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   lockerOpenSound;
    [SerializeField] private AudioClip   lockerCloseSound;
    [SerializeField] private AudioClip   lockerLockedSound;

    [SerializeField] private Transform                    lookTarget;
    [SerializeField] private PurchaseLocker[]             miniLockers;
    [SerializeField] private ToolLockerDiegeticController _diegeticController;

    [Tooltip("The LockController padlock on this locker. Animated alongside the door when locked.")]
    [SerializeField] private LockController _lockController;

    // ── Networked state ───────────────────────────────────────────────────────

    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<int>  viewerCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// When true the locker is physically locked and cannot be opened until unlocked via a key.
    /// Starts locked (true) so the locker is inaccessible on Day 1.
    /// </summary>
    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── ILockable ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsLocked => _isLocked.Value;

    /// <summary>Locks the locker. Must be called on the server.</summary>
    public void Lock()
    {
        if (!IsServer) return;
        _isLocked.Value = true;
    }

    /// <summary>Unlocks the locker so players can open it. Must be called on the server.</summary>
    public void Unlock()
    {
        if (!IsServer) return;
        _isLocked.Value = false;
    }

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Coroutine _decorCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnIsOpenChanged;

        // Snap decor to the current replicated state for late-joining clients.
        ApplyDecorImmediate(isOpen.Value);

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnIsOpenChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    [ContextMenu("Open")]
    public void ForceOpen() => OpenLockerServerRpc();

    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value)
        {
            PlayLockedTriedOpeningServerRpc();
            return;
        }

        Debug.Log("Toggle Tool Locker");

        if (_diegeticController != null)
            _diegeticController.Open(player, this);
        else
            UIController.Instance.OpenToolShop(lookTarget, this);

        OpenLockerServerRpc();
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    /// <summary>
    /// Hides the purchased shop item at <paramref name="itemIndex"/> on all clients.
    /// Called by the purchasing client immediately after a successful buy.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void HideShopItemServerRpc(int itemIndex) => HideShopItemClientRpc(itemIndex);

    [ClientRpc]
    private void HideShopItemClientRpc(int itemIndex) => _diegeticController?.HideItem(itemIndex);

    /// <summary>Called by each local client when they close the tool shop UI.
    /// Decrements the viewer count and closes the locker when no viewers remain.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyPlayerClosedServerRpc()
    {
        viewerCount.Value = Mathf.Max(0, viewerCount.Value - 1);
        if (viewerCount.Value == 0)
            CloseLockerInternal();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CloseLockerServerRpc() => CloseLockerInternal();

    [ServerRpc(RequireOwnership = false)]
    public void OpenLockerServerRpc()
    {
        viewerCount.Value++;
        isOpen.Value = true;
    }

    /// <summary>
    /// Broadcasts the locked-tried-opening feedback (door shake + padlock shake + sound) to all
    /// clients when a player attempts to open the locker while it is still locked.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void PlayLockedTriedOpeningServerRpc() => PlayLockedTriedOpeningClientRpc();

    [ClientRpc]
    private void PlayLockedTriedOpeningClientRpc()
    {
        anim.SetTrigger(AnimLockedShakeParam);
        _lockController?.PlayLockedAnimation();

        if (lockerLockedSound != null)
            audioSource.PlayOneShot(lockerLockedSound);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Server-only: called when a new day starts.
    /// Broadcasts a restock to all clients so sold-out items reappear in the locker.
    /// </summary>
    private void OnDayStart()
    {
        if (!IsServer) return;
        RestockClientRpc();
    }

    [ClientRpc]
    private void RestockClientRpc() => _diegeticController?.RestockItems();

    private void CloseLockerInternal()
    {
        isOpen.Value      = false;
        viewerCount.Value = 0;

        foreach (var miniLocker in miniLockers)
            miniLocker.CloseServerRpc();
    }

    private void OnIsOpenChanged(bool oldValue, bool newValue)
    {
        anim.SetBool(AnimOpenParam, newValue);

        if (newValue)
        {
            OnAnyLockerOpened?.Invoke();
            audioSource.PlayOneShot(lockerOpenSound);

            // Activate decorations immediately as the door begins to open.
            SetDecorActive(true);
        }
        else
        {
            audioSource.PlayOneShot(lockerCloseSound);

            // Deactivate decorations only once the door is fully closed.
            if (_decorCoroutine != null)
                StopCoroutine(_decorCoroutine);
            _decorCoroutine = StartCoroutine(WaitForFullyClosedThenHideDecor());
        }
    }

    /// <summary>
    /// Polls the Animator every frame until it finishes transitioning into
    /// <c>Locker Closed</c>, then hides the decorations.
    /// </summary>
    private IEnumerator WaitForFullyClosedThenHideDecor()
    {
        // Wait one frame so the Animator registers the new parameter value.
        yield return null;

        while (anim.IsInTransition(0) || !anim.GetCurrentAnimatorStateInfo(0).IsName(StateClosed))
            yield return null;

        SetDecorActive(false);
        _decorCoroutine = null;
    }

    private void SetDecorActive(bool active)
    {
        if (_decorations == null) return;
        foreach (var decoration in _decorations)
            if (decoration != null)
                decoration.SetActive(active);
    }

    /// <summary>
    /// Snaps decor to the correct state immediately without waiting for animation,
    /// used on spawn to match the authoritative network state.
    /// </summary>
    private void ApplyDecorImmediate(bool open)
    {
        anim.SetBool(AnimOpenParam, open);
        SetDecorActive(open);
    }
}
