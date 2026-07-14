using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProfilePage : MonoBehaviour
{
    private const string StatusObjectName = "Status";
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI dateOfBirthText;
    [SerializeField] TextMeshProUGUI genderText;
    [SerializeField] TextMeshProUGUI lastExitText;
    [SerializeField] TextMeshProUGUI reasonText;
    [SerializeField] TextMeshProUGUI idNumberText;
    [SerializeField] Image profileImage;

    [Tooltip("Optional text shown in the profile status field.")]
    [SerializeField] TextMeshProUGUI statusStampText;

    [SerializeField] private ClickablePCElement nextButton;
    [SerializeField] private ClickablePCElement prevButton;

    private void Awake()
    {
        CacheStatusText();
    }

    /// <summary>
    /// Populates the profile page for the given suspect.
    /// </summary>
    /// <param name="suspectData">Suspect whose data is displayed.</param>
    /// <param name="entryReason">Last known entry reason shown on the terminal.</param>
    /// <param name="lastEntryDate">Last known entry date shown on the terminal.</param>
    /// <param name="displayStatus">Status text ready for display.</param>
    public void SetProfileData(SuspectData suspectData, string entryReason = "unknown", string lastEntryDate = "unknown", string displayStatus = "Alive")
    {
        nameText.text = "Name: " + suspectData.FirstName + " " + suspectData.LastName;
        dateOfBirthText.text =  "DoB: " + suspectData.DateOfBirth;
        genderText.text = "Sex: " + suspectData.Sex;
        lastExitText.text = "Last Entry: " + NormalizeUnknown(lastEntryDate);
        if (reasonText != null)
        {
            reasonText.gameObject.SetActive(true);
            reasonText.text = "Reason: " + NormalizeUnknown(entryReason);
        }
        idNumberText.text = "ID:" + suspectData.IDNumber;

        Sprite sprite = Sprite.Create(suspectData.IDPhoto,
            new Rect(0, 0, suspectData.IDPhoto.width, suspectData.IDPhoto.height),
            new Vector2(0.5f, 0.5f));

        profileImage.sprite = sprite;
        profileImage.enabled = true;

        SetStatus(displayStatus);
    }

    public void SetNewsData(TerminalNewsEntry newsEntry)
    {
        NewspaperContentScriptable content = newsEntry?.Content;

        nameText.text = content != null ? content.headerText : "NEWS ENTRY UNAVAILABLE";
        dateOfBirthText.text = newsEntry != null ? "Date: " + newsEntry.Date : "Date: unknown";
        genderText.text = content != null ? content.subheaderText : string.Empty;
        lastExitText.text = string.Empty;
        if (reasonText != null)
        {
            reasonText.gameObject.SetActive(true);
            reasonText.text = content != null ? content.descriptionText : string.Empty;
        }
        idNumberText.text = content != null ? content.footerText : string.Empty;

        if (profileImage != null)
        {
            profileImage.sprite = null;
            profileImage.enabled = false;
        }

        if (statusStampText != null)
        {
            statusStampText.text = string.Empty;
            statusStampText.gameObject.SetActive(false);
        }

        SetNavigationState(false, false);
    }

    public void SetNavigationState(bool canGoPrev, bool canGoNext)
    {
        SetButtonState(prevButton, canGoPrev);
        SetButtonState(nextButton, canGoNext);
    }

    private void SetStatus(string status)
    {
        CacheStatusText();
        if (statusStampText == null)
            return;

        statusStampText.gameObject.SetActive(true);
        statusStampText.text = "Status: " + NormalizeUnknown(status);
    }

    private void CacheStatusText()
    {
        if (statusStampText != null)
            return;

        Transform statusTransform = FindDescendantByName(transform, StatusObjectName);
        if (statusTransform != null)
            statusStampText = statusTransform.GetComponent<TextMeshProUGUI>();
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform result = FindDescendantByName(child, targetName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static string NormalizeUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private void SetButtonState(ClickablePCElement button, bool enabled)
    {
        if (button == null)
            return;

        button.gameObject.SetActive(enabled);
    }
}