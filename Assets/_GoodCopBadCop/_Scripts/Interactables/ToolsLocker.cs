using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ToolsLocker : Interactable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string AnimOpenParam = "Open";
    private const string StateClosed   = "Locker Closed";

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on all clients whenever any tool locker transitions to open.
    /// Used by tutorial systems to detect when a player opens the locker.
    /// </summary>
    public static event Action OnAnyLockerOpened;

    // ── Serialized fields ─────────────────────────────────────────────────────

    [SerializeField] private Animator anim;

    [Header("Decor")]
    [Tooltip("Root Decorations GameObject — activated as soon as the door begins to open, deactivated when fully closed.")]
    [SerializeField] private GameObject _decorations;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip   lockerOpenSound;
    [SerializeField] private AudioClip   lockerCloseSound;

    [SerializeField] private Transform                    lookTarget;
    [SerializeField] private PurchaseLocker[]             miniLockers;
    [SerializeField] private ToolLockerDiegeticController _diegeticController;

    // ── Networked state ───────────────────────────────────────────────────────

    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<int>  viewerCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Coroutine _decorCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnIsOpenChanged;

        // Snap decor to the current replicated state for late-joining clients.
        ApplyDecorImmediate(isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnIsOpenChanged;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    [ContextMenu("Open")]
    public void ForceOpen() => OpenLockerServerRpc();

    public override void Interact(PlayerInteractionController player)
    {
        Debug.Log("Toggle Tool Locker");

        if (_diegeticController != null)
            _diegeticController.Open(player, this);
        else
            UIController.Instance.OpenToolShop(lookTarget, this);

        OpenLockerServerRpc();
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

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

    // ── Private helpers ───────────────────────────────────────────────────────

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
        if (_decorations != null)
            _decorations.SetActive(active);
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
