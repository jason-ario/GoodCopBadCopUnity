using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// An interactable coin slot panel that accepts a Kill Coin to activate the Kill Machine.
///
/// Full insertion sequence (owner client only):
///   1.  Player controls are locked.
///   2.  Player body rotates toward the panel (LookAtTarget).
///   3.  Camera DOTweens to _camPos and body lean is applied over _cameraMoveDuration.
///   4.  Right-arm IK is anchored to _rightArmIKTarget and blended on.
///   5.  "InsertCoin" anim trigger fires and "HoldingCoin" bool is cleared.
///   6.  The coin is released from the hand (ParentConstraint removed, NT stays off).
///       Kill Coin.useRightIK is false, so DisableArmIKs() inside ReleaseHeldObjectForThrow
///       does NOT touch the IK we just set — it remains active through _insertDelay.
///   7.  After _insertDelay: IK blends off, body lean resets, camera returns.
///       A ClientRpc fires so every client snaps + slides the coin from _coinEntrance
///       to _coinAllIn over _coinTravelDuration using a SmoothStep lerp.
///   8.  After _coinTravelDuration: coin is despawned and the Kill Machine is activated.
///   9.  After _cameraReturnDuration: right-arm mask is disabled, controls are restored.
///
/// Prefab / scene setup:
///   - NetworkObject + HighlightEffect + Collider on the Interactable layer.
///   - Kill Coin PickableItemData in itemsThatCanInteractWith.
///   - Child Transforms: "Coin Entrance Pos", "Coin All In Pos", "Cam Pos".
///   - A separate "Hand IK Target" child Transform (or reuse Coin Entrance Pos) for _rightArmIKTarget.
///   - "InsertCoin" trigger in both the player body and arms Animator Controllers.
/// </summary>
public class CoinSlotPanel : Interactable
{
    private const string InsertCoinTrigger = "InsertCoin";
    private const string HoldingCoinBool   = "HoldingCoin";
    private const string InteractTextReady = "Coin Slot";
    private const string InteractTextUsed  = "Coin Slot (Used)";

    // ── Camera & IK ───────────────────────────────────────────────────────────

    [Header("Camera & IK")]
    [Tooltip("Child Transform the player camera DOTweens to during the insertion sequence.")]
    [SerializeField] private Transform _camPos;

    [Tooltip("World Transform the right-arm IK anchors to while the insert animation plays.")]
    [SerializeField] private Transform _rightArmIKTarget;

    [Tooltip("Seconds the camera takes to reach _camPos (mirrors SwitchButton).")]
    [SerializeField] private float _cameraMoveDuration = 0.5f;

    [Tooltip("Seconds the camera takes to return to the normal position after the coin is released.")]
    [SerializeField] private float _cameraReturnDuration = 0.25f;

    // ── Timing ────────────────────────────────────────────────────────────────

    [Header("Timing")]
    [Tooltip("Seconds between the anim trigger and the coin beginning its slide " +
             "(IK is active during this window).")]
    [SerializeField] private float _insertDelay = 0.6f;

    [Tooltip("Seconds the coin takes to travel from _coinEntrance to _coinAllIn.")]
    [SerializeField] private float _coinTravelDuration = 1f;

    // ── Coin animation markers ────────────────────────────────────────────────

    [Header("Coin Animation Markers")]
    [Tooltip("World position/rotation where the coin snaps at the start of its slide.")]
    [SerializeField] private Transform _coinEntrance;

    [Tooltip("World position/rotation the coin smoothly moves to before being despawned.")]
    [SerializeField] private Transform _coinAllIn;

    // ── Audio ─────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [SerializeField] private AudioClip _insertSound;
    [SerializeField] private float _insertSoundVolume = 1f;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _isActivated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True once a coin has been successfully inserted. Prevents double-activation.</summary>
    public bool IsActivated => _isActivated.Value;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractTextReady;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isActivated.OnValueChanged += OnActivatedChanged;
        RefreshState(_isActivated.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isActivated.OnValueChanged -= OnActivatedChanged;
    }

    // ── InteractWithItem ──────────────────────────────────────────────────────

    /// <summary>
    /// Called via left-click while holding a Kill Coin.
    /// The Kill Coin PickableItemData must be listed in itemsThatCanInteractWith on the prefab.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        KillCoin coin = item as KillCoin;
        if (IsActivated || coin == null) return;

