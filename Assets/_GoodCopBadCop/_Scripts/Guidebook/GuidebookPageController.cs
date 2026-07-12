using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One entry in the guidebook page list.
/// <see cref="anomalyTypeName"/> controls whether the page is visible (front face lock).
/// <see cref="backAnomalyTypeName"/> is informational — the back face is always shown
/// once the page is accessible, with no separate lock.
/// </summary>
[Serializable]
public struct GuidebookPageEntry
{
    [Tooltip("The physical page Transform to flip.")]
    public Transform page;

    [Tooltip("C# type name of the anomaly that must be unlocked before this page appears " +
             "(e.g. 'BlueVeinsAnomaly'). Leave empty to always show this page.")]
    public string anomalyTypeName;

    [Tooltip("C# type name of the anomaly shown on the back face of this page. " +
             "No separate lock — back is always accessible once the page is visible.")]
    public string backAnomalyTypeName;
}

/// <summary>
/// Physical page-stack mechanic for the Guidebook.
///
/// Pages begin on the right (unread) stack. Moving the horizontal axis right flips the
/// top right page 180° on the Z axis onto the left (read) stack; left flips it back.
///
/// Each page is double-sided: a "Canvas" child shows the front face and a
/// "Canvas Back" child (rotated 180° Z) shows the back face. The active face
/// switches at the midpoint of every flip animation so the content change is
/// hidden at the moment the page is most edge-on to the viewer.
///
/// Pages can be locked behind an anomaly unlock via <see cref="GuidebookPageEntry.anomalyTypeName"/>.
/// Locked pages are fully deactivated and contribute no thickness to the stack.
/// When a new anomaly unlocks, the page is reactivated, the list is rebuilt, and all
/// positions are recalculated automatically.
/// </summary>
public class GuidebookPageController : MonoBehaviour
{
    private const float HorizontalThreshold = 0.5f;

    private const string FrontCanvasName = "Contents/Canvas";
    private const string BackCanvasName  = "Contents/Canvas Back";

    [Header("Pages")]
    [Tooltip("All guidebook pages in reading order. Pages with an anomalyTypeName are hidden " +
             "until that anomaly is unlocked. Leave anomalyTypeName empty to always show.")]
    [SerializeField] private GuidebookPageEntry[] _pageEntries;

    [Header("Stack Origins — local space")]
    [SerializeField] private Vector3 _rightOrigin = new Vector3( 0.10f, 0f, 0f);
    [SerializeField] private Vector3 _leftOrigin  = new Vector3(-0.10f, 0f, 0f);

    [Tooltip("Y offset added per page in a stack, simulating physical thickness.")]
    [SerializeField] private float _pageThickness = 0.002f;

    [Tooltip("Z offset added per page in a stack, preventing depth-plane overlap.")]
    [SerializeField] private float _pageDepth = 0.001f;

    [Header("Animation")]
    [SerializeField] private float          _turnDuration     = 0.35f;
    [Tooltip("Per-page flip duration used when animating a tab jump across multiple pages.")]
    [SerializeField] private float          _snapFlipDuration = 0.08f;
    [SerializeField] private float          _arcHeight        = 0.02f;
    [SerializeField] private AnimationCurve _turnCurve        = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _pageFlipClip;

    /// <summary>
    /// Fired when a flip animation completes.
    /// Argument is the new left-stack count (equivalent to the active page/tab index).
    /// </summary>
    public event Action<int> OnPageChanged;

    // ── Runtime state ─────────────────────────────────────────────────────────

    /// <summary>Unlocked pages that can be flipped through.</summary>
    private Transform[] _activePages;

    public int  LeftCount       => _leftCount;
    public bool HasNextPage     => _activePages != null && _leftCount < _activePages.Length;
    public bool HasPreviousPage => _activePages != null && _leftCount > 0;

