using System.Collections;
using UnityEngine;

public class PlayerTutorialUI : MonoBehaviour
{
    public static PlayerTutorialUI Instance { get; private set; }

    private static readonly int BlackBarsOn = Animator.StringToHash("BlackBarsOn");

    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private float defaultHoldDuration = 3f;

    private Animator _animator;
    private Coroutine _sequenceCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _animator = GetComponent<Animator>();

        if (textReveal == null)
            Debug.LogError("[PlayerTutorialUI] textReveal is not assigned. Assign the Text (TMP) child in the Inspector.", this);
    }

    private void Start()
    {
        GameManager.Instance.OnGameStart += OnGameStart;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;
    }

    private void OnGameStart()
    {
        StartCoroutine(DelayedShow("Go to the booth to start your shift.", 3f));
    }

    private IEnumerator DelayedShow(string message, float delay)
    {
        yield return new WaitForSeconds(delay);
        Show(message);
    }

    /// <summary>Shows the tutorial bars with the given message, holds for the specified duration, then hides. Interrupts any running sequence.</summary>
    public void Show(string message, float holdDuration = -1f)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning("[PlayerTutorialUI] Show called with a null or empty message. Ignoring.", this);
            return;
        }

        float duration = holdDuration < 0f ? defaultHoldDuration : holdDuration;

        StopSequence();
        _sequenceCoroutine = StartCoroutine(SequenceCoroutine(message, duration));
    }

    /// <summary>Immediately dismisses the current tutorial sequence and slides the bars out.</summary>
    public void Dismiss()
    {
        StopSequence();
        textReveal.Clear();
        textReveal.gameObject.SetActive(false);
        _animator.SetBool(BlackBarsOn, false);
    }

    private IEnumerator SequenceCoroutine(string message, float holdDuration)
    {
        textReveal.gameObject.SetActive(true);
        _animator.SetBool(BlackBarsOn, true);

        yield return textReveal.RevealText(message);
        yield return new WaitForSeconds(holdDuration);

        Dismiss();
    }

    private void StopSequence()
    {
        if (_sequenceCoroutine != null)
        {
            StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = null;
        }
    }
}
