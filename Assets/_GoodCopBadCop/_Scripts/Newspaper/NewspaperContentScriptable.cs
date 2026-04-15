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
}