    private int     _leftCount;
    private bool    _isTurning;
    private bool    _isSnapping;
    private float[] _pageZRotations;
    private float   _prevHorizontal;
    private Coroutine _snapSequence;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked += HandleAnomalyUnlocked;
        RebuildActivePages();
        ResetPages();
    }

    private void OnDisable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked -= HandleAnomalyUnlocked;
    }

    private void Update()
    {
        if (_isTurning || _isSnapping) return;

        float h = Input.GetAxisRaw("Horizontal");

        if (h > HorizontalThreshold && _prevHorizontal <= HorizontalThreshold)
            TurnNext(_turnDuration);
        else if (h < -HorizontalThreshold && _prevHorizontal >= -HorizontalThreshold)
            TurnPrevious(_turnDuration);

        _prevHorizontal = h;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Flips the top right-stack page onto the left stack (axis right / forward).</summary>
    public void TurnNext(float duration)
    {
        if (_isTurning || !HasNextPage) return;

        int       rightCount = _activePages.Length - _leftCount;
        Transform page       = _activePages[_leftCount];
        int       idx        = _leftCount;

        PlayFlipSound();
        StartCoroutine(AnimateTurn(
            page,
            RightPos(rightCount - 1),
            LeftPos(_leftCount),
            _pageZRotations[idx],
            _pageZRotations[idx] - 180f,
            idx,
            duration,
            showBackAtMidpoint: true,
            () => { _leftCount++; OnPageChanged?.Invoke(_leftCount); }
        ));
    }

    /// <summary>Flips the top left-stack page back onto the right stack (axis left / back).</summary>
    public void TurnPrevious(float duration)
    {
        if (_isTurning || !HasPreviousPage) return;

        int       rightCount = _activePages.Length - _leftCount;
        Transform page       = _activePages[_leftCount - 1];
        int       idx        = _leftCount - 1;

        PlayFlipSound();
        StartCoroutine(AnimateTurn(
            page,
            LeftPos(_leftCount - 1),
            RightPos(rightCount),
            _pageZRotations[idx],
            _pageZRotations[idx] + 180f,
            idx,
            duration,
            showBackAtMidpoint: false,
            () => { _leftCount--; OnPageChanged?.Invoke(_leftCount); }
        ));
    }

    /// <summary>
    /// Instantly positions all active pages to match <paramref name="leftCount"/> pages on the
    /// left stack, with no animation. Safe to call from OnEnable or external tab-jump code.
    /// </summary>
    public void SnapTo(int leftCount)
    {
        StopAllCoroutines();
        _isTurning = false;

        if (_activePages == null) return;

        EnsureZArray();
        _leftCount = Mathf.Clamp(leftCount, 0, _activePages.Length);

        int n = _activePages.Length;
        for (int i = 0; i < n; i++)
        {
            if (_activePages[i] == null) continue;

            if (i < _leftCount)
            {
                _pageZRotations[i]            = 180f;
                _activePages[i].localPosition = LeftPos(i);
                _activePages[i].localRotation = Quaternion.Euler(0f, 0f, 180f);
            }
            else
            {
                _pageZRotations[i]            = 0f;
                _activePages[i].localPosition = RightPos(n - 1 - i);
                _activePages[i].localRotation = Quaternion.identity;
            }
        }

        UpdateFaceVisibility();
    }

    /// <summary>Returns every active page to the right stack with zero rotation.</summary>
    public void ResetPages() => SnapTo(0);

    /// <summary>
    /// Animates page flips in sequence until <paramref name="page"/> is at the top of the
    /// right stack. Each flip uses <see cref="_snapFlipDuration"/> so the sequence is fast
    /// but still visually readable. Cancels any in-progress sequence before starting.
    /// Called by <see cref="GuidebookSectionTab"/> when the player clicks a section tab.
    /// </summary>
    public void SnapToPage(Transform page)
    {
        if (_activePages == null || page == null) return;
        int idx = Array.IndexOf(_activePages, page);
        if (idx < 0 || idx == _leftCount) return;

        if (_snapSequence != null) StopCoroutine(_snapSequence);
        _snapSequence = StartCoroutine(AnimatedSnapTo(idx));
    }

    private IEnumerator AnimatedSnapTo(int targetLeftCount)
    {
        _isSnapping = true;

        while (_leftCount != targetLeftCount)
        {
            // If already turning (e.g. from a physical input just before click), wait for it.
            while (_isTurning) yield return null;

            if (_leftCount < targetLeftCount)
                TurnNext(_snapFlipDuration);
            else
                TurnPrevious(_snapFlipDuration);

            // Wait for the flip we just triggered to start and finish.
            yield return null; 
            while (_isTurning) yield return null;
        }

        _isSnapping = false;
        _snapSequence = null;
    }

    // ── Face visibility ───────────────────────────────────────────────────────

    /// <summary>
    /// Shows the correct canvas for the current stack side while also hiding any canvas
    /// whose anomaly is still locked. Front and back are gated independently so a page
    /// with only one unlocked side still shows that side correctly.
    /// </summary>
    private void SetPageFace(Transform page, bool showBack)
    {
        bool frontUnlocked = true;
        bool backUnlocked  = true;

        if (_pageEntries != null)
        {
            foreach (GuidebookPageEntry entry in _pageEntries)
            {
                if (entry.page != page) continue;
                frontUnlocked = IsEntryUnlocked(entry.anomalyTypeName);
                backUnlocked  = IsEntryUnlocked(entry.backAnomalyTypeName);
                break;
            }
        }

        Transform front = page.Find(FrontCanvasName);
        Transform back  = page.Find(BackCanvasName);

        if (showBack)
        {
            if (front != null) front.gameObject.SetActive(false);
            if (back  != null) back.gameObject.SetActive(backUnlocked);
        }
        else
        {
            if (front != null) front.gameObject.SetActive(frontUnlocked);
            if (back  != null) back.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Activates or deactivates both Canvas children on a page.
    /// Call with <c>false</c> for locked pages (blank paper appearance)
    /// and <c>true</c> when a page becomes accessible. <see cref="UpdateFaceVisibility"/>
    /// will then determine which face to show based on stack position.
    /// </summary>
    private void SetPageContentsActive(Transform page, bool active)
    {
        Transform front = page.Find(FrontCanvasName);
        Transform back  = page.Find(BackCanvasName);
        if (front != null) front.gameObject.SetActive(active);
        if (back  != null) back.gameObject.SetActive(active);
    }

    /// <summary>
    /// Refreshes front/back Canvas visibility for all active pages based on the current
    /// left-stack count. Pages on the left stack (already flipped) show their back face.
    /// </summary>
    private void UpdateFaceVisibility()
    {
        if (_activePages == null) return;

        for (int i = 0; i < _activePages.Length; i++)
        {
            if (_activePages[i] == null) continue;
            SetPageFace(_activePages[i], i < _leftCount);
        }
    }

    // ── Unlock handling ───────────────────────────────────────────────────────

    private bool IsEntryUnlocked(string typeName)
    {
        return string.IsNullOrEmpty(typeName)
            || AnomalyUnlockManager.Instance == null
            || AnomalyUnlockManager.Instance.IsAnomalyUnlocked(typeName);
    }

    private void RebuildActivePages()
    {
        var active = new List<Transform>();

        if (_pageEntries == null)
        {
            _activePages = Array.Empty<Transform>();
            return;
        }

        foreach (GuidebookPageEntry entry in _pageEntries)
        {
            if (entry.page == null) continue;

            bool frontUnlocked = IsEntryUnlocked(entry.anomalyTypeName);
            bool backUnlocked  = IsEntryUnlocked(entry.backAnomalyTypeName);
            bool pageActive    = frontUnlocked || backUnlocked;

            entry.page.gameObject.SetActive(pageActive);

            if (pageActive)
            {
                // Pre-enable both canvases; UpdateFaceVisibility will immediately
                // hide whichever face is locked and set the correct stack-side face.
                SetPageContentsActive(entry.page, true);
                active.Add(entry.page);
            }
        }

        _activePages = active.ToArray();
    }

    private void HandleAnomalyUnlocked(string typeName)
    {
        if (_pageEntries == null) return;

        bool relevant = false;
        foreach (GuidebookPageEntry entry in _pageEntries)
        {
            if (string.Equals(entry.anomalyTypeName,     typeName, StringComparison.Ordinal)
             || string.Equals(entry.backAnomalyTypeName, typeName, StringComparison.Ordinal))
            {
                relevant = true;
                break;
            }
        }

        if (!relevant) return;

        int savedLeft = _leftCount;
        RebuildActivePages();
        EnsureZArray();
        SnapTo(Mathf.Min(savedLeft, _activePages.Length));
    }

    // ── Position helpers ──────────────────────────────────────────────────────

    private Vector3 RightPos(int stackIndex) =>
        _rightOrigin + Vector3.up * (stackIndex * _pageThickness) + new Vector3(0f, 0f, stackIndex * _pageDepth);

    private Vector3 LeftPos(int stackIndex) =>
        _leftOrigin + Vector3.up * (stackIndex * _pageThickness) + new Vector3(0f, 0f, stackIndex * _pageDepth);

    // ── Animation ─────────────────────────────────────────────────────────────

    private void PlayFlipSound()
    {
        if (_audioSource != null && _pageFlipClip != null)
            _audioSource.PlayOneShot(_pageFlipClip);
    }

    private IEnumerator AnimateTurn(
        Transform page,
        Vector3   fromPos,
        Vector3   toPos,
        float     fromZ,
        float     toZ,
        int       pageIndex,
        float     duration,
        bool      showBackAtMidpoint,
        Action    onComplete)
    {
        _isTurning = true;
        bool faceSwitched = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = _turnCurve.Evaluate(Mathf.Clamp01(elapsed / duration));

            // Switch face at the midpoint — the page is most edge-on at t≈0.5,
            // hiding the content swap from the viewer.
            if (!faceSwitched && t >= 0.5f)
            {
                faceSwitched = true;
                SetPageFace(page, showBackAtMidpoint);
            }

            float arcY = Mathf.Sin(t * Mathf.PI) * _arcHeight;
            page.localPosition = Vector3.Lerp(fromPos, toPos, t) + new Vector3(0f, arcY, 0f);
            page.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(fromZ, toZ, t));

            yield return null;
        }

        float finalZ = toZ % 360f;
        if (finalZ < 0f) finalZ += 360f;

        _pageZRotations[pageIndex] = finalZ;
        page.localPosition         = toPos;
        page.localRotation         = Quaternion.Euler(0f, 0f, finalZ);

        // Ensure face is correct if the midpoint switch was somehow missed
        if (!faceSwitched)
            SetPageFace(page, showBackAtMidpoint);

        _isTurning = false;
        onComplete?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureZArray()
    {
        int needed = _activePages?.Length ?? 0;
        if (_pageZRotations == null || _pageZRotations.Length != needed)
            _pageZRotations = new float[needed];
    }
}
