using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the start-shift gate.
/// Before the intro cutscene completes, interacting opens the start-shift screen.
/// Once the intro cutscene is done (<see cref="ShiftManager.OnShiftReady"/> fires),
/// the gate behaves like a standard gate — toggling open and closed.
/// </summary>
public class GateStartShiftController : Interactable
{
    [Header("Gate Animation")]
    [SerializeField] private Animator gateAnimator;
    [SerializeField] private Transform forwardMarker;

    [Header("Gate Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip doorOpenClip;
    [SerializeField] private AudioClip doorCloseClip;

    [Header("Gate Settings")]
    [SerializeField] private float waitDelay = 0.5f;

    private bool _introComplete = false;
    private bool _gateOpen = false;
    private bool _beingInteractedWith = false;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Start Shift";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ShiftManager.Instance.OnShiftReady += OnIntroComplete;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftReady -= OnIntroComplete;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (!_introComplete)
        {
            UIController.Instance.OpenStartShiftScreen();
        }
        else
        {
            if (!_beingInteractedWith)
                StartCoroutine(WaitAndToggleDoor(player));
        }
    }

    private IEnumerator WaitAndToggleDoor(PlayerInteractionController player)
    {
        if (!_gateOpen && audioSource != null && doorOpenClip != null)
            audioSource.PlayOneShot(doorOpenClip);

        _beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();
        yield return new WaitForSeconds(waitDelay);

        ToggleDoor(player);

        yield return new WaitForSeconds(waitDelay);
        _beingInteractedWith = false;
    }

    private void ToggleDoor(PlayerInteractionController player)
    {
        if (_gateOpen)
        {
            _gateOpen = false;
            gateAnimator.SetBool("OpenedIn", false);
            gateAnimator.SetBool("OpenedOut", false);
            interactText = "Open";

            if (audioSource != null && doorCloseClip != null)
                audioSource.PlayOneShot(doorCloseClip);
        }
        else
        {
            _gateOpen = true;
            interactText = "Close";

            if (forwardMarker != null)
            {
                Vector3 doorForward = forwardMarker.forward;
                Vector3 playerToDoor = transform.position - player.transform.position;
                float side = Vector3.Dot(doorForward, playerToDoor);

                if (side > 0)
                {
                    gateAnimator.SetBool("OpenedIn", true);
                    gateAnimator.SetBool("OpenedOut", false);
                }
                else
                {
                    gateAnimator.SetBool("OpenedIn", false);
                    gateAnimator.SetBool("OpenedOut", true);
                }
            }
            else
            {
                // Fallback if no forward marker is assigned.
                gateAnimator.SetBool("OpenedIn", true);
            }
        }
    }

    /// <summary>Opens the gate on all clients. Must be called on the server.</summary>
    public void OpenGate()
    {
        SetGateOpenClientRpc(true);
    }

    /// <summary>Closes the gate on all clients. Must be called on the server.</summary>
    public void CloseGate()
    {
        SetGateOpenClientRpc(false);
    }

    [ClientRpc]
    private void SetGateOpenClientRpc(bool open)
    {
        gateAnimator.SetBool("Open", open);
    }

    /// <summary>
    /// Called when the intro cutscene ends. Switches the gate into simple open/close mode.
    /// </summary>
    private void OnIntroComplete()
    {
        _introComplete = true;
        interactText = "Open";
    }
}
