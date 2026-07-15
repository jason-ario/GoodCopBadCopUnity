using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ProfileView : MonoBehaviour
{
    [SerializeField] private Image photo;
    [SerializeField] private TextMeshProUGUI idNumber;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI dateOfBirth;
    [SerializeField] private TextMeshProUGUI sex;
    [SerializeField] private TextMeshProUGUI status;
    [SerializeField] private TextMeshProUGUI lastEntry;
    [SerializeField] private TextMeshProUGUI reason;

    private Sprite _profileSprite;

    public void Show(SuspectData suspectData, string entryReason, string lastEntryDate, string displayStatus)
    {
        DestroyProfileSprite();

        if (suspectData == null)
            return;

        SetText(nameLabel, "Name: " + suspectData.FirstName + " " + suspectData.LastName);
        SetText(dateOfBirth, "DoB: " + suspectData.DateOfBirth);
        SetText(sex, "Sex: " + suspectData.Sex);
        SetText(status, "Status: " + NormalizeUnknown(displayStatus));
        SetText(lastEntry, "Last Entry: " + NormalizeUnknown(lastEntryDate));
        SetText(reason, "Reason: " + NormalizeUnknown(entryReason));
        SetText(idNumber, "ID:" + suspectData.IDNumber);
        SetPhoto(suspectData.IDPhoto);
    }

    private void SetPhoto(Texture2D texture)
    {
        if (photo == null)
            return;

        if (texture == null)
        {
            photo.sprite = null;
            photo.enabled = false;
            return;
        }

        _profileSprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));

        photo.sprite = _profileSprite;
        photo.enabled = true;
        photo.preserveAspect = true;
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
            label.text = value;
    }

    private static string NormalizeUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private void DestroyProfileSprite()
    {
        if (_profileSprite == null)
            return;

        Destroy(_profileSprite);
        _profileSprite = null;
    }

    private void OnDestroy()
    {
        DestroyProfileSprite();
    }
}
