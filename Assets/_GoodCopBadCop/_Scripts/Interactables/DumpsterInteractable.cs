using System.Collections;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable dumpster for the Take Out the Trash task.
///
/// Left-clicking the dumpster while holding a TrashBag triggers the throw sequence:
///   1. Player controls are locked.
///   2. A throw animation trigger is fired on the player animator (synced to all clients).
///   3. ReleaseHeldObjectForThrow() detaches the bag without calling DropServerRpc,
///      keeping NetworkTransform disabled so DOTween retains full control.
///   4. DOJump arcs the real bag into the dumpster.
///   5. The bag is despawned and controls are restored.
///
/// A world-space label hovers above the dumpster and is visible only while the player looks
/// at it. It shows "X/Capacity" in white, or "DUMPSTER FULL" in red when at capacity.
///
/// Prefab setup:
///   - NetworkObject + HighlightEffect + Collider (Interactable layer)
///   - Trash Bag PickableItemData assigned to itemsThatCanInteractWith
///   - Three child Transforms assigned to _throwTargets (positions inside the dumpster opening)
/// </summary>
public class DumpsterInteractable : Interactable
{
    private const string ThrowAnimTrigger   = "ThrowTrashBag";
    private const string InteractTextDefault = "Dumpster";
    private const string InteractTextFull    = "Dumpster Full";

    [Header("Throw Targets")]
    [Tooltip("Three positions inside the dumpster opening. One is chosen at random per throw.")]
    [SerializeField] private Transform[] _throwTargets = new Transform[3];

    [Header("Throw Settings")]
    [Tooltip("Seconds after the animation trigger fires before the bag visually leaves the hand.")]
    [SerializeField] private float _throwWindupDelay = 0.15f;
    [Tooltip("Peak height of the throw arc above the straight-line path.")]
    [SerializeField] private float _jumpHeight = 1.5f;
    [Tooltip("Total duration of the throw arc in seconds.")]
    [SerializeField] private float _jumpDuration = 0.45f;
    [SerializeField] private Ease _jumpEase = Ease.Linear;

    [Header("Capacity")]
    [Tooltip("Maximum number of trash bags this dumpster accepts.")]
    [SerializeField] private int _capacity = 3;

    [Header("World Label")]
    [Tooltip("The world-space Canvas GO that contains the label. Toggled on reticle hover.")]
    [SerializeField] private GameObject _labelRoot;
    [Tooltip("The TextMeshProUGUI on the label canvas.")]
    [SerializeField] private TextMeshProUGUI _labelText;

    [Header("Audio")]
    [SerializeField] private AudioClip _depositSound;
    [SerializeField] private AudioSource _audioSource;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _bagsDeposited = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True when this dumpster has reached its bag capacity.</summary>
    public bool IsFull => _bagsDeposited.Value >= _capacity;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractTextDefault;
        if (_labelRoot != null)
            _labelRoot.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _bagsDeposited.OnValueChanged += OnDepositCountChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;

        // Sync label to the current server state (handles late-joining clients).
        RefreshLabel();
        RefreshInteractText();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _bagsDeposited.OnValueChanged -= OnDepositCountChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    /// <summary>Empties the dumpster at the start of each new day.</summary>
    private void OnDayStart()
    {
        if (IsServer)
            _bagsDeposited.Value = 0;
    }

    private void LateUpdate()
    {
        // Billboard: keep the label facing the camera while it's visible.
        if (_labelRoot != null && _labelRoot.activeSelf && Camera.main != null)
        {
            _labelRoot.transform.LookAt(Camera.main.transform.position);
            _labelRoot.transform.Rotate(0f, 180f, 0f);
        }
    }

    // ── Highlight callbacks (called by PlayerInteractionController) ───────────

    protected override void OnHighlight()
    {
        if (_labelRoot != null)
            _labelRoot.SetActive(true);
    }

    protected override void OnStopHighlight()
    {
        if (_labelRoot != null)
            _labelRoot.SetActive(false);
    }

    // ── Interact ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called via left-click while holding a TrashBag (left-click with held item path).
    /// The Trash Bag PickableItemData must be listed in itemsThatCanInteractWith on the prefab.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        TrashBag bag = item as TrashBag;
        if (IsFull || bag == null) return;

