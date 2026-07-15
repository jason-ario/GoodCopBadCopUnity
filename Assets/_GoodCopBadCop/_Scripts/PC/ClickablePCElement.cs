using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

public class ClickablePCElement : MonoBehaviour
{
    [Header("Feedback")]
    [SerializeField] private RectTransform feedbackTarget;
    [SerializeField] private bool animateFeedback = true;
    [SerializeField] private float hoverScale = 1.04f;
    [SerializeField] private float pressedScale = 0.98f;
    [SerializeField] private float hoverDuration = 0.08f;
    [SerializeField] private float clickDuration = 0.06f;
    [SerializeField] private Ease hoverEase = Ease.OutQuad;
    [SerializeField] private Ease clickEase = Ease.OutQuad;

    [Header("Legacy")]
    public UnityEvent onClickEvent;

    private Vector3 _baseScale;
    private Tween _feedbackTween;
    private Action _clickHandler;

    private RectTransform FeedbackTarget
    {
        get
        {
            if (feedbackTarget == null)
                feedbackTarget = transform as RectTransform;

            return feedbackTarget;
        }
    }

    protected virtual void Awake()
    {
        CacheBaseScale();
    }

    protected virtual void OnEnable()
    {
        CacheBaseScale();
        ResetFeedback();
    }

    protected virtual void OnDisable()
    {
        KillFeedbackTween();
        ResetFeedback();
    }

    public void SetClickHandler(Action clickHandler)
    {
        _clickHandler = clickHandler;
        onClickEvent = new UnityEvent();
    }

    public void ClearClickHandler()
    {
        _clickHandler = null;
    }

    public virtual void OnHoverEnter()
    {
        if (!animateFeedback)
            return;

        TweenScale(_baseScale * hoverScale, hoverDuration, hoverEase);
    }

    public virtual void OnHoverExit()
    {
        if (!animateFeedback)
            return;

        TweenScale(_baseScale, hoverDuration, hoverEase);
    }

    public virtual void OnClick()
    {
        PlayClickFeedback();
        _clickHandler?.Invoke();
        onClickEvent?.Invoke();
    }

    protected void SetFeedbackAnimationEnabled(bool enabled)
    {
        animateFeedback = enabled;
        if (!enabled)
            ResetFeedback();
    }

    private void PlayClickFeedback()
    {
        if (!animateFeedback || FeedbackTarget == null)
            return;

        KillFeedbackTween();
        _feedbackTween = DOTween.Sequence()
            .Append(FeedbackTarget.DOScale(_baseScale * pressedScale, clickDuration).SetEase(clickEase))
            .Append(FeedbackTarget.DOScale(_baseScale * hoverScale, clickDuration).SetEase(clickEase))
            .SetLink(gameObject);
    }

    private void TweenScale(Vector3 targetScale, float duration, Ease ease)
    {
        if (FeedbackTarget == null)
            return;

        KillFeedbackTween();
        _feedbackTween = FeedbackTarget.DOScale(targetScale, duration)
            .SetEase(ease)
            .SetLink(gameObject);
    }

    private void CacheBaseScale()
    {
        if (FeedbackTarget != null)
            _baseScale = FeedbackTarget.localScale;
    }

    private void ResetFeedback()
    {
        if (FeedbackTarget != null && _baseScale != Vector3.zero)
            FeedbackTarget.localScale = _baseScale;
    }

    private void KillFeedbackTween()
    {
        if (_feedbackTween == null)
            return;

        _feedbackTween.Kill();
        _feedbackTween = null;
    }

    protected virtual void OnDestroy()
    {
        KillFeedbackTween();
    }
}