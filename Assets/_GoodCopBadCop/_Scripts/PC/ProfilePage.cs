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
    
    [SerializeField] private ClickablePCElement nextButton;
    [SerializeField] private ClickablePCElement prevButton;

    public void SetProfileData(SuspectData suspectData, string lastExitReason = "unknown", string lastExitDate = "unknown")
    {
        nameText.text = "Name: " + suspectData.FirstName + " " + suspectData.LastName;
        dateOfBirthText.text =  "DoB: " + suspectData.DateOfBirth;
        genderText.text = "Sex: " + suspectData.Sex;
        lastExitText.text = "Last Exit: " + lastExitDate;
        reasonText.text = lastExitReason;
        idNumberText.text = "ID:" + suspectData.IDNumber;
        
        Sprite sprite = Sprite.Create(suspectData.IDPhoto,// your Texture2D
            new Rect(0, 0, suspectData.IDPhoto.width, suspectData.IDPhoto.height), // full texture
            new Vector2(0.5f, 0.5f)// pivot (center)
        );
        
        profileImage.sprite = sprite;
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

        // Optional: if you want disabled instead of hidden:
        // button.SetInteractable(enabled);
    }
}
