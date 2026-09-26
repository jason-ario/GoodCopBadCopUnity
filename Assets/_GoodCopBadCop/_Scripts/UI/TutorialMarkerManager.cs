using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton that hands out and recycles <see cref="TutorialMarker"/> instances from a pool.
/// Call <see cref="Mark"/> to attach a hovering arrow to any world Transform, and
/// <see cref="Unmark"/> / <see cref="UnmarkAll"/> to remove them.
/// The manager never destroys pool instances — it reuses them across the session.
/// </summary>
public class TutorialMarkerManager : MonoBehaviour
{
    public static TutorialMarkerManager Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────
    [Tooltip("Prefab that contains a TutorialMarker component (the arrow mesh).")]
    [SerializeField] private TutorialMarker markerPrefab;

    [Tooltip("How many markers to pre-warm in the pool on Awake.")]
    [SerializeField] private int initialPoolSize = 4;

    // ── Private state ────────────────────────────────────────────────────────
    private readonly List<TutorialMarker> _pool = new();
    private readonly Dictionary<Transform, TutorialMarker> _active = new();

    // ── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (markerPrefab == null)
        {
            Debug.LogError("[TutorialMarkerManager] markerPrefab is not assigned. Assign the TutorialMarker prefab in the Inspector.", this);
            return;
        }

        for (int i = 0; i < initialPoolSize; i++)
            _pool.Add(CreatePooledMarker());
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a tutorial marker above <paramref name="target"/>.
    /// Calling this again on the same target is a no-op (safe to call repeatedly).
    /// </summary>
    /// <param name="target">The Transform to point at.</param>
    public void Mark(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("[TutorialMarkerManager] Mark called with a null target.", this);
            return;
        }

        if (_active.ContainsKey(target)) return;

        TutorialMarker marker = GetFromPool();
        _active[target] = marker;
        marker.Show(target);
    }

    /// <summary>Hides the marker on <paramref name="target"/> and returns it to the pool.</summary>
    /// <param name="target">The Transform whose marker should be removed.</param>
    public void Unmark(Transform target)
    {
        if (target == null) return;

        if (!_active.TryGetValue(target, out TutorialMarker marker)) return;

        _active.Remove(target);
        marker.Hide();
        _pool.Add(marker);
    }

    /// <summary>Hides all active markers and returns every instance to the pool.</summary>
    public void UnmarkAll()
    {
        foreach (KeyValuePair<Transform, TutorialMarker> pair in _active)
        {
            pair.Value.Hide();
            _pool.Add(pair.Value);
        }
        _active.Clear();
    }

    /// <summary>Returns whether <paramref name="target"/> currently has an active marker.</summary>
    public bool IsMarked(Transform target) => target != null && _active.ContainsKey(target);

    /// <summary>
    /// Highlights <paramref name="interactable"/> for as long as the marker on
    /// <paramref name="target"/> is shown. Use for runtime/network-spawned objects the arrow cannot
    /// resolve on its own (e.g. Vlad's documents, ATM coupons). No-op if <paramref name="target"/> is
    /// not currently marked.
    /// </summary>
    public void AddHighlight(Transform target, Interactable interactable)
    {
        if (target == null || interactable == null) return;
        if (_active.TryGetValue(target, out TutorialMarker marker))
            marker.AddHighlightTarget(interactable);
    }

    /// <summary>
    /// Returns a live view of every Transform currently marked (i.e. every active tutorial
    /// objective). Used by <see cref="CompassController"/> to render a directional pip for each
    /// one on the bottom-of-screen compass. Do not mutate the returned collection.
    /// </summary>
    public IReadOnlyCollection<Transform> GetMarkedTargets() => _active.Keys;

    // ── Internals ────────────────────────────────────────────────────────────

    private TutorialMarker GetFromPool()
    {
        if (_pool.Count > 0)
        {
            TutorialMarker m = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return m;
        }

        return CreatePooledMarker();
    }

    private TutorialMarker CreatePooledMarker()
    {
        TutorialMarker m = Instantiate(markerPrefab, transform);
        m.gameObject.SetActive(false);
        return m;
    }
}
