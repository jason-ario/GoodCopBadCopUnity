using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/Suspect Data")]
public class SuspectData : ScriptableObject
{
    [Header("Suspect Data")]
    public string FirstName;
    public string LastName;
    public string Nickname;
    public string Occupation;
    public string CharacterDescription;
    public string DateOfBirth;
    
    public string Sex;

    public Texture2D IDPhoto;
    public GameObject CharacterPrefab;
    public string[] reasonsForEntry;
    
    [Header("Dialogue")]
    public Response[] dialogueResponses; 
    [TextArea(3, 10)]
    public string entryDialogue;
    public AudioClip[] voiceAudioClips;
    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }
}