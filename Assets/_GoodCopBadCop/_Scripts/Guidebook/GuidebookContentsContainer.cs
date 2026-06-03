using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton on the Guide Book Contents Container scene object.
/// Exposes Open/Close to toggle the contents child, which houses all
/// guidebook render cameras. The root object stays permanently active
/// so the singleton is always reachable at runtime.
///
/// Call <see cref="TriggerRender"/> from any system (including inactive children)
/// to force a one-frame activation of _contents so render texture cameras
/// recapture the latest UI state. The <see cref="_isRefreshing"/> guard prevents
/// re-entrant calls caused by OnEnable firing on children when contents activates.
/// </summary>
public class GuidebookContentsContainer : MonoBehaviour
{
    public static GuidebookContentsContainer Instance { get; private set; }

    [SerializeField] private GameObject _contents;

    private bool _isRefreshing;
    private Coroutine _refreshCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        _contents.SetActive(true);
        yield return new WaitForEndOfFrame();
        _contents.SetActive(false);
    }

    /// <summary>
    /// Temporarily enables _contents for two frames so the render texture cameras
    /// recapture the current UI state. Safe to call from inactive child components —
    /// this MonoBehaviour stays active throughout the session.
    /// Re-entrant calls while a refresh is in progress are ignored.
    /// </summary>
    public void TriggerRender()
    {
        if (_contents == null || _isRefreshing) return;
        if (_refreshCoroutine != null)
            StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = StartCoroutine(RenderCoroutine());
    }

    private IEnumerator RenderCoroutine()
    {
        _isRefreshing = true;

        bool wasActive = _contents.activeSelf;
        if (!wasActive)
            _contents.SetActive(true);

        // Wait for end of frame so LayoutGroups finish positioning newly built rows
        // before the render texture camera captures.
        yield return new WaitForEndOfFrame();

        // One additional frame ensures the render texture has picked up the new state.
        yield return null;

        if (!wasActive)
            _contents.SetActive(false);

        _isRefreshing = false;
        _refreshCoroutine = null;
    }
}
