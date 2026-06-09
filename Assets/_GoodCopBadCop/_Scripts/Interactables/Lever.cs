using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

public class Lever : Interactable
{
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioSource leverAudio;
    [SerializeField] AudioClip leverOnSound;
    [SerializeField] AudioClip leverOffSound;
    [SerializeField] private ShutterController shutter;

    [Header("Camera & IK")]
    [Tooltip("Child Transform the player camera DOTweens to during the pull sequence.")]
    [SerializeField] private Transform _camPos;

    [Tooltip("World Transform the right-arm IK anchors to while the pull animation plays.")]
    [SerializeField] private Transform _ikTarget;

    [Tooltip("Seconds the camera takes to reach _camPos.")]
    [SerializeField] private float _cameraMoveDuration = 0.5f;

    [Tooltip("Seconds the camera takes to return to the normal position after the lever is pulled.")]
    [SerializeField] private float _cameraReturnDuration = 0.25f;
    
    private static readonly int IsUpParam = Animator.StringToHash("IsUp");

    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsUp => _isUp.Value;

    public override void OnNetworkSpawn()
    {
        _isUp.OnValueChanged += OnLeverStateChanged;

        // Sync visual state on spawn
        _animator.SetBool(IsUpParam, _isUp.Value);

        // Sync shutter state on spawn (silent — no audio on initial sync)
        if (_isUp.Value)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnLeverStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        StartCoroutine(PullLeverSequence(player));
    }

    private IEnumerator PullLeverSequence(PlayerInteractionController player)
    {
        PlayerMovementController  movement = player.playerMovementController;
        PlayerAnimationController anim     = player.playerAnimationController;

        // ── Phase 1: Lock controls + orient player + move camera ──────────────
        movement.SetCanControl(false);
        movement.LookAtTarget(transform);

        if (_ikTarget != null)
            anim.RightArmIKTarget = _ikTarget;

        if (_camPos != null)
        {
            movement.CameraTransform.DOMove(_camPos.position, _cameraMoveDuration);
            movement.CameraTransform.DORotate(_camPos.rotation.eulerAngles, _cameraMoveDuration);
        }

        anim.SetBodyLeanDirect(1f, 1f);

        yield return new WaitForSeconds(_cameraMoveDuration);

        // ── Phase 2: IK on + perform lever action ─────────────────────────────
        anim.EnableRightArmMask();
        anim.TurnRightArmRigOnAndOff(0.2f, 0.5f);

        // Apply visuals immediately on the interacting client — no RTT wait.
        bool predicted = !_isUp.Value;
        ApplyLeverVisuals(predicted);

        ToggleLeverServerRpc(NetworkManager.Singleton.LocalClientId);

        yield return new WaitForSeconds(1f);

        // ── Phase 3: Camera return + IK off ───────────────────────────────────
        anim.SetBodyLeanDirect(0f);
        movement.ResetCameraPos(false, _cameraReturnDuration);

        yield return new WaitForSeconds(_cameraReturnDuration);

        // ── Phase 4: Restore ──────────────────────────────────────────────────
        anim.DisableRightArmMask();
        movement.SetCanControl(true);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleLeverServerRpc(ulong senderClientId)
    {
        _isUp.Value = !_isUp.Value;

        // Broadcast visuals to all clients except the one that already predicted.
        BroadcastLeverStateClientRpc(_isUp.Value, senderClientId);
    }

    /// <summary>
    /// Applies the lever visual to all clients except the one that predicted it locally.
    /// </summary>
    [ClientRpc]
    private void BroadcastLeverStateClientRpc(bool isUp, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;
        ApplyLeverVisuals(isUp);
    }

    private void OnLeverStateChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastLeverStateClientRpc.
    }

    private void ApplyLeverVisuals(bool isUp)
    {
        _animator.SetBool(IsUpParam, isUp);
        leverAudio.PlayOneShot(isUp ? leverOnSound : leverOffSound);

        if (isUp)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    /// <summary>
    /// Raises the lever on the server and broadcasts visuals to all clients,
    /// opening the shutter. Call this from tutorial scripts that need to open
    /// the window automatically without player input.
    /// Must be called on the server.
    /// </summary>
    public void OpenServerSide()
    {
        if (!IsServer) return;
        _isUp.Value = true;
        BroadcastLeverStateClientRpc(true, ulong.MaxValue); // ulong.MaxValue = no exclusion
    }

    public void Reset()
    {
        if (!IsServer) return;

        // Only broadcast (and play the lever-off sound) when the lever was actually up.
        // If it is already down, the visual state is already correct on all clients and
        // firing the RPC would play a spurious sound — notably during the intro cutscene
        // when ResetEverything() runs before the lever has ever been touched.
        if (!_isUp.Value) return;

        _isUp.Value = false;
        BroadcastLeverStateClientRpc(false, ulong.MaxValue);
    }
}
