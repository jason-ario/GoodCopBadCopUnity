using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-frame distance gate for persistent (hold) highlights. Only <see cref="Interactable"/>s that
/// currently have a <see cref="HighlightHold"/> claimed are tracked, so the cost scales with the
/// number of glowing objects, not the number of interactables in the scene.
///
/// Each tick hands the local camera position to <see cref="Interactable.UpdateHoldHighlightRange"/>,
/// which fades the glow in when the camera comes within the object's range and out when it leaves —
/// the same behaviour as <see cref="TutorialMarker"/>'s visible range, just with a larger radius.
///
/// Purely local and visual. Self-creating (hidden, DontDestroyOnLoad); no scene setup required.
/// </summary>
public sealed class HoldHighlightRangeDriver : MonoBehaviour
{
    private static readonly HashSet<Interactable> _tracked = new();
    private static readonly List<Interactable> _snapshot = new();
    private static HoldHighlightRangeDriver _instance;

    public static void Track(Interactable interactable)
    {
        if (interactable == null) return;
        _tracked.Add(interactable);
        EnsureInstance();
    }

    public static void Untrack(Interactable interactable)
    {
        _tracked.Remove(interactable);
    }

    private static void EnsureInstance()
    {
        if (_instance != null || !Application.isPlaying) return;

        var go = new GameObject("[Hold Highlight Range Driver]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<HoldHighlightRangeDriver>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _tracked.Clear();
        _snapshot.Clear();
        _instance = null;
    }

    private void LateUpdate()
    {
        if (_tracked.Count == 0) return;

        Camera cam = Camera.main;
        Vector3 viewer = cam != null ? cam.transform.position : Vector3.zero;
        float time = Time.time;

        // Snapshot: range changes run OnHighlight/OnStopHighlight overrides that may release holds.
        _snapshot.Clear();
        _snapshot.AddRange(_tracked);

        for (int i = 0; i < _snapshot.Count; i++)
        {
            Interactable interactable = _snapshot[i];
            if (interactable == null)
            {
                _tracked.Remove(interactable);
                continue;
            }

            if (cam != null) interactable.UpdateHoldHighlightRange(viewer);
            interactable.UpdateHoldHighlightPulse(time);
        }
    }
}
