using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkAnimator. The owning client drives all animator
/// parameter changes and they replicate to the server and all other clients.
/// Use this instead of the default NetworkAnimator on player prefabs so that
/// client-owned players correctly sync their animation state to the server.
/// </summary>
public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
