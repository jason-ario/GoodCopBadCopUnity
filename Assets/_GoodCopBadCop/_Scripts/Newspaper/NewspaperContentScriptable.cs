using UnityEngine;

[ CreateAssetMenu(fileName = "NewspaperContent", menuName = "NewspaperContent")]
public class NewspaperContentScriptable : ScriptableObject
{
    [TextArea(3, 10)]
    public string headerText; 
    [TextArea(3, 10)]
    public string subheaderText; 
    [TextArea(3, 10)]
    public string descriptionText;
    [TextArea(3, 10)]
    public string footerText;

    [Tooltip("Photo printed on this day's newspaper. Fitted (aspect preserved) into the newspaper's " +
             "image frame. Leave empty to show the controller's fallback image.")]
    public Sprite image;
}
