using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProfilePage : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI dateOfBirthText;
    [SerializeField] TextMeshProUGUI genderText;
    [SerializeField] TextMeshProUGUI lastExitText;
    [SerializeField] TextMeshProUGUI reasonText;
    [SerializeField] TextMeshProUGUI idNumberText;
    [SerializeField] Image profileImage;

    [Tooltip("Optional stamp-style text shown over the profile when a suspect is DECEASED or REPLACED.")]
    [SerializeField] TextMeshProUGUI statusStampText;

    [SerializeField] private ClickablePCElement nextButton;
    [SerializeField] private ClickablePCElement prevButton;

    /// <summary>
    /// Populates the profile page for the given suspect.
    /// </summary>
    /// <param name="suspectData">Suspect whose data is displayed.</param>
    /// <param name="lastExitReason">Last recorded exit reason shown on the terminal.</param>
    /// <param name="lastExitDate">Last recorded exit date shown on the terminal.</param>
    /// <param name="status">Optional status stamp text (e.g. "DECEASED", "REPLACED"). Pass empty to hide.</param>
    public void SetProfileData(SuspectData suspectData, string lastExitReason = "unknown", string lastExitDate = "unknown", string status = "")
    {
        nameText.text = "Name: " + suspectData.FirstName + " " + suspectData.LastName;
        dateOfBirthText.text =  "DoB: " + suspectData.DateOfBirth;
        genderText.text = "Sex: " + suspectData.Sex;
        lastExitText.text = "Last Exit: " + lastExitDate;
        if (reasonText != null)
        {
            bool hasReason = !string.IsNullOrWhiteSpace(lastExitReason);
            reasonText.gameObject.SetActive(hasReason);
            if (hasReason)
                reasonText.text = "Reason: " + lastExitReason;
        }
        idNumberText.text = "ID:" + suspectData.IDNumber;

        Sprite sprite = Sprite.Create(suspectData.IDPhoto,
            new Rect(0, 0, suspectData.IDPhoto.width, suspectData.IDPhoto.height),
            new Vector2(0.5f, 0.5f));

        profileImage.sprite = sprite;
        profileImage.enabled = true;

        // Status stamp (DECEASED / REPLACED)
        if (statusStampText != null)
        {
            statusStampText.text = status;
            statusStampText.gameObject.SetActive(!string.IsNullOrEmpty(status));
        }
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

    private void SetButtonState(ClickablePCElement button, bool enabled)
    {
        if (button == null)
            return;

        button.gameObject.SetActive(enabled);
    }
}