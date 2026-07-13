using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TerminalListItem : MonoBehaviour
{
    private static readonly Vector2 ProfileImagePosition = new Vector2(0.12f, 0f);
    private static readonly Vector2 ProfileImageSize = new Vector2(0.24f, 0.24f);
    private static readonly Vector2 RecordTextPosition = new Vector2(0.42f, 0f);
    private const float RecordTextWidth = 250f;
    private static readonly Vector2 NewsTextPosition = new Vector2(0.42f, 0f);
    private const float NewsTextWidth = 320f;

    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image profileImage;

    private PC _pc;
    private SuspectData _suspectData;
    private TerminalNewsEntry _newsEntry;
    private Sprite _generatedProfileSprite;

    public void Setup(SuspectData suspectData, PC pc, string status = "")
    {
        _suspectData = suspectData;
        _newsEntry = null;
        _pc = pc;

        ConfigureTextSizing();
        ApplyRecordLayout();

        string displayName = suspectData.LastName + ", " + suspectData.FirstName;
        nameText.text = string.IsNullOrWhiteSpace(status)
            ? displayName
            : displayName + " - " + status;

        SetClickable(true);
        SetProfileImage(suspectData);
    }

    public void SetupNews(TerminalNewsEntry newsEntry, PC pc)
    {
        _suspectData = null;
        _newsEntry = newsEntry;
        _pc = pc;

        ConfigureTextSizing();
        ApplyNewsLayout();

        string header = newsEntry?.Content != null ? newsEntry.Content.headerText : "MISSING NEWS ENTRY";
        string date = newsEntry != null ? newsEntry.Date : "UNKNOWN DATE";
        nameText.text = $"{date} - {header}";

        SetClickable(newsEntry?.Content != null);
        ClearProfileImage();
    }

    public void SetupSummary(string text)
    {
        _suspectData = null;
        _newsEntry = null;
        _pc = null;
        ConfigureTextSizing();
        ApplySummaryLayout();
        nameText.text = text;

        SetClickable(false);
        ClearProfileImage();
    }

    public void Select()
    {
        if (_pc == null)
            return;

        if (_newsEntry != null)
        {
            _pc.OpenNewsEntryPage(_newsEntry);
            return;
        }

        if (_suspectData != null)
            _pc.OpenProfilePage(_suspectData);
    }

    private void ConfigureTextSizing()
    {
        if (nameText == null)
            return;

        nameText.enableAutoSizing = true;
        nameText.fontSizeMin = 12f;
        nameText.fontSizeMax = 26.8f;
    }

    private void SetProfileImage(SuspectData suspectData)
    {
        Image image = EnsureProfileImage();
        if (image == null)
            return;

        Texture2D photo = suspectData != null ? suspectData.IDPhoto : null;
        if (photo == null)
        {
            image.enabled = false;
            image.sprite = null;
            return;
        }

        if (_generatedProfileSprite != null)
            Destroy(_generatedProfileSprite);

        _generatedProfileSprite = Sprite.Create(
            photo,
            new Rect(0, 0, photo.width, photo.height),
            new Vector2(0.5f, 0.5f));

        image.sprite = _generatedProfileSprite;
        image.enabled = true;
    }

    private Image EnsureProfileImage()
    {
        if (profileImage == null)
        {
            GameObject imageObject = new GameObject("Profile Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(transform, false);
            imageObject.layer = gameObject.layer;

            profileImage = imageObject.GetComponent<Image>();
            profileImage.preserveAspect = true;
            profileImage.raycastTarget = false;
        }

        ApplyRecordLayout();
        return profileImage;
    }

    private void ApplyRecordLayout()
    {
        if (profileImage != null)
        {
            RectTransform imageRect = profileImage.rectTransform;
            imageRect.anchorMin = new Vector2(0f, 0.5f);
            imageRect.anchorMax = new Vector2(0f, 0.5f);
            imageRect.pivot = new Vector2(0f, 0.5f);
            imageRect.anchoredPosition = ProfileImagePosition;
            imageRect.sizeDelta = ProfileImageSize;
        }

        if (nameText == null)
            return;

        RectTransform textRect = nameText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(0f, 0.5f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition = RecordTextPosition;
        textRect.sizeDelta = new Vector2(RecordTextWidth, textRect.sizeDelta.y);
    }

    private void ApplySummaryLayout()
    {
        if (nameText == null)
            return;

        RectTransform textRect = nameText.rectTransform;
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = new Vector2(0.9767f, 0f);
        textRect.sizeDelta = new Vector2(329.491f, textRect.sizeDelta.y);
    }

    private void ApplyNewsLayout()
    {
        if (nameText == null)
            return;

        RectTransform textRect = nameText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(0f, 0.5f);
        textRect.pivot = new Vector2(0f, 0.5f);
        textRect.anchoredPosition = NewsTextPosition;
        textRect.sizeDelta = new Vector2(NewsTextWidth, textRect.sizeDelta.y);
    }

    private void ClearProfileImage()
    {
        if (profileImage != null)
        {
            profileImage.enabled = false;
            profileImage.sprite = null;
        }

        if (_generatedProfileSprite != null)
        {
            Destroy(_generatedProfileSprite);
            _generatedProfileSprite = null;
        }
    }

    private void SetClickable(bool clickable)
    {
        ClickablePCElement clickableElement = GetComponent<ClickablePCElement>();
        if (clickableElement != null)
            clickableElement.enabled = clickable;
    }

    private void OnDestroy()
    {
        if (_generatedProfileSprite != null)
            Destroy(_generatedProfileSprite);
    }
}
