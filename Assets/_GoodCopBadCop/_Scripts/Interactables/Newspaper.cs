using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Held newspaper / daily fax. LMB raises it to read; zoom mode "holds LMB" through the
/// same path (see <see cref="HeldDocumentInspection"/>).
///
/// While sitting in the mailbox (<see cref="IsInMailbox"/>, set by <see cref="PostBox"/>) the paper
/// is never a target itself: the interaction ray passes through it to the mailbox, which offers
/// "Take newspaper" and routes it to the player's hands. Once taken it behaves as a normal pickup.
/// </summary>
public class Newspaper : PickableObject
{
    private readonly NetworkVariable<bool> _inMailbox = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True while the paper waits in the mailbox and must be taken through it.</summary>
    public bool IsInMailbox => IsSpawned && _inMailbox.Value;

    /// <summary>Server-only. Marks the paper as waiting in (or taken out of) the mailbox.</summary>
    public void SetInMailboxServer(bool inMailbox)
    {
        if (!IsServer) return;
        _inMailbox.Value = inMailbox;
    }

    public override bool IsInteractable => base.IsInteractable && !IsInMailbox;

    public override bool PassesInteractionRayThrough => IsInMailbox;

    public override void Highlight(bool highlight) => base.Highlight(highlight && !IsInMailbox);

    protected override void OnHeldStateChanged(bool isHeld)
    {
        base.OnHeldStateChanged(isHeld);

        // Safety net: however it got into someone's hands, it is no longer in the mailbox.
        if (isHeld && IsServer && _inMailbox.Value)
            _inMailbox.Value = false;
    }

    /// <summary>LMB opens the newspaper for reading.</summary>
    public override string GetHeldUseVerb() => "Read";

    public override void OnStartUse()
    {
        base.OnStartUse();
        HeldDocumentInspection.BeginFromUse(playerPickupController);
    }
    
    public override void OnStopUse()
    {
        base.OnStopUse();
        HeldDocumentInspection.EndFromUse(playerPickupController);
    }
}
