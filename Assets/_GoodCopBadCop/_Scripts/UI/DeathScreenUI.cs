using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class DeathScreenUI : MonoBehaviour
{
    public static DeathScreenUI Instance;

    [SerializeField] private CanvasGroup mainCanvasGroup;
    [SerializeField] private CanvasGroup bloodSplatterGroup;
    [SerializeField] private TextMeshProUGUI youDiedText;
    [SerializeField] private Button spectateButton;

    [Header("Settings")]
    [SerializeField] private float fadeDuration = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Hide initially
        mainCanvasGroup.alpha = 0f;
        mainCanvasGroup.interactable = false;
        mainCanvasGroup.blocksRaycasts = false;
        
        spectateButton.onClick.AddListener(OnSpectateClicked);
    }

    public void Show(float delay)
    {
        DOVirtual.DelayedCall(delay, () =>
        {
            mainCanvasGroup.interactable = true;
            mainCanvasGroup.blocksRaycasts = true;
            
            mainCanvasGroup.DOFade(1f, fadeDuration);
            bloodSplatterGroup.DOFade(1f, fadeDuration);
            
            youDiedText.transform.DOScale(1.1f, 3f).SetLoops(-1, LoopType.Yoyo);
        });
    }

    public void OnSpectateClicked()
    {
        mainCanvasGroup.DOFade(0f, 0.5f).OnComplete(() =>
        {
            mainCanvasGroup.interactable = false;
            mainCanvasGroup.blocksRaycasts = false;
            PlayerInstance.Instance?.StartSpectating();
        });
    }
}
