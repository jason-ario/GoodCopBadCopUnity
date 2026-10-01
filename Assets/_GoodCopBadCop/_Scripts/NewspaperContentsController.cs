using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fills the world-space newspaper canvas for the current day, then snapshots it into the
/// Newspaper render texture with <see cref="camera"/>.
/// </summary>
public class NewspaperContentsController : MonoBehaviour
{
    [SerializeField] private TMP_Text dateText;
    [SerializeField] private TMP_Text headerText; 
    [SerializeField] TMP_Text subheaderText; 
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] TMP_Text footerText;

    [Header("Image")]
    [Tooltip("UI Image that displays each day's newspaper photo. The sprite is assigned as-is; " +
             "size/aspect come from the Image's own RectTransform and settings.")]
    [SerializeField] private Image photoImage;

    [Tooltip("Shown when a day's content has no image assigned.")]
    [SerializeField] private Sprite fallbackImage;

    [Space]
    [SerializeField] private NewspaperContentScriptable[] _newspaperContentScriptables;
    [SerializeField] private GameObject camera;

    private static readonly DateTime StartDate = new DateTime(1989, 10, 20);
    private const string DayNumberKey = "dayNumber";

    private void Awake()
    {
        int day = PlayerPrefs.GetInt(DayNumberKey, 1);
        PopulateFromDay(day);
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftReady += PopulateNewspaperContents;
        ShiftManager.Instance.OnShiftStart += PopulateNewspaperContents;
    }

    /// <summary>
    /// Refreshes the newspaper using the current day from ShiftManager.
    /// Subscribed to OnShiftReady and OnShiftStart.
    /// </summary>
    public void PopulateNewspaperContents()
    {
        PopulateFromDay(ShiftManager.Instance.CurrentDay);
    }

    /// <summary>Number of days with authored newspaper content, for editor tooling bounds-checking.</summary>
    public int DayCount => _newspaperContentScriptables?.Length ?? 0;

    /// <summary>
    /// Populates all newspaper text fields for the given day number, then triggers the runtime
    /// camera-snapshot coroutine.
    /// </summary>
    private void PopulateFromDay(int day)
    {
        SetContentsForDay(day);
        StartCoroutine(CameraSnapshot());
    }

    /// <summary>
    /// Sets all newspaper text fields for the given day number, without starting the runtime
    /// camera-snapshot coroutine (coroutines require Play Mode). Used by <see cref="PopulateFromDay"/>
    /// for the normal runtime flow, and by NewspaperContentsControllerEditor's Edit Mode
    /// "Bake to PNG" tool, which performs its own synchronous capture instead.
    /// </summary>
    public void SetContentsForDay(int day)
    {
        // Reactivate the hidden content (the runtime snapshot routine deactivates it again at the
        // end) so its TMP text is actually renderable while the camera captures it.
        gameObject.SetActive(true);

        Debug.Log("Populating Newspaper Contents");
        int index = day - 1;
        string date = StartDate.AddDays(index).ToString("dd MMM yyyy");
        Debug.Log(index);

        NewspaperContentScriptable newspaperContentScriptable = _newspaperContentScriptables[index];
        dateText.text = date;
        headerText.text = newspaperContentScriptable.headerText;
        subheaderText.text = newspaperContentScriptable.subheaderText;
        descriptionText.text = newspaperContentScriptable.descriptionText;
        footerText.text = newspaperContentScriptable.footerText;
        ApplyImage(newspaperContentScriptable.image);
    }

    /// <summary>
    /// Swaps the day's sprite into <see cref="photoImage"/>. Sizing is left entirely to the Image's
    /// authored RectTransform/settings. Falls back to <see cref="fallbackImage"/> (or hides the Image
    /// if none) when the day has no image.
    /// </summary>
    private void ApplyImage(Sprite daySprite)
    {
        if (photoImage == null) return;

        Sprite sprite = daySprite != null ? daySprite : fallbackImage;
        photoImage.enabled = sprite != null;
        if (sprite == null) return;

        photoImage.sprite = sprite;
    }

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has been submitted,
    /// then deactivates both the camera and this content root — nothing needs to be active (or
    /// rendered) again until the next populate call reactivates it.
    /// </summary>
    IEnumerator CameraSnapshot()
    {
        yield return new WaitForEndOfFrame();
        camera.SetActive(true);
        yield return new WaitForEndOfFrame();
        camera.SetActive(false);
        gameObject.SetActive(false);
    }
}
