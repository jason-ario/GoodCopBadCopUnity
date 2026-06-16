using DG.Tweening;
using UnityEngine;

/// <summary>
/// Displays cinematic black bars and a "Spectator Mode" label at the top of the screen
/// while the local player is watching a teammate. Slides in on Show() and out on Hide().
/// </summary>
public class SpectatorUI : MonoBehaviour
{
    private const float BarSlideDistance = 200f;
    private const float AnimDuration = 0.35f;

    [SerializeField] private RectTransform topBar;
    [SerializeField] private RectTransform bottomBar;

    private void Start()
    {
        // Removed automatic deactivation to allow external activation.
        // Root object should be set to inactive in the Inspector by default.
    }

    /// <summary>Slides both bars onto the screen from their off-screen positions.</summary>
    public void Show()
    {
        gameObject.SetActive(true);

        topBar.DOKill();
        bottomBar.DOKill();

        topBar.anchoredPosition = new Vector2(0f, BarSlideDistance);
        bottomBar.anchoredPosition = new Vector2(0f, -BarSlideDistance);

        topBar.DOAnchorPosY(0f, AnimDuration).SetEase(Ease.OutCubic);
        bottomBar.DOAnchorPosY(0f, AnimDuration).SetEase(Ease.OutCubic);
    }

    /// <summary>Slides both bars back off-screen and deactivates the root object.</summary>
    public void Hide()
    {
        topBar.DOKill();
        bottomBar.DOKill();

        topBar.DOAnchorPosY(BarSlideDistance, AnimDuration).SetEase(Ease.InCubic);
        bottomBar.DOAnchorPosY(-BarSlideDistance, AnimDuration).SetEase(Ease.InCubic)
            .OnComplete(() => gameObject.SetActive(false));
    }
}