        base.InteractWithItem(player, item);
        StartCoroutine(InsertCoinSequence(player, coin));
    }

    // ── Coin insertion sequence ───────────────────────────────────────────────

    private IEnumerator InsertCoinSequence(PlayerInteractionController player, KillCoin coin)
    {
        PlayerMovementController  movement = player.playerMovementController;
        PlayerAnimationController anim     = player.playerAnimationController;

        // ── Phase 1: Lock controls + move camera ──────────────────────────────
        movement.SetCanMove(false);
        movement.SetCanLook(false);
        player.SetCanInteract(false, string.Empty);

        movement.LookAtTarget(transform);

        if (_camPos != null)
        {
            movement.CameraTransform.DOMove(_camPos.position, _cameraMoveDuration);
            movement.CameraTransform.DORotate(_camPos.rotation.eulerAngles, _cameraMoveDuration);
        }

        anim.SetBodyLeanDirect(1f, 1f);

        yield return new WaitForSeconds(_cameraMoveDuration);

        // ── Phase 2: IK on + insert animation + release coin ─────────────────
        anim.EnableRightArmMask();

        if (_rightArmIKTarget != null)
        {
            anim.RightArmIKTarget    = _rightArmIKTarget;
            anim.CamRightArmIKTarget = _rightArmIKTarget;
        }

        // Blend the right-arm rig on. Kill Coin has useRightIK = false, so
        // DisableArmIKs (inside ReleaseHeldObjectForThrow) will NOT clear this.
        anim.SetRightArmRigWeightSmooth(1f, 0.5f);

        anim.SetAnimTrigger(InsertCoinTrigger);
        anim.SetAnimBool(HoldingCoinBool, false);

        // Coin stays in the player's hand until the insert animation finishes.
        yield return new WaitForSeconds(_insertDelay);

        // ── Phase 3: IK off + camera return + release coin into slot ─────────
        anim.SetRightArmRigWeightSmooth(0f, 0.5f);
        anim.RightArmIKTarget    = null;
        anim.CamRightArmIKTarget = null;
        anim.SetBodyLeanDirect(0f);
        movement.ResetCameraPos(false, _cameraReturnDuration);

        // Detach the coin from the player's hand now that the animation is done.
        // NT stays disabled, giving us full control over the transform until despawn.
        player.pickupController.ReleaseHeldObjectForThrow();

        PlayInsertSoundServerRpc(transform.position);

        // Broadcast the procedural coin animation to all clients.
        AnimateCoinInsertionServerRpc(new NetworkObjectReference(coin.NetworkObject));

        yield return new WaitForSeconds(_coinTravelDuration);

        // ── Phase 4: Despawn coin + activate kill machine ─────────────────────
        coin.DespawnServerRpc();
        ActivateKillMachineServerRpc();

        // Let the camera finish returning before restoring the arm mask.
        yield return new WaitForSeconds(_cameraReturnDuration);

        // ── Phase 5: Restore ──────────────────────────────────────────────────
        anim.DisableRightArmMask();
        movement.SetCanMove(true);
        movement.SetCanLook(true);
        player.SetCanInteract(true, string.Empty);
    }

    // ── Coin procedural animation (all clients) ───────────────────────────────

    /// <summary>Relays the coin slide request through the server so every client receives it.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void AnimateCoinInsertionServerRpc(NetworkObjectReference coinRef)
    {
        AnimateCoinInsertionClientRpc(coinRef);
    }

    /// <summary>
    /// Received on every client. Disables the coin's NetworkTransform (safety), removes any
    /// lingering ParentConstraint, snaps the coin to _coinEntrance, and starts the slide coroutine.
    /// </summary>
    [ClientRpc]
    private void AnimateCoinInsertionClientRpc(NetworkObjectReference coinRef)
    {
        if (!coinRef.TryGet(out NetworkObject coinNetObj)) return;

        KillCoin coin = coinNetObj.GetComponent<KillCoin>();
        if (coin == null) return;

        // Ensure NT is off so no remote authority overwrites our driven positions.
        NetworkTransform nt = coin.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        // Remove any constraint still active on non-owner clients (body-arm follow).
        coin.RemoveParent();

        StartCoroutine(AnimateCoinSlide(coin));
    }

    private IEnumerator AnimateCoinSlide(KillCoin coin)
    {
        if (_coinEntrance == null || _coinAllIn == null)
        {
            Debug.LogWarning("[CoinSlotPanel] _coinEntrance or _coinAllIn is not assigned — skipping coin slide.", this);
            yield break;
        }

        // Snap coin to the slot mouth.
        coin.transform.position = _coinEntrance.position;
        coin.transform.rotation = _coinEntrance.rotation;

        Vector3    startPos = _coinEntrance.position;
        Quaternion startRot = _coinEntrance.rotation;
        Vector3    endPos   = _coinAllIn.position;
        Quaternion endRot   = _coinAllIn.rotation;

        float elapsed = 0f;
        while (elapsed < _coinTravelDuration)
        {
            // Guard: despawn may arrive before the slide finishes on remote clients.
            if (coin == null) yield break;

            float t      = Mathf.Clamp01(elapsed / _coinTravelDuration);
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            coin.transform.position = Vector3.Lerp(startPos, endPos, smooth);
            coin.transform.rotation = Quaternion.Slerp(startRot, endRot, smooth);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (coin != null)
        {
            coin.transform.position = endPos;
            coin.transform.rotation = endRot;
        }
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the slot as activated and triggers the Kill Machine.
    /// Idempotent — the _isActivated guard prevents double-firing.
    /// Resets the panel once the kill sequence completes.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ActivateKillMachineServerRpc()
    {
        if (_isActivated.Value) return;

        _isActivated.Value = true;

        if (KillMachineController.Instance != null)
        {
            void OnKillComplete()
            {
                KillMachineController.Instance.OnKillComplete -= OnKillComplete;
                ResetPanel();
            }

            KillMachineController.Instance.OnKillComplete += OnKillComplete;
            KillMachineController.Instance.Kill();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayInsertSoundServerRpc(Vector3 position)
    {
        PlayInsertSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayInsertSoundClientRpc(Vector3 position)
    {
        if (SFXController.Instance != null && _insertSound != null)
            SFXController.Instance.PlayAtPosition(_insertSound, position, _insertSoundVolume);
    }

    // ── NetworkVariable callbacks ─────────────────────────────────────────────

    private void OnActivatedChanged(bool previous, bool current) => RefreshState(current);

    private void RefreshState(bool activated)
    {
        interactText = activated ? InteractTextUsed : InteractTextReady;
        enabled      = !activated;
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets the panel so it can accept another coin. Must be called on the server.
    /// </summary>
    public void ResetPanel()
    {
        if (!IsServer) return;
        _isActivated.Value = false;
    }
}
