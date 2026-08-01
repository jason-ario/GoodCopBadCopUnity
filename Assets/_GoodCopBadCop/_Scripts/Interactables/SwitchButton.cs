using System;
using System.Collections;
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

public class SwitchButton : Interactable
{
    [SerializeField] private AudioSource buttonPressSound;
    [SerializeField] private Animator anim;
    [SerializeField] Transform ikTarget;
    [SerializeField] private CinemachineCamera _camera;
    public bool buttonReady = false;
    private bool powerOn = true;

    /// <summary>Raised on all clients the moment the switch is successfully pressed.</summary>
    public static event Action OnPressed;
    
    protected override void Awake()
    {
        base.Awake();
        ShiftManager.Instance.OnShiftReady += OnShiftReady;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer) return;

        ShiftManager.OnNextSuspectReadyForBell += HandleNextSuspectReady;
    }

    public override void OnNetworkDespawn()
    {
        ShiftManager.OnNextSuspectReadyForBell -= HandleNextSuspectReady;
    }

    void OnShiftReady()
    {
        // Day 1 switch readiness is driven by Day_01's tutorial sequence — skip auto-ready.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        SetReady(true);
    }

    /// <summary>
    /// Called server-side when the next suspect is ready to be summoned.
    /// Defers one frame so that any scripted auto-summon (e.g. Vlad on Day 1) that runs
    /// synchronously on the same event can clear the flag before the switch lights up.
    /// </summary>
    private void HandleNextSuspectReady()
    {
        StartCoroutine(SetReadyIfStillPending());
    }

    private IEnumerator SetReadyIfStillPending()
    {
        yield return null;
        if (ShiftManager.NextSuspectReadyForBell)
            SetReady(true);
    }

    public void PowerOff()
    {
        powerOn = false;

        // Route through SetReady (not a bare anim.SetBool) so buttonReady and the visual
        // never diverge, and the server rebroadcasts the false state to every client.
        SetReady(false);
    }

    public void PowerOn()
    {
        powerOn = true;

        // Server-authoritative recompute: always re-derive from the live game state
        // (ShiftManager.NextSuspectReadyForBell) and always rebroadcast via SetReady, rather
        // than only reacting when a locally-cached buttonReady already happened to be true.
        //
        // Previously this only called SetReady(true) conditionally, so a client whose local
        // buttonReady cache had gone stale (e.g. a dropped/missed SetReadyClientRpc, or the
        // deferred one-frame HandleNextSuspectReady->SetReadyIfStillPending coroutine getting
        // aborted mid-flight by the outage) would never be told to re-sync — the button kept
        // working (HandleButtonPressServer only ever checks the server's own buttonReady) but
        // stayed visually dark on that client forever. Unconditionally rebroadcasting here
        // closes that gap: every power-restore is now a hard resync point for every client.
        if (IsServer)
        {
            SetReady(ShiftManager.NextSuspectReadyForBell);
        }
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        StartCoroutine(PressButtonSequence(player));
    }

    IEnumerator PressButtonSequence(PlayerInteractionController player)
    {
        player.playerMovementController.SetCanControl(false);
        player.playerMovementController.LookAtTarget(transform);

        player.playerAnimationController.RightArmIKTarget = ikTarget;
        player.playerMovementController.CameraTransform.DOMove(_camera.transform.position, .5f);
        player.playerMovementController.CameraTransform.DORotate(_camera.transform.rotation.eulerAngles, .5f);

        // Lean the body forward as the camera moves toward the switch.
        player.playerAnimationController.SetBodyLeanDirect(1f, 1f);

        yield return new WaitForSeconds(.5f);
        player.playerAnimationController.EnableRightArmMask();
        player.playerAnimationController.TurnRightArmRigOnAndOff(.2f, .5f);
        player.playerAnimationController.SetAnimTrigger("PressButton");

        // Route the game-state logic through the server so it is authoritative on all clients.
        if (IsServer)
        {
            HandleButtonPressServer();
            PlayButtonSoundClientRpc();
        }
        else
        {
            RequestButtonPressServerRpc();
            // Sound and animation are triggered server-side via HandleButtonPressServer →
            // PlayButtonSoundClientRpc so all clients hear it, including the pressing client.
        }

        yield return new WaitForSeconds(1);

        // Release the lean as the camera returns.
        player.playerAnimationController.SetBodyLeanDirect(0f);
        player.playerMovementController.ResetCameraPos(false, .25f);

        yield return new WaitForSeconds(.25f);

        player.playerAnimationController.DisableRightArmMask();
        player.playerMovementController.SetCanControl(true);
    }

    /// <summary>
    /// Sends a button-press request to the server from a client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestButtonPressServerRpc()
    {
        HandleButtonPressServer();
    }

    /// <summary>
    /// Authoritative button-press logic. Must run on the server only.
    /// The switch's sole function is summoning the next suspect once the shift signals one
    /// is ready — starting the shift itself is handled by <see cref="TimecardMachine"/>'s
    /// clock-in punch.
    /// </summary>
    private void HandleButtonPressServer()
    {
        if (!buttonReady || !powerOn || !ShiftManager.NextSuspectReadyForBell) return;

        SetReady(false);
        NotifyPressedClientRpc();
        PlayButtonSoundClientRpc();

        ShiftManager.Instance.PlayBuzzerSoundNetworked();
        SuspectController.Instance.NextSuspect();
    }

    /// <summary>Broadcasts the press event to every client so local listeners can react.</summary>
    [ClientRpc]
    private void NotifyPressedClientRpc()
    {
        OnPressed?.Invoke();
    }

    [ClientRpc]
    private void PlayButtonSoundClientRpc()
    {
        if (buttonPressSound != null)
        {
            buttonPressSound.Play();
        }

        anim.SetTrigger("Push");
    }

    /// <summary>
    /// Sets the button's ready state on all clients. Must be called on the server.
    /// </summary>
    public void SetReady(bool b)
    {
        buttonReady = b;
        anim.SetBool("Ready", powerOn && b);

        if (IsServer)
        {
            SetReadyClientRpc(b);
        }
    }

    [ClientRpc]
    private void SetReadyClientRpc(bool b)
    {
        buttonReady = b;
        anim.SetBool("Ready", powerOn && b);
    }
}
