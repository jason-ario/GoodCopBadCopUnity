using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Local (per-client) one-shot tutorial: the first time the local player holds a
/// <see cref="TrashBag"/> containing junk, every registered dumpster's <see cref="TutorialCallout"/>
/// (pooled arrow + highlight via <see cref="HighlightHold.TutorialArrow"/>) is shown. The call-outs
/// hide whenever the player stops holding a junk-filled bag, and are dismissed for the rest of the
/// session once a bag the local player carried with junk is deposited in any dumpster.
///
/// Inputs:
///   - <see cref="TrashBag"/> reports held/junk changes via <see cref="ReportBagState"/> (all clients).
///   - <see cref="DumpsterInteractable"/> registers its call-out and forwards server-confirmed
///     deposits via <see cref="NotifyBagDeposited"/> (ClientRpc, all clients).
/// </summary>
public static class DumpsterDepositTutorial
{
    private static readonly HashSet<TutorialCallout> _callouts = new();
    private static TrashBag _heldJunkBag;      // bag the local player currently holds with junk
    private static ulong _trackedBagId = ulong.MaxValue; // last bag the local player held with junk
    private static bool _completed;
    private static bool _shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _callouts.Clear();
        _heldJunkBag = null;
        _trackedBagId = ulong.MaxValue;
        _completed = false;
        _shown = false;
    }

    public static void Register(TutorialCallout callout)
    {
        if (callout == null || !_callouts.Add(callout)) return;
        callout.SetActive(_shown);
    }

    public static void Unregister(TutorialCallout callout)
    {
        if (callout == null || !_callouts.Remove(callout)) return;
        callout.SetActive(false);
    }

    /// <summary>Called on every client whenever a bag's holder or junk count changes, or it despawns.</summary>
    public static void ReportBagState(TrashBag bag, bool despawning = false)
    {
        if (_completed || bag == null) return;

        NetworkManager nm = NetworkManager.Singleton;
        bool heldLocallyWithJunk = !despawning
                                   && nm != null
                                   && bag.IsHeld
                                   && bag.HolderClientId == nm.LocalClientId
                                   && bag.JunkCount > 0;

        if (heldLocallyWithJunk)
        {
            _heldJunkBag = bag;
            _trackedBagId = bag.NetworkObjectId;
        }
        else if (_heldJunkBag == bag)
        {
            _heldJunkBag = null;
        }

        SetShown(_heldJunkBag != null);
    }

    /// <summary>Called on every client when the server confirms a bag was deposited.</summary>
    public static void NotifyBagDeposited(ulong bagNetworkObjectId)
    {
        if (_completed || bagNetworkObjectId != _trackedBagId) return;

        _completed = true;
        _heldJunkBag = null;
        SetShown(false);
    }

    private static void SetShown(bool shown)
    {
        if (_shown == shown) return;
        _shown = shown;

        foreach (TutorialCallout callout in _callouts)
            if (callout != null) callout.SetActive(shown);
    }
}
