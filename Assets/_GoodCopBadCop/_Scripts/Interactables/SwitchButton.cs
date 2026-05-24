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

    void OnShiftReady()
    {
        // Day 1 switch readiness is driven by Day_01's tutorial sequence — skip auto-ready.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
        SetReady(true);
    }

    public void PowerOff()
    {
        powerOn = false;
        anim.SetBool("Ready", false);
    }

    public void PowerOn()
    {
        powerOn = true;

        if (buttonReady)
        {
            SetReady(true);
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
            HandleButtonPressServer();
        else
            RequestButtonPressServerRpc();

        PlayButtonSoundClientRpc();

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
    /// </summary>
    private void HandleButtonPressServer()
    {
        if (!buttonReady || !powerOn) return;

        SetReady(false);
        NotifyPressedClientRpc();

        if (!ShiftManager.Instance.shiftStarted.Value)
        {
            ShiftManager.Instance.TryStartShift();
        }
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
