using System.Collections;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable dumpster for the Take Out the Trash task.
///
/// When the local player interacts while holding a TrashBag (and the dumpster is not full):
///   1. Player controls are locked.
///   2. A throw animation trigger is fired on the player animator (synced to all clients).
///   3. The networked bag is dropped and despawned immediately.
///   4. A local-only visual proxy arcs via DOJump into a randomly chosen throw target.
///   5. Controls are restored; both the dumpster counter and TakeOutTrashTask are notified.
///
/// A world-space label hovers above the dumpster and is visible only while the player looks
/// at it. It shows "X/Capacity" in white, or "DUMPSTER FULL" in red when at capacity.
///
/// Prefab setup:
///   - NetworkObject + HighlightEffect + Collider (Interactable layer)
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

    [Header("Throw Proxy")]
    [Tooltip("Visual-only prefab instantiated on remote clients to show the bag arc. Assign the TrashBag visual (non-networked) prefab.")]
    [SerializeField] private GameObject _throwProxyPrefab;

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
    /// Called by PlayerInteractionController on the local client.
    /// Requires the player to be carrying a TrashBag and the dumpster to have capacity.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (IsFull) return;

        TrashBag bag = player.pickupController.HeldObject as TrashBag;
        if (bag == null) return;

        base.Interact(player);
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

        // ── 3. Cache bag world pose before the hand releases it ───────────────
        Vector3    bagWorldPos = bag.transform.position;
        Quaternion bagWorldRot = bag.transform.rotation;

        // ── 3b. Pick throw target now so remotes use the same destination ─────
        Transform target = PickThrowTarget();

        // ── 3c. Tell all remote clients to animate a proxy arc ────────────────
        ShowThrowProxyServerRpc(NetworkManager.Singleton.LocalClientId, bagWorldPos, bagWorldRot, target.position);

        // ── 4. Release and despawn the networked bag ──────────────────────────
        // Cache reference before DropObject() nulls the pickup controller's held ref.
        TrashBag depositedBag = bag;
        player.pickupController.DropObject();
        depositedBag.DespawnServerRpc();

        // ── 5. Build a local-only visual proxy at the cached hand position ────
        // DOJump drives this proxy with no NetworkTransform conflict.
        GameObject proxy = BuildVisualProxy(depositedBag, bagWorldPos, bagWorldRot);

        // ── 6. Windup delay before the bag visually moves ─────────────────────
        yield return new WaitForSeconds(_throwWindupDelay);

        // ── 7. DOJump the proxy to the pre-selected target ────────────────────
        bool jumpDone = false;

        proxy.transform
            .DOJump(target.position, _jumpHeight, numJumps: 1, _jumpDuration)
            .SetEase(_jumpEase)
            .OnComplete(() => jumpDone = true);

        yield return new WaitUntil(() => jumpDone);

        // ── 8. Deposit feedback and cleanup ───────────────────────────────────
        PlayDepositAudio();
        Destroy(proxy);

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

    // ── Remote proxy arc ─────────────────────────────────────────────────────

    /// <summary>
    /// Relays throw proxy parameters through the server so every remote client can
    /// animate a local bag arc without a networked object.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ShowThrowProxyServerRpc(ulong throwingClientId, Vector3 startPos, Quaternion startRot, Vector3 targetPos)
    {
        ShowThrowProxyClientRpc(throwingClientId, startPos, startRot, targetPos);
    }

    [ClientRpc]
    private void ShowThrowProxyClientRpc(ulong throwingClientId, Vector3 startPos, Quaternion startRot, Vector3 targetPos)
    {
        // The throwing client already runs a proxy locally inside ThrowSequence.
        if (NetworkManager.Singleton.LocalClientId == throwingClientId) return;

        if (_throwProxyPrefab == null)
        {
            Debug.LogWarning("[DumpsterInteractable] _throwProxyPrefab is not assigned — remote clients won't see the bag arc.");
            return;
        }

        StartCoroutine(RemoteProxyThrowCoroutine(startPos, startRot, targetPos));
    }

    private IEnumerator RemoteProxyThrowCoroutine(Vector3 startPos, Quaternion startRot, Vector3 targetPos)
    {
        GameObject proxy = Instantiate(_throwProxyPrefab, startPos, startRot);

        yield return new WaitForSeconds(_throwWindupDelay);

        bool jumpDone = false;
        proxy.transform
            .DOJump(targetPos, _jumpHeight, numJumps: 1, _jumpDuration)
            .SetEase(_jumpEase)
            .OnComplete(() => jumpDone = true);

        yield return new WaitUntil(() => jumpDone);

        Destroy(proxy);
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

    // ── Visual proxy ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a non-networked, physics-free copy of the bag's visible meshes at the given
    /// world pose. Safe to DOTween and Destroy locally without any Netcode conflicts.
    /// </summary>
    private static GameObject BuildVisualProxy(TrashBag bag, Vector3 worldPos, Quaternion worldRot)
    {
        var root = new GameObject("TrashBagThrowProxy");
        root.transform.SetPositionAndRotation(worldPos, worldRot);

        foreach (MeshRenderer mr in bag.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            var child = new GameObject(mr.gameObject.name);
            child.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
            child.transform.SetParent(root.transform, worldPositionStays: true);
            child.transform.localScale = mr.transform.lossyScale;

            child.AddComponent<MeshFilter>().sharedMesh        = mf.sharedMesh;
            child.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
        }

        return root;
    }

    // ── Audio ─────────────────────────────────────────────────────────────────

    private void PlayDepositAudio()
    {
        if (_audioSource != null && _depositSound != null)
            _audioSource.PlayOneShot(_depositSound);
    }
}