        base.InteractWithItem(player, item);
        StartCoroutine(ThrowSequence(player, bag));
    }

    // ── Deposit (server-authoritative) ───────────────────────────────────────

    /// <summary>
    /// Increments this dumpster's deposit count then forwards to the global task tracker.
    /// Called by the local client at the end of the throw animation.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void DepositBagServerRpc()
    {
        if (IsFull) return;

        _bagsDeposited.Value = Mathf.Min(_bagsDeposited.Value + 1, _capacity);

        // Forward to task — already on server, so this executes inline.
        TakeOutTrashTask.Instance?.DepositBagServerRpc();
    }

    /// <summary>
    /// Resets this dumpster's deposit counter for the next task cycle.
    /// Called by TakeOutTrashTask.ResetTask() from the server.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ResetServerRpc()
    {
        _bagsDeposited.Value = 0;
    }

    // ── Throw sequence ───────────────────────────────────────────────────────

    private IEnumerator ThrowSequence(PlayerInteractionController player, TrashBag bag)
    {
        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim    = player.playerAnimationController;

        // ── 1. Lock controls ─────────────────────────────────────────────────
        movement.SetCanMove(false);
        movement.SetCanLook(false);
        player.SetCanInteract(false, string.Empty);

        // ── 2. Fire throw animation (synced to all clients) ──────────────────
        anim.SetAnimTrigger(ThrowAnimTrigger);
        anim.SetAnimBool("HoldingTrashBag", false);

        // ── 3. Pick throw target ──────────────────────────────────────────────
        Transform target = PickThrowTarget();

        // ── 4. Release the bag from the player's hand ─────────────────────────
        // ReleaseHeldObjectForThrow skips DropServerRpc so NetworkTransform is
        // never re-enabled — DOTween has full control of the bag's transform.
        TrashBag depositedBag = bag;
        player.pickupController.ReleaseHeldObjectForThrow();

        // ── 5. Windup delay before the bag visually moves ─────────────────────
        yield return new WaitForSeconds(_throwWindupDelay);

        // ── 6. Broadcast the throw arc to ALL clients ─────────────────────────
        // PlayThrowArcClientRpc runs DOJump on every client (including the
        // local one) so onlookers see the arc instead of the bag disappearing.
        depositedBag.PlayThrowArcClientRpc(target.position, _jumpHeight, _jumpDuration, (int)_jumpEase);

        yield return new WaitForSeconds(_jumpDuration);

        // ── 7. Deposit feedback ───────────────────────────────────────────────
        PlayDepositAudio();

        // ── 8. Despawn the bag ────────────────────────────────────────────────
        depositedBag.DespawnServerRpc();

        // ── 9. Notify server — increments dumpster counter and task progress ──
        DepositBagServerRpc();

        // ── 10. Restore player controls ───────────────────────────────────────
        movement.SetCanMove(true);
        movement.SetCanLook(true);
        player.SetCanInteract(true, string.Empty);
    }

    /// <summary>Picks a random non-null entry from _throwTargets; falls back to this transform.</summary>
    private Transform PickThrowTarget()
    {
        var valid = System.Array.FindAll(_throwTargets, t => t != null);

        if (valid.Length == 0)
        {
            Debug.LogWarning("[DumpsterInteractable] No throw targets assigned — using dumpster centre.");
            return transform;
        }

        return valid[Random.Range(0, valid.Length)];
    }

    // ── NetworkVariable callback ──────────────────────────────────────────────

    private void OnDepositCountChanged(int previous, int current)
    {
        RefreshLabel();
        RefreshInteractText();
    }

    private void RefreshInteractText()
    {
        interactText = IsFull ? InteractTextFull : InteractTextDefault;
    }

    // ── World-space label ─────────────────────────────────────────────────────

    private void RefreshLabel()
    {
        if (_labelText == null) return;

        if (IsFull)
        {
            _labelText.text  = "FULL";
            _labelText.color = Color.red;
        }
        else
        {
            _labelText.text  = $"{_bagsDeposited.Value}/{_capacity}";
            _labelText.color = Color.white;
        }
    }

    // ── Audio ─────────────────────────────────────────────────────────────────

    private void PlayDepositAudio()
    {
        if (_audioSource != null && _depositSound != null)
            _audioSource.PlayOneShot(_depositSound);
    }
}
