using System;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A trash bag that can be picked up, filled with JunkItems, and deposited in a
/// DumpsterInteractable. Extends PickableObject to integrate with the existing item
/// hold/drop system.
///
/// Fill level is reflected on the mesh through the "Key 1" blend shape on the
/// SkinnedMeshRenderer: weight 100 = empty bag, weight 0 = fully stuffed bag.
/// The weight is driven by the networked junk count so all clients see the same shape.
///
/// Prefab requirements (add in Inspector):
///   - NetworkObject
///   - HighlightEffect  (required by Interactable)
///   - ParentConstraint (required by PickableObject)
///   - SkinnedMeshRenderer with a "Key 1" blend shape
///   - Collider on the Interactable layer
///   - PickableItemData ScriptableObject assigned in the "Item Data" field
/// Must be registered as a Network Prefab in the NetworkManager.
/// </summary>
public class TrashBag : PickableObject, IAmmoProvider
{
    private const string JunkBlendShapeName = "Key 1";
    private const int    BlendShapeNotFound  = -1;

    [Header("Junk Collection")]
    [Tooltip("Maximum number of junk items this bag can hold before it is considered full.")]
    [SerializeField] private int _maxJunkCapacity = 3;

    [Tooltip("Duration in seconds for the blend shape to smoothly transition to the new fill level.")]
    [SerializeField] private float _blendShapeTweenDuration = 0.4f;

    [Header("Audio")]
    [Tooltip("Sound played on all clients each time a junk item is collected into this bag.")]
    [SerializeField] private AudioClip _useSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _junkCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local references ──────────────────────────────────────────────────────

    private SkinnedMeshRenderer _skinnedMesh;
    private int                 _blendShapeIndex = BlendShapeNotFound;
    private Tween               _blendShapeTween;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns true when the bag has reached its maximum junk capacity.</summary>
    public bool IsFull => _junkCount.Value >= _maxJunkCapacity;

    /// <summary>Current number of junk items collected into this bag.</summary>
    public int JunkCount => _junkCount.Value;

    /// <summary>Maximum junk items this bag can hold (inspector-configurable).</summary>
    public int MaxJunkCapacity => _maxJunkCapacity;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _junkCount.Value;
    public float MaxAmmo => _maxJunkCapacity;
    public event Action OnAmmoChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        _skinnedMesh = GetComponent<SkinnedMeshRenderer>();
        CacheBlendShapeIndex();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _junkCount.OnValueChanged += OnJunkCountChanged;

        // Apply the current value immediately so late-joining clients start with
        // the correct blend shape weight rather than the default empty pose.
        SnapBlendShape(_junkCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _junkCount.OnValueChanged -= OnJunkCountChanged;
    }

    // ── Junk collection ───────────────────────────────────────────────────────

    /// <summary>
    /// Increments the junk count by one. SERVER ONLY — call directly from server-side
    /// logic (e.g. from JunkItem's ServerRpc) to avoid a double-hop through another RPC.
    /// </summary>
    public void AddJunk()
    {
        Debug.Assert(IsServer, "[TrashBag] AddJunk must only be called on the server.");

        if (_junkCount.Value < _maxJunkCapacity)
            _junkCount.Value++;
    }

    // ── Blend shape ───────────────────────────────────────────────────────────

    private void CacheBlendShapeIndex()
    {
        if (_skinnedMesh == null || _skinnedMesh.sharedMesh == null) return;

        _blendShapeIndex = _skinnedMesh.sharedMesh.GetBlendShapeIndex(JunkBlendShapeName);

        if (_blendShapeIndex == BlendShapeNotFound)
        {
            Debug.LogWarning(
                $"[TrashBag] Blend shape '{JunkBlendShapeName}' not found on mesh " +
                $"'{_skinnedMesh.sharedMesh.name}'. Fill level will not be reflected visually.");
        }
    }

    private void OnJunkCountChanged(int previous, int current)
    {
        OnAmmoChanged?.Invoke();
        UpdateBlendShapeSmooth(current);

        if (_useSound != null && SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(_useSound, transform.position);
    }

    /// <summary>
    /// Snaps the "Key 1" blend shape to the correct weight for <paramref name="junkCount"/>
    /// without any animation. Used on spawn so late-joining clients start in the right pose.
    /// </summary>
    private void SnapBlendShape(int junkCount)
    {
        if (_skinnedMesh == null || _blendShapeIndex == BlendShapeNotFound) return;

        _blendShapeTween?.Kill();
        _skinnedMesh.SetBlendShapeWeight(_blendShapeIndex, TargetBlendWeight(junkCount));
    }

    /// <summary>
    /// Smoothly tweens the "Key 1" blend shape to the target weight for
    /// <paramref name="junkCount"/> over <see cref="_blendShapeTweenDuration"/> seconds.
    /// Weight 100 = completely empty bag, weight 0 = fully stuffed bag.
    /// </summary>
    private void UpdateBlendShapeSmooth(int junkCount)
    {
        if (_skinnedMesh == null || _blendShapeIndex == BlendShapeNotFound) return;

        float from   = _skinnedMesh.GetBlendShapeWeight(_blendShapeIndex);
        float to     = TargetBlendWeight(junkCount);

        _blendShapeTween?.Kill();
        _blendShapeTween = DOVirtual
            .Float(from, to, _blendShapeTweenDuration,
                   w => _skinnedMesh.SetBlendShapeWeight(_blendShapeIndex, w))
            .SetEase(Ease.InOutSine);
    }

    /// <summary>Converts a junk count into the corresponding blend shape weight (0–100).</summary>
    private float TargetBlendWeight(int junkCount)
    {
        float fillRatio = _maxJunkCapacity > 0 ? (float)junkCount / _maxJunkCapacity : 0f;
        return (1f - fillRatio) * 100f;
    }

    // ── Throw arc ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcasts a DOTween throw arc to all clients so onlooker clients also see
    /// the bag fly into the dumpster rather than disappearing from mid-air.
    /// Called by DumpsterInteractable after ReleaseHeldObjectForThrow() on the
    /// throwing client; runs on every client including the thrower.
    /// </summary>
    /// <param name="targetPosition">World-space landing point inside the dumpster.</param>
    /// <param name="jumpHeight">Peak height of the arc above the straight-line path.</param>
    /// <param name="jumpDuration">Total arc duration in seconds.</param>
    /// <param name="ease">DOTween Ease cast to int for RPC serialization.</param>
    [ClientRpc]
    public void PlayThrowArcClientRpc(Vector3 targetPosition, float jumpHeight, float jumpDuration, int ease)
    {
        transform.DOKill();
        transform.DOJump(targetPosition, jumpHeight, numJumps: 1, jumpDuration)
                 .SetEase((Ease)ease);
    }
}
