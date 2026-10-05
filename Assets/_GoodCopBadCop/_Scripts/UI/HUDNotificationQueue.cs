using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Single bottom-centre HUD notification slot with a queue. Every bottom-centre alert
/// ("Someone is waiting at the booth", "A shipment is waiting at the gate", "Health low",
/// "Radiation high", one-shot messages, ...) goes through one instance of this component, so
/// they never overlap. When several are requested at once they show one at a time in request
/// order.
///
/// Each notification has a <c>key</c>. Calling <see cref="Show"/> again with a key that is
/// already queued or showing updates that entry instead of adding a duplicate.
/// <see cref="Hide"/> removes the entry by key. If it is on screen, it fades out early and the
/// next queued entry plays.
///
/// Looping entries (<c>loop: true</c>) go back to the end of the queue after showing. They wait
/// at least <see cref="_repeatGapDuration"/> before showing again and keep cycling until
/// <see cref="Hide"/> is called with their key. Non-looping entries show once.
///
/// Renamed from <c>BoothWaitingNotification</c>. The script GUID is unchanged, so existing
/// scene and prefab references still work.
/// </summary>
public class HUDNotificationQueue : MonoBehaviour
{
    private class Entry
    {
        public string Key;
        public string Message;
        public bool Loop;
        public float RepeatGap = -1f;
        public float ReadyTime;
        public long Order;
    }

    [SerializeField] private TMPTextReveal _textReveal;
    [SerializeField] private CanvasGroup _canvasGroup;
    [Tooltip("How long each notification stays fully visible before fading out.")]
    [SerializeField] private float _displayDuration = 4f;
    [SerializeField] private float _fadeDuration = 0.4f;
    [Tooltip("CanvasGroup alpha used while the notification is visible (fade-in target).")]
    [Range(0f, 1f)]
    [SerializeField] private float _visibleAlpha = 0.8f;
    [Tooltip("Looping notifications only: minimum time after a looping notification fades out before it can show again.")]
    [SerializeField] private float _repeatGapDuration = 4.5f;
    [Tooltip("Pause between one notification fading out and the next one fading in.")]
    [SerializeField] private float _gapBetweenNotifications = 0.3f;

    private readonly List<Entry> _entries = new List<Entry>();
    private Entry _current;
    private bool _interruptCurrent;
    private Coroutine _processor;
    private long _nextOrder;

    /// <summary>Key of the notification currently on screen, or null.</summary>
    public string CurrentKey => _current?.Key;

    /// <summary>True if a notification with this key is showing, queued, or waiting to repeat.</summary>
    public bool IsActive(string key) => Find(key) != null;

    private void Awake()
    {
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;

        if (_entries.Count == 0)
            gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (_entries.Count > 0 && _processor == null)
            _processor = StartCoroutine(ProcessQueue());
    }

    private void OnDisable()
    {
        // Unity stops coroutines when the object is disabled. Reset display state; entries stay
        // queued and resume in OnEnable.
        _processor = null;
        _current = null;
        _interruptCurrent = false;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        if (_canvasGroup != null)
            DOTween.Kill(_canvasGroup);
    }

    /// <summary>
    /// Queues a notification under <paramref name="key"/>. If an entry with that key already
    /// exists, its message and loop flag are updated in place and no duplicate is added.
    /// <paramref name="repeatGap"/> overrides <see cref="_repeatGapDuration"/> for this looping
    /// entry (seconds between fading out and showing again); negative uses the default.
    /// </summary>
    public void Show(string key, string message, bool loop = false, float repeatGap = -1f)
    {
        if (string.IsNullOrEmpty(key))
            key = message;
        if (string.IsNullOrEmpty(key))
            return;

        var existing = Find(key);
        if (existing != null)
        {
            bool messageChanged = existing.Message != message;
            existing.Message = message;
            existing.Loop = loop;
            existing.RepeatGap = repeatGap;

            if (existing == _current && messageChanged && _textReveal != null)
                _textReveal.RevealText(message);
        }
        else
        {
            _entries.Add(new Entry
            {
                Key = key,
                Message = message,
                Loop = loop,
                RepeatGap = repeatGap,
                ReadyTime = 0f,
                Order = _nextOrder++
            });
        }

        EnsureRunning();
    }

    /// <summary>
    /// Removes the notification with <paramref name="key"/>. If it is on screen, it fades out
    /// now and the next queued notification plays.
    /// </summary>
    public void Hide(string key)
    {
        var entry = Find(key);
        if (entry == null)
            return;

        _entries.Remove(entry);
        if (entry == _current)
            _interruptCurrent = true;
    }

    /// <summary>Clears every queued notification and fades out the current one.</summary>
    public void HideAll()
    {
        _entries.Clear();
        if (_current != null)
            _interruptCurrent = true;
    }

    private Entry Find(string key)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Key == key)
                return _entries[i];
        }
        return null;
    }

    private Entry PickNext()
    {
        Entry best = null;
        float now = Time.time;
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.ReadyTime > now)
                continue;
            if (best == null || e.Order < best.Order)
                best = e;
        }
        return best;
    }

    private void EnsureRunning()
    {
        if (!gameObject.activeSelf)
        {
            // OnEnable starts the processor (if the parent hierarchy is active).
            gameObject.SetActive(true);
            return;
        }

        if (isActiveAndEnabled && _processor == null)
            _processor = StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        while (_entries.Count > 0)
        {
            var next = PickNext();
            if (next == null)
            {
                // Only looping entries are left, and they're all waiting out their repeat gap.
                yield return null;
                continue;
            }

            _current = next;
            yield return DisplayEntry(next);
            _current = null;

            // Hide() may have removed the entry while it was showing.
            if (_entries.Contains(next))
            {
                if (next.Loop)
                {
                    float gap = next.RepeatGap >= 0f ? next.RepeatGap : _repeatGapDuration;
                    next.ReadyTime = Time.time + gap;
                    next.Order = _nextOrder++;
                }
                else
                {
                    _entries.Remove(next);
                }
            }

            if (_entries.Count > 0 && _gapBetweenNotifications > 0f)
                yield return new WaitForSeconds(_gapBetweenNotifications);
        }

        _processor = null;
        if (_textReveal != null)
            _textReveal.Clear();
        gameObject.SetActive(false);
    }

    private IEnumerator DisplayEntry(Entry entry)
    {
        _interruptCurrent = false;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(_visibleAlpha, _fadeDuration);
        }

        if (_textReveal != null)
            _textReveal.RevealText(entry.Message);

        float elapsed = 0f;
        while (elapsed < _displayDuration && !_interruptCurrent)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        _interruptCurrent = false;

        if (_canvasGroup != null)
        {
            DOTween.Kill(_canvasGroup);
            _canvasGroup.DOFade(0f, _fadeDuration);
        }

        yield return new WaitForSeconds(_fadeDuration);

        if (_textReveal != null)
            _textReveal.Clear();
    }
}
