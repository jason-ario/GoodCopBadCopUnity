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

    private void Awake()
    {
        ResolveReferences();
    }

    public void Show(SuspectData suspectData, string entryReason, string lastEntryDate, string displayStatus)
    {
        ResolveReferences();
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

    private void ResolveReferences()
    {
        if (photo == null)
            photo = FindDescendantByName(transform, "Photo")?.GetComponent<Image>();

        if (idNumber == null)
            idNumber = FindDescendantByName(transform, "ID No")?.GetComponent<TextMeshProUGUI>();

        Transform mainStats = FindDescendantByName(transform, "MainStats");
        Transform statsRoot = mainStats != null ? mainStats : transform;

        if (nameLabel == null)
            nameLabel = FindDescendantByName(statsRoot, "Name")?.GetComponent<TextMeshProUGUI>();

        if (dateOfBirth == null)
            dateOfBirth = FindDescendantByName(statsRoot, "Date of Birth")?.GetComponent<TextMeshProUGUI>();

        if (sex == null)
            sex = FindDescendantByName(statsRoot, "Sex")?.GetComponent<TextMeshProUGUI>();

        if (status == null)
            status = FindDescendantByName(statsRoot, "Status")?.GetComponent<TextMeshProUGUI>();

        if (lastEntry == null)
        {
            lastEntry = FindDescendantByName(transform, "Last Entry")?.GetComponent<TextMeshProUGUI>();
            if (lastEntry == null)
                lastEntry = FindDescendantByName(transform, "Last Exit Date")?.GetComponent<TextMeshProUGUI>();
        }

        if (reason == null)
            reason = FindDescendantByName(transform, "Reason")?.GetComponent<TextMeshProUGUI>();
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