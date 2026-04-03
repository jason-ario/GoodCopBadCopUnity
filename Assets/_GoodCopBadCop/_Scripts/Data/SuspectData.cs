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
    public string EntryPermitExpiryDate;

    [Header("Infection")]
    [Range(0, 100)] public int startingInfectionScore = 0;
    [Min(0)] public int dailyInfectionProgression = 5;

    public Texture2D IDPhoto;
    public SuspectCharacter CharacterPrefab;
    public string[] reasonsForEntry;
    
    [Header("Dialogue")]
    public Response[] dialogueResponses; 
    public string[] entryDialogues;
    public string[] anomalyEntryDialogues;
    public AudioClip[] voiceAudioClips;
    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }

    public string IDNumber;

    [ContextMenu("Set Random ID Number")]
    public void SetRandomIDNumber()
    {
        IDNumber = Random.Range(1000000, 9999999).ToString();
    }
    
}