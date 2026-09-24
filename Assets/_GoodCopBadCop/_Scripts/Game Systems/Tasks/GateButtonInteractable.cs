using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Physical button that opens the checkpoint gate when interacted with. Plays a press
/// animation ('Press' trigger — see 'button.controller') and a one-shot sound effect,
/// identically on every client, then asks the assigned <see cref="CheckpointGateController"/>
/// to open. Ignores presses while the gate is already open.
///
/// Scene setup: attach to the "Gate Button" prefab root alongside its Animator and
/// AudioSource. Requires a Collider on this GameObject so raycasts from
/// <see cref="PlayerInteractionController"/> resolve straight to this <see cref="Interactable"/>.
/// </summary>
public class GateButtonInteractable : Interactable
{
    [Header("Gate")]
    [Tooltip("The checkpoint gate this button opens.")]
    [SerializeField] private CheckpointGateController checkpointGate;

    [Header("Objective Highlight")]
    [Tooltip("Alternate highlight style shown while the button is force-highlighted as an active " +
             "objective (e.g. by SortMailTask while a shipment is waiting at the gate) and the reticle " +
             "is not aimed at it — see Interactable.ForceHighlightProfile. A distinct yellow " +
             "outline/glow so the call-out reads clearly as 'go here', separate from the ordinary " +
             "hover highlight. Falls back to the authored hover style if left unassigned.")]
    [SerializeField] private HighlightProfile objectiveHighlightProfile;

    protected override HighlightProfile ForceHighlightProfile => objectiveHighlightProfile;


    [Header("Press Feedback")]
    [Tooltip("The button's own Animator (drives the 'Press' trigger). Defaults to the Animator on this GameObject.")]
    [SerializeField] private Animator buttonAnimator;
    [Tooltip("AudioSource used to play pressSfx. Defaults to the AudioSource on this GameObject.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("One-shot sound played when the button is pressed.")]
    [SerializeField] private AudioClip pressSfx;
    [Tooltip("Volume for pressSfx.")]
    [SerializeField] private float pressSfxVolume = 1f;

    private static readonly int PressParam = Animator.StringToHash("Press");

    protected override void Awake()
    {
        base.Awake();

        if (buttonAnimator == null)
            buttonAnimator = GetComponent<Animator>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (checkpointGate != null && checkpointGate.IsOpen) return;

        RequestPressServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPressServerRpc()
    {
        if (checkpointGate != null && checkpointGate.IsOpen) return;

        PlayPressClientRpc();
        checkpointGate?.RequestOpen();
    }

    [ClientRpc]
    private void PlayPressClientRpc()
    {
        if (buttonAnimator != null)
            buttonAnimator.SetTrigger(PressParam);

        if (audioSource != null && pressSfx != null)
            audioSource.PlayOneShot(pressSfx, pressSfxVolume);
    }
}
