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
        reasonText.text = lastExitReason;
        idNumberText.text = "ID:" + suspectData.IDNumber;
        
        Sprite sprite = Sprite.Create(suspectData.IDPhoto,
            new Rect(0, 0, suspectData.IDPhoto.width, suspectData.IDPhoto.height),
            new Vector2(0.5f, 0.5f));
        
        profileImage.sprite = sprite;

        // Status stamp (DECEASED / REPLACED)
        if (statusStampText != null)
        {
            statusStampText.text = status;
            statusStampText.gameObject.SetActive(!string.IsNullOrEmpty(status));
        }
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
