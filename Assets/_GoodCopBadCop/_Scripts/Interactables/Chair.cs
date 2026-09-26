using System;
using DG.Tweening;
using UnityEngine;

public class Chair : Interactable
{
    [SerializeField] private Transform sitPos;
    public Transform SitPos => sitPos;
    [SerializeField] private Transform standingPos;
    public Transform StandingPos => standingPos;
    [SerializeField] float sitDuration = .5f;
    public bool canMoveWhileSeated = false;
    public bool canRotateWhileSeated = false;
    [Tooltip("If true, this chair (and anything parented to it) rotates along with the player while seated. " +
             "If false, the player can still look/rotate in place (when canRotateWhileSeated is true) without " +
             "spinning the chair/object itself.")]
    public bool rotateSeatWithPlayer = true;

    private const string DefaultSitText = "Sit";

    // Movement controller of the player currently seated here (null when free). Used to stop the
    // reticle from targeting/hinting this chair while someone is sitting in it.
    private PlayerMovementController _occupant;

    /// <summary>Every chair shows the reticle's key hint (e.g. [E] Sit).</summary>
    public override bool ShowInteractHint => true;

    public override bool IsInteractable => base.IsInteractable && (_occupant == null || !_occupant.IsSitting);

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrWhiteSpace(interactText))
            interactText = DefaultSitText;
    }

    private void Start()
    {
        standingPos.transform.parent = null;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        Sit(player);
    }

    void Sit(PlayerInteractionController player)
    {
        _occupant = player.GetComponent<PlayerMovementController>();
        _occupant.Sit(this);
        player.transform.DOMove(sitPos.position, sitDuration);
        player.transform.DORotate(sitPos.eulerAngles, sitDuration).OnComplete(() => OnSeated(player.transform));
    }

    public void SitImmediate(PlayerInteractionController player)
    {
        _occupant = player.GetComponent<PlayerMovementController>();
        _occupant.Sit(this);
        player.transform.position = sitPos.position;
        player.transform.rotation = sitPos.rotation;
        OnSeated(player.transform);
    }

    void OnSeated(Transform player)
    {
        if (rotateSeatWithPlayer)
        {
            transform.parent = player.transform;
        }
        player.GetComponent<PlayerMovementController>().SetCanLook(canRotateWhileSeated);
        player.GetComponent<PlayerMovementController>().SetCanMove(canMoveWhileSeated);
    }

    // Called when the player stands up. Only clears the parent if this chair was actually
    // parented to the player while seated (rotateSeatWithPlayer) — otherwise it was never
    // reparented and clearing it would incorrectly detach it from its original hierarchy.
    public void OnStoodUp()
    {
        _occupant = null;
        if (rotateSeatWithPlayer)
        {
            transform.parent = null;
        }
    }
}
